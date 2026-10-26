using System.Globalization;
using System.Linq;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

/// <summary>Only candidate envelope walls. Windows, unknown boundaries and live closure remain separate.</summary>
public sealed class KsProcgenGroupHullWallResult
{
    public KsProcgenGroupMaterialStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenGroupHullBoundaryResult? Boundary { get; init; }
    public KsProcgenGroupMaterialResult? InteriorMaterials { get; init; }
    public IReadOnlyList<KsProcgenWallChoice> HullWalls { get; init; } = [];
    public int ExpandedCells { get; init; }
    public ulong WallProposalHash { get; init; }
    public bool HasUnresolvedBoundary => Boundary?.HasUnresolvedBoundary ?? true;
    public bool WindowGoalVerified => false;
    public bool GasClosureVerified => false;
    public bool EngineMaterialsVerified => false;
}

/// <summary>
/// Select candidate hull-wall prototypes only for claimable envelope cells. Revalidate complete
/// interior materials and boundary geometry under one graph allowance. Multiple incident room
/// families must nominate the same registered hull-wall entity. This stage does not select windows
/// or repair unknown boundary cells; Planned means a wall proposal, never a complete hull.
/// </summary>
public static class KsProcgenGroupHullWallPlanner
{
    public static KsProcgenGroupHullWallResult Plan(IPrototypeManager prototypeManager, KsProcgenRequest request,
        KsProcgenGroupPartitionResult partition, KsProcgenThemeAssignmentResult themes, int maximumExpandedCells = 262_144)
    {
        var expanded = 0;
        KsProcgenGroupHullWallResult Fail(KsProcgenGroupMaterialStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Owned hull-wall prototype proposal did not complete."), ExpandedCells = expanded,
        };
        if (maximumExpandedCells is < 0 or > 1_000_000)
            return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "InvalidHullWallBudget");
        var materials = KsProcgenGroupMaterialPlanner.Plan(prototypeManager, request, partition, themes,
            maximumExpandedCells: maximumExpandedCells);
        expanded += materials.ExpandedCells;
        if (materials.Status != KsProcgenGroupMaterialStatus.Planned)
            return Fail(materials.Status, materials.Issue!.Code);
        var boundary = KsProcgenGroupHullBoundaryAnalyzer.Analyze(request, partition,
            maximumExpandedCells: maximumExpandedCells - expanded);
        expanded += boundary.ExpandedCells;
        if (boundary.Status != KsProcgenEntranceSceneStatus.Candidate)
            return Fail(boundary.Status == KsProcgenEntranceSceneStatus.BudgetExceeded
                ? KsProcgenGroupMaterialStatus.BudgetExceeded : KsProcgenGroupMaterialStatus.InvalidInput, boundary.Issue!.Code);
        var regionById = themes.Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        var incidentByCell = boundary.Edges.Where(edge => edge.Kind == KsProcgenOwnedBoundaryKind.WritableEnvelope)
            .GroupBy(edge => edge.BoundaryCell).ToDictionary(group => group.Key, group => group.Select(edge => edge.FloorCell).ToHashSet());
        var regionByFloor = themes.Regions.SelectMany(region => region.FloorCells.Select(cell => (cell, region.Id)))
            .ToDictionary(entry => entry.cell, entry => entry.Id);
        var utilityByCell = new Dictionary<Vector2i, HashSet<string>>();
        foreach (var region in themes.Regions.Where(region => region.UtilityRoom != null))
        foreach (var cell in region.UtilityRoom!.ReusedHullCells)
        {
            if (expanded >= maximumExpandedCells)
                return Fail(KsProcgenGroupMaterialStatus.BudgetExceeded, "UtilityHullMaterialBudget");
            expanded++;
            if (!boundary.RequiredEnvelopeCells.Contains(cell))
                return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "StaleUtilityHullMaterialClaim");
            if (!utilityByCell.TryGetValue(cell, out var owners))
                utilityByCell.Add(cell, owners = new(StringComparer.Ordinal));
            owners.Add(region.Id);
        }
        var walls = new List<KsProcgenWallChoice>();
        foreach (var cell in KsProcgenGeometry.SortCells(boundary.RequiredEnvelopeCells))
        {
            if (expanded >= maximumExpandedCells)
                return Fail(KsProcgenGroupMaterialStatus.BudgetExceeded, "HullWallSelectionExpansionBudget");
            expanded++;
            var incidentRegions = incidentByCell.TryGetValue(cell, out var incident) ?
                incident.Select(floor => regionByFloor[floor]) : Enumerable.Empty<string>();
            var utilityRegions = utilityByCell.TryGetValue(cell, out var utilityOwners) ? utilityOwners : Enumerable.Empty<string>();
            var regionIds = incidentRegions.Concat(utilityRegions)
                .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            if (regionIds.Length == 0)
                return Fail(KsProcgenGroupMaterialStatus.InvalidInput, "MissingUtilityHullMaterialOwner");
            string? entityId = null;
            foreach (var regionId in regionIds)
            {
                var theme = regionById[regionId].Theme;
                if (!KsProcgenSelectedContent.TryWall(prototypeManager, theme, out var family) ||
                    string.IsNullOrWhiteSpace(family!.HullWall) || !prototypeManager.TryIndex<EntityPrototype>(family.HullWall, out _))
                    return Fail(KsProcgenGroupMaterialStatus.IncompatibleMaterials, "MissingOwnedHullWallMaterial");
                if (entityId != null && entityId != family.HullWall)
                    return Fail(KsProcgenGroupMaterialStatus.IncompatibleMaterials, "ConflictingOwnedHullWallMaterials");
                entityId = family.HullWall;
            }
            walls.Add(new(cell, regionIds[0], entityId!));
        }
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-group-hull-walls-v1");
        hash.AddString(materials.MaterialHash.ToString(CultureInfo.InvariantCulture));
        hash.AddString(boundary.BoundaryHash.ToString(CultureInfo.InvariantCulture));
        hash.AddInt(walls.Count);
        foreach (var wall in walls)
        {
            hash.AddInt(wall.Cell.X); hash.AddInt(wall.Cell.Y);
            hash.AddString(wall.RegionId); hash.AddString(wall.EntityId);
        }
        return new()
        {
            Status = KsProcgenGroupMaterialStatus.Planned, Boundary = boundary, InteriorMaterials = materials,
            HullWalls = KsProcgenReadOnly.Copy(walls), ExpandedCells = expanded, WallProposalHash = hash.Value,
        };
    }
}
