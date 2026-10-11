using System.Globalization;
using System.Linq;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenGroupWindowStatus : byte
{
    Planned,
    InvalidInput,
    IncompatibleMaterials,
    HardTargetUnmet,
    BudgetExceeded,
}

public sealed record KsProcgenWindowChoice(Vector2i Cell, string RegionId, string EntityId);

/// <summary>Candidate substitutions under caller-declared face context; no live closure or goal certification.</summary>
public sealed class KsProcgenGroupWindowResult
{
    public KsProcgenGroupWindowStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenGroupHullWallResult? SourceHull { get; init; }
    public KsProcgenGroupWindowBoundaryResult? Boundary { get; init; }
    public KsProcgenWindowPlanResult? Selection { get; init; }
    public IReadOnlyList<KsProcgenWallChoice> RemainingWalls { get; init; } = [];
    public IReadOnlyList<KsProcgenWindowChoice> Windows { get; init; } = [];
    public int ExpandedCells { get; init; }
    public ulong WindowProposalHash { get; init; }
    public bool WindowGoalVerified => false;
    public bool GasClosureVerified => false;
    public bool EngineMaterialsVerified => false;
}

/// <summary>
/// Resolve complete owned wall candidates, independently revalidate exterior faces, and replace
/// selected editable walls exactly once. Every incident room must agree on a registered window
/// prototype. Missing families exclude a cell from eligibility; conflicting families fail atomically.
/// Fixed declared windows contribute only counts. SourceHull is diagnostic input, RemainingWalls
/// and Windows are the resulting structural proposals. Neither may yet be published.
/// </summary>
public static class KsProcgenGroupWindowPlanner
{
    public static KsProcgenGroupWindowResult Plan(IPrototypeManager prototypeManager, KsProcgenRequest request,
        KsProcgenGroupPartitionResult partition, KsProcgenThemeAssignmentResult themes,
        IReadOnlyList<KsProcgenExteriorWindowFact> facts, int maximumExpandedCells = 262_144)
    {
        var expanded = 0;
        KsProcgenGroupWindowResult Fail(KsProcgenGroupWindowStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Owned window proposal did not complete."), ExpandedCells = expanded,
        };
        if (maximumExpandedCells is < 0 or > 1_000_000)
            return Fail(KsProcgenGroupWindowStatus.InvalidInput, "InvalidOwnedWindowBudget");
        var hull = KsProcgenGroupHullWallPlanner.Plan(prototypeManager, request, partition, themes,
            maximumExpandedCells: maximumExpandedCells);
        expanded += hull.ExpandedCells;
        if (hull.Status != KsProcgenGroupMaterialStatus.Planned)
            return Fail(hull.Status == KsProcgenGroupMaterialStatus.BudgetExceeded ? KsProcgenGroupWindowStatus.BudgetExceeded :
                hull.Status == KsProcgenGroupMaterialStatus.IncompatibleMaterials ? KsProcgenGroupWindowStatus.IncompatibleMaterials :
                KsProcgenGroupWindowStatus.InvalidInput, hull.Issue!.Code);
        var boundary = KsProcgenGroupWindowBoundaryAnalyzer.Analyze(request, partition, facts,
            maximumExpandedCells: maximumExpandedCells - expanded);
        expanded += boundary.ExpandedCells;
        if (boundary.Status != KsProcgenEntranceSceneStatus.Candidate)
            return Fail(boundary.Status == KsProcgenEntranceSceneStatus.BudgetExceeded ? KsProcgenGroupWindowStatus.BudgetExceeded :
                KsProcgenGroupWindowStatus.InvalidInput, boundary.Issue!.Code);
        if (boundary.HullBoundary!.BoundaryHash != hull.Boundary!.BoundaryHash)
            return Fail(KsProcgenGroupWindowStatus.InvalidInput, "StaleOwnedWindowBoundary");
        var regionByFloor = themes.Regions.SelectMany(region => region.FloorCells.Select(cell => (cell, region)))
            .ToDictionary(entry => entry.cell, entry => entry.region);
        var edgesByCell = boundary.HullBoundary.Edges.GroupBy(edge => edge.BoundaryCell)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var classified = new List<KsProcgenWindowBoundaryCell>();
        var prototypes = new Dictionary<Vector2i, KsProcgenWindowChoice>();
        foreach (var cell in boundary.Cells)
        {
            if (expanded >= maximumExpandedCells)
                return Fail(KsProcgenGroupWindowStatus.BudgetExceeded, "OwnedWindowPrototypeBudget");
            expanded++;
            if (cell.Fixed || !cell.FacesExterior || !cell.SupportsAirtightWindow || cell.IsCorner || cell.RequiredStructure || cell.WindowsDisabled)
            {
                classified.Add(cell);
                continue;
            }
            var regions = edgesByCell[cell.Cell].Select(edge => regionByFloor[edge.FloorCell])
                .DistinctBy(region => region.Id).OrderBy(region => region.Id, StringComparer.Ordinal).ToArray();
            string? entityId = null;
            var supported = true;
            foreach (var region in regions)
            {
                var family = prototypeManager.Index<KsProcgenWallPackPrototype>(region.Theme.WallPackId).Families
                    .Single(candidate => candidate.Id == region.Theme.WallFamilyId);
                if (string.IsNullOrWhiteSpace(family.Window) || !prototypeManager.TryIndex<EntityPrototype>(family.Window, out _))
                {
                    supported = false;
                    continue;
                }
                if (entityId != null && entityId != family.Window)
                    return Fail(KsProcgenGroupWindowStatus.IncompatibleMaterials, "ConflictingOwnedWindowMaterials");
                entityId = family.Window;
            }
            classified.Add(cell with { SupportsAirtightWindow = supported });
            if (supported) prototypes.Add(cell.Cell, new(cell.Cell, regions[0].Id, entityId!));
        }
        var selection = KsProcgenWindowPlanner.Plan(classified, request.Seed, request.WindowGoal);
        if (selection.Status is KsProcgenWindowPlanStatus.InvalidInput or KsProcgenWindowPlanStatus.HardTargetUnmet)
            return Fail(selection.Status == KsProcgenWindowPlanStatus.HardTargetUnmet ? KsProcgenGroupWindowStatus.HardTargetUnmet :
                KsProcgenGroupWindowStatus.InvalidInput, selection.Issue!.Code);
        var chosen = selection.ChosenWindowCells.ToHashSet();
        var walls = hull.InteriorMaterials!.Walls.Concat(hull.HullWalls).Where(wall => !chosen.Contains(wall.Cell))
            .OrderBy(wall => wall.Cell.Y).ThenBy(wall => wall.Cell.X).ToReadOnlyList();
        var windows = selection.ChosenWindowCells.Select(cell => prototypes[cell]).ToReadOnlyList();
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-group-windows-v1");
        hash.AddString(hull.WallProposalHash.ToString(CultureInfo.InvariantCulture));
        hash.AddString(boundary.ClassificationHash.ToString(CultureInfo.InvariantCulture));
        hash.AddInt(request.Seed); hash.AddInt(BitConverter.SingleToInt32Bits(request.WindowGoal.ExteriorWindowFraction));
        hash.AddInt(request.WindowGoal.HardFraction ? 1 : 0); hash.AddInt(request.WindowGoal.ToleranceCells);
        hash.AddInt(request.WindowGoal.MinimumCount); hash.AddInt(request.WindowGoal.MaximumCount ?? -1);
        hash.AddInt((int) selection.Status); hash.AddInt(selection.EligibleCells); hash.AddInt(selection.FixedWindowCells);
        hash.AddInt(selection.RequestedWindowCells); hash.AddInt(selection.AchievedWindowCells);
        hash.AddInt(classified.Count);
        foreach (var cell in classified)
        {
            AddCell(cell.Cell); hash.AddInt(cell.SupportsAirtightWindow ? 1 : 0);
            hash.AddString(prototypes.TryGetValue(cell.Cell, out var candidate) ? candidate.EntityId : "");
        }
        hash.AddInt(walls.Count);
        foreach (var wall in walls) { AddCell(wall.Cell); hash.AddString(wall.RegionId); hash.AddString(wall.EntityId); }
        hash.AddInt(windows.Count);
        foreach (var window in windows) { AddCell(window.Cell); hash.AddString(window.RegionId); hash.AddString(window.EntityId); }
        void AddCell(Vector2i cell) { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
        return new()
        {
            Status = KsProcgenGroupWindowStatus.Planned, SourceHull = hull, Boundary = boundary, Selection = selection,
            RemainingWalls = walls, Windows = windows, ExpandedCells = expanded, WindowProposalHash = hash.Value,
        };
    }
}
