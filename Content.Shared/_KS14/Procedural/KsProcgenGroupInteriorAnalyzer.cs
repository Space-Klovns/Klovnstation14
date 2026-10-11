using System.Collections.Frozen;
using System.Globalization;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public sealed class KsProcgenGroupInteriorResult
{
    public KsProcgenEntranceSceneStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenEntranceScene? Scene { get; init; }
    public KsProcgenEntranceSceneResult? Analysis { get; init; }
    public IReadOnlySet<Vector2i> BlockedCells { get; init; } = FrozenSet<Vector2i>.Empty;
    public int ExpandedCells { get; init; }
    public ulong AccessHash { get; init; }
    public bool EngineAccessVerified => false;
}

/// <summary>
/// Independently audit complete owned interior proposals. Fixtures are conservatively solid and
/// vault-required furniture blocks clean walking. This never inspects actual actors or light physics.
/// </summary>
public static class KsProcgenGroupInteriorAnalyzer
{
    private static readonly Vector2i[] Cardinal = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public static KsProcgenGroupInteriorResult Analyze(KsProcgenRequest request, KsProcgenGroupPartitionResult partition,
        KsProcgenThemeAssignmentResult themes, IReadOnlyList<KsProcgenFurnishedRegion> furnishings,
        IReadOnlyList<KsProcgenLitRegion> lighting, int maximumExpandedCells = 262_144)
    {
        var expanded = 0;
        KsProcgenGroupInteriorResult Fail(KsProcgenEntranceSceneStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Owned interior access audit did not validate."), ExpandedCells = expanded,
        };
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out var issue))
            return new() { Status = KsProcgenEntranceSceneStatus.InvalidInput, Issue = issue };
        if (partition == null || partition.Status != KsProcgenEntranceSceneStatus.Candidate || partition.Scene == null ||
            partition.Analysis?.Status != KsProcgenEntranceSceneStatus.Candidate || partition.PartitionHash == 0 ||
            shape!.EntranceContract == null || themes?.Status != KsProcgenThemeAssignmentStatus.Selected ||
            themes.Regions.Count is < 1 or > 256 || furnishings == null || lighting == null ||
            furnishings.Count != themes.Regions.Count || lighting.Count != themes.Regions.Count ||
            partition.FloorCells.Count > 4_096 || partition.Regions.Count is < 1 or > 256 ||
            partition.Regions.Sum(network => (long) network.Partition.DoorOpenings.Count) > 4_096 ||
            maximumExpandedCells is < 0 or > 1_000_000)
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "InvalidGroupInteriorInput");
        var initial = KsProcgenEntranceSceneAnalyzer.AnalyzeNormalized(shape, request.ConnectivityPolicy, request.RootCells,
            partition.Analysis.ConfigurationId!, partition.Scene, Math.Min(131_072, maximumExpandedCells));
        expanded += initial.ExpandedCells;
        if (initial.Status != KsProcgenEntranceSceneStatus.Candidate)
            return Fail(initial.Status, initial.Issue!.Code);
        if (initial.SceneHash != partition.Analysis.SceneHash || !partition.Scene.CleanDomainCells.SetEquals(partition.FloorCells))
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "StaleGroupInteriorSource");
        var furnishingById = new Dictionary<string, KsProcgenFurnishingResult>(StringComparer.Ordinal);
        var lightingById = new Dictionary<string, KsProcgenLightingPlanResult>(StringComparer.Ordinal);
        foreach (var region in furnishings)
            if (region == null || region.Proposal == null || !furnishingById.TryAdd(region.RegionId, region.Proposal))
                return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "DuplicateInteriorRegion");
        foreach (var region in lighting)
            if (region == null || region.Proposal == null || !lightingById.TryAdd(region.RegionId, region.Proposal))
                return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "DuplicateInteriorRegion");
        var regionIds = new HashSet<string>(StringComparer.Ordinal);
        var covered = new HashSet<Vector2i>();
        var occupied = new HashSet<Vector2i>();
        var blocked = new HashSet<Vector2i>();
        var terminalsByRegion = new Dictionary<string, HashSet<Vector2i>>(StringComparer.Ordinal);
        var floorByRegion = new Dictionary<string, HashSet<Vector2i>>(StringComparer.Ordinal);
        var doorTerminals = partition.Regions.SelectMany(network => network.Partition.DoorOpenings)
            .SelectMany(door => new[] { door.Threshold, door.InsideApproach, door.OutsideApproach }).ToHashSet();
        foreach (var region in themes.Regions.OrderBy(region => region.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(region.Id) || !regionIds.Add(region.Id) ||
                !furnishingById.TryGetValue(region.Id, out var furniture) || !lightingById.TryGetValue(region.Id, out var lights) ||
                furniture.Status is not (KsProcgenFurnishingStatus.Proposed or KsProcgenFurnishingStatus.Sparse) ||
                lights.Status is not (KsProcgenLightingPlanStatus.Proposed or KsProcgenLightingPlanStatus.Sparse or KsProcgenLightingPlanStatus.NotRequested) ||
                furniture.Entities.Count > 4_096 || lights.Lights.Count > 4_096 || region.FloorCells.Count is < 1 or > 4_096 ||
                furniture.ProtectedPassageCells.Count > 4_096 || region.ProtectedPassageCells.Count > 4_096)
                return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "InvalidInteriorRegion");
            var floor = region.FloorCells.ToHashSet();
            var owner = initial.Components.SingleOrDefault(component => component.Cells.Contains(region.FloorCells[0]));
            if (floor.Count != region.FloorCells.Count || owner == null || floor.Any(cell => !owner.Cells.Contains(cell) || !covered.Add(cell)) ||
                region.EntranceGroupIds.Count != owner.GroupIds.Count || !region.EntranceGroupIds.ToHashSet(StringComparer.Ordinal).SetEquals(owner.GroupIds) ||
                !region.ProtectedPassageCells.ToHashSet().SetEquals(floor.Where(partition.ProtectedCells.Contains)) ||
                region.ProtectedPassageCells.Any(cell => !furniture.ProtectedPassageCells.Contains(cell)) ||
                furniture.ProtectedPassageCells.Any(cell => !floor.Contains(cell)))
                return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "InvalidInteriorOwnership");
            var terminals = furniture.ProtectedPassageCells.ToHashSet();
            terminals.UnionWith(doorTerminals.Where(floor.Contains));
            foreach (var entity in furniture.Entities)
            {
                if (!Enum.IsDefined(entity.Movement) || entity.OccupiedCells.Count > 256 ||
                    entity.OccupiedCells.Any(cell => !floor.Contains(cell) || !shape.CanWrite(cell) || !occupied.Add(cell)))
                    return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "InvalidInteriorEntityFootprint");
                if (entity.Movement != KsProcgenMovementClass.Clear)
                    blocked.UnionWith(entity.OccupiedCells);
                if (entity.InteractionApproach is { } approach)
                {
                    if (!floor.Contains(approach))
                        return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "ForeignInteriorInteractionApproach");
                    terminals.Add(approach);
                }
            }
            foreach (var light in lights.Lights)
            {
                if (!floor.Contains(light.Cell) || !shape.CanWrite(light.Cell) || !occupied.Add(light.Cell))
                    return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "InvalidInteriorLightFootprint");
                blocked.Add(light.Cell);
            }
            if (lights.Status == KsProcgenLightingPlanStatus.NotRequested && lights.Lights.Count != 0)
                return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "UnexpectedInteriorLights");
            floorByRegion.Add(region.Id, floor);
            terminalsByRegion.Add(region.Id, terminals);
        }
        if (!covered.SetEquals(partition.FloorCells))
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "IncompleteInteriorCoverage");
        if (partition.ProtectedCells.Any(blocked.Contains))
            return Fail(KsProcgenEntranceSceneStatus.Rejected, "OccupiedProtectedEntranceRoute");
        // Check each room independently so operating approaches cannot rely on a detour outside it.
        foreach (var regionId in regionIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            var clean = floorByRegion[regionId].Where(cell => !blocked.Contains(cell)).ToHashSet();
            var terminals = terminalsByRegion[regionId];
            if (clean.Count == 0 || terminals.Any(cell => !clean.Contains(cell)))
                return Fail(KsProcgenEntranceSceneStatus.Rejected, "BlockedInteriorApproach");
            var start = KsProcgenGeometry.SortCells(clean)[0];
            var reached = new HashSet<Vector2i> { start };
            var queue = new Queue<Vector2i>();
            queue.Enqueue(start);
            while (queue.TryDequeue(out var cell))
            {
                if (expanded >= maximumExpandedCells)
                    return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "GroupInteriorExpansionBudget");
                expanded++;
                foreach (var step in Cardinal)
                    if (clean.Contains(cell + step) && reached.Add(cell + step)) queue.Enqueue(cell + step);
            }
            if (reached.Count != clean.Count)
                return Fail(KsProcgenEntranceSceneStatus.Rejected, "DisconnectedInteriorRoom");
        }
        var scene = new KsProcgenEntranceScene(partition.FloorCells.Where(cell => !blocked.Contains(cell)).ToFrozenSet(),
            partition.Scene.CleanHostCells.ToFrozenSet(), partition.Scene.BlockedThresholdCells.ToFrozenSet(), partition.Scene.HostContextComplete);
        var analysis = KsProcgenEntranceSceneAnalyzer.AnalyzeNormalized(shape, request.ConnectivityPolicy, request.RootCells,
            partition.Analysis.ConfigurationId!, scene, Math.Min(131_072, maximumExpandedCells - expanded));
        expanded += analysis.ExpandedCells;
        if (analysis.Status != KsProcgenEntranceSceneStatus.Candidate)
            return Fail(analysis.Status, analysis.Issue!.Code);
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-group-interior-access-v1");
        hash.AddString(partition.PartitionHash.ToString(CultureInfo.InvariantCulture));
        hash.AddString(analysis.SceneHash.ToString(CultureInfo.InvariantCulture));
        foreach (var regionId in regionIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            hash.AddString(regionId);
            var terminals = KsProcgenGeometry.SortCells(terminalsByRegion[regionId]);
            hash.AddInt(terminals.Count);
            foreach (var cell in terminals) { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
        }
        return new() { Status = KsProcgenEntranceSceneStatus.Candidate, Scene = scene, Analysis = analysis,
            BlockedCells = blocked.ToFrozenSet(), ExpandedCells = expanded, AccessHash = hash.Value };
    }
}
