using System.Collections.Frozen;
using System.Globalization;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>All partition fallbacks are confined to this connected entrance network.</summary>
public sealed record KsProcgenOwnedPartition(string Id, IReadOnlyList<string> GroupIds,
    KsProcgenPureFillResult Fill, KsProcgenPartitionResult Partition, IReadOnlyList<Vector2i> ProtectedCells);

/// <summary>Nonreusable diagnostics for an attempted quota, orientation and width variant.</summary>
public sealed record KsProcgenSizeGoalPartitionAttempt(bool AcrossX, KsProcgenPartitionStatus Status,
    string? ReasonCode, int ExpandedCells, int SeamProbes, int MergeAttempts)
{
    public int WidthVariant { get; init; }
    public string NetworkId { get; init; } = string.Empty;
    public int AllocationOrdinal { get; init; }
    public bool Reflected { get; init; }
}

public sealed class KsProcgenGroupPartitionResult
{
    public KsProcgenEntranceSceneStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenOwnedPartition> Regions { get; init; } = [];
    public IReadOnlyList<Vector2i> FloorCells { get; init; } = [];
    public IReadOnlyList<Vector2i> WallCells { get; init; } = [];
    public IReadOnlySet<Vector2i> ProtectedCells { get; init; } = FrozenSet<Vector2i>.Empty;
    public KsProcgenEntranceScene? Scene { get; init; }
    public KsProcgenEntranceSceneResult? Analysis { get; init; }
    public int ExpandedCells { get; init; }
    public int MergeAttempts { get; init; }
    public int SeamProbes { get; init; }
    public ulong PartitionHash { get; init; }
    public bool SizeGoalSubdivisionUsed { get; init; }
    public int SizeGoalQuotaProbes { get; init; }
    public int SizeGoalFitProbes { get; init; }
    public int SizeGoalAllocationAttempts { get; init; }
    public IReadOnlyList<KsProcgenSizeGoalPartitionAttempt> SizeGoalAttempts { get; init; } = [];
    public bool EngineAccessVerified => false;
}

/// <summary>
/// Validates a floor proposal, proposes logical rooms, solves seams separately within each entrance
/// component, and independently validates the final scene. Connectivity floods share the graph
/// allowance; ownership/interface/door work shares a separate seam probe allowance across networks.
/// Allocation, sorting and other non-graph work remain bounded by input limits.
/// </summary>
public static class KsProcgenGroupPartitionPlanner
{
    private static readonly Vector2i[] Cardinal = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public static KsProcgenGroupPartitionResult Plan(KsProcgenRequest request, KsProcgenGroupFloorResult floor,
        int preferredMaxRoomCells = 24, int minimumRoomCells = 4, int maximumMergeAttempts = 64,
        int maximumExpandedCells = 262_144, IReadOnlyList<KsProcgenZone>? zones = null,
        int maximumSeamProbes = 262_144, bool useSizeGoals = false)
    {
        var expanded = 0;
        var merges = 0;
        var seamProbes = 0;
        var sizeGoalSubdivisionUsed = false;
        var quotaProbes = 0;
        var fitProbes = 0;
        var allocationAttempts = 0;
        var allocationId = string.Empty;
        var sizeGoalAttempts = new List<KsProcgenSizeGoalPartitionAttempt>();
        KsProcgenGroupPartitionResult Fail(KsProcgenEntranceSceneStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Entrance-aware partition proposal did not validate."),
            ExpandedCells = expanded, MergeAttempts = merges, SeamProbes = seamProbes,
            SizeGoalAttempts = KsProcgenReadOnly.Copy(sizeGoalAttempts),
            SizeGoalQuotaProbes = quotaProbes, SizeGoalFitProbes = fitProbes,
            SizeGoalAllocationAttempts = allocationAttempts,
        };
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out var issue))
            return new() { Status = KsProcgenEntranceSceneStatus.InvalidInput, Issue = issue };
        if (floor == null || floor.Status != KsProcgenEntranceSceneStatus.Candidate || floor.Scene == null ||
            floor.Analysis?.Status != KsProcgenEntranceSceneStatus.Candidate || floor.Routing == null ||
            floor.FloorHash == 0 || shape!.EntranceContract == null || request.Mode != KsProcgenMode.Procedural ||
            request.GeometryMode != KsProcgenGeometryMode.Footprint ||
            preferredMaxRoomCells < minimumRoomCells || minimumRoomCells < 1 || preferredMaxRoomCells > 4_096 ||
            maximumMergeAttempts is < 0 or > 64 || maximumExpandedCells is < 0 or > 1_000_000 ||
            floor.FloorCells.Count > 4_096 || zones?.Count > 256 || maximumSeamProbes is < 0 or > 1_000_000)
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "InvalidGroupPartitionInput");
        var configurationId = floor.Analysis.ConfigurationId!;
        var initial = KsProcgenEntranceSceneAnalyzer.AnalyzeNormalized(shape, request.ConnectivityPolicy, request.RootCells,
            configurationId, floor.Scene, Math.Min(131_072, maximumExpandedCells));
        expanded += initial.ExpandedCells;
        if (initial.Status != KsProcgenEntranceSceneStatus.Candidate)
            return Fail(initial.Status, initial.Issue!.Code);
        var sourceHash = KsProcgenStableHash.Create();
        sourceHash.AddString("ks-procgen-group-floor-v1");
        sourceHash.AddString(request.RequestId);
        sourceHash.AddInt(request.Seed);
        sourceHash.AddString(floor.Routing.RouteHash.ToString(CultureInfo.InvariantCulture));
        sourceHash.AddString(initial.SceneHash.ToString(CultureInfo.InvariantCulture));
        var initialFloor = floor.FloorCells.ToHashSet();
        if (sourceHash.Value != floor.FloorHash || initial.SceneHash != floor.Analysis.SceneHash ||
            initialFloor.Count != floor.FloorCells.Count || !initialFloor.SetEquals(floor.Scene.CleanDomainCells) ||
            !floor.ProtectedCells.SetEquals(floor.Routing.FloorCells) || !floor.ProtectedCells.IsSubsetOf(initialFloor) ||
            !floor.WallCells.SequenceEqual(KsProcgenGeometry.SortCells(shape.TargetCells.Where(cell => shape.CanWrite(cell) && !initialFloor.Contains(cell)))))
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "StaleGroupPartitionSource");
        // Reconstruct ownership from inspected graph witnesses, not caller-supplied region labels.
        var ownerByCell = initial.Components.SelectMany(component => component.Cells.Select(cell => (cell, component.Component)))
            .ToDictionary(entry => entry.cell, entry => entry.Component);
        var assigned = new Dictionary<int, List<KsProcgenZone>>();
        var goalLayouts = new Dictionary<int, IReadOnlyList<KsProcgenSizeGoalLayout>>();
        foreach (var component in initial.Components)
            assigned.Add(component.Component, new List<KsProcgenZone>());
        if (zones != null)
        {
            var covered = new HashSet<Vector2i>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var zone in zones)
            {
                if (zone == null || string.IsNullOrWhiteSpace(zone.Id) || !ids.Add(zone.Id) || zone.Cells.Count == 0 ||
                    !Enum.IsDefined(zone.Kind) || zone.Cells.Any(cell => !ownerByCell.ContainsKey(cell) || !covered.Add(cell)))
                    return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "InvalidOwnedPartitionZones");
                var owner = ownerByCell[zone.Cells[0]];
                if (zone.Cells.Any(cell => ownerByCell[cell] != owner))
                    return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "CrossNetworkPartitionZone");
                var validationWork = new KsProcgenPartitionWorkBudget(
                    maximumExpandedCells: maximumExpandedCells - expanded, maximumSeamProbes: 0);
                var connected = validationWork.TryConnectedComponents(zone.Cells.ToHashSet(), out var zoneComponents);
                expanded += validationWork.ExpandedCells;
                if (!connected)
                    return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "GroupPartitionExpansionBudget");
                if (zoneComponents.Count != 1)
                    return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "CrossNetworkPartitionZone");
                assigned[owner].Add(zone with { Cells = KsProcgenGeometry.SortCells(zone.Cells) });
            }
            if (!covered.SetEquals(initialFloor))
                return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "IncompleteOwnedPartitionZones");
        }
        else
        {
            if (useSizeGoals && request.SizeMix.Count != 0 && initial.Components.Count > 1)
            {
                foreach (var component in initial.Components)
                    foreach (var ignored in component.Cells)
                    {
                        if (expanded >= maximumExpandedCells)
                            return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "GroupPartitionExpansionBudget");
                        expanded++;
                    }
                var allocation = KsProcgenSizeGoalAllocator.Allocate(request.RequestId, initial.Components,
                    request.SizeMix, minimumRoomCells);
                quotaProbes = allocation.QuotaProbes;
                fitProbes = allocation.FitProbes;
                allocationId = allocation.AllocationId;
                foreach (var entry in allocation.Layouts) goalLayouts.Add(entry.Key, entry.Value);
            }
            var zoneCount = 0;
            foreach (var component in initial.Components)
            {
                if (goalLayouts.TryGetValue(component.Component, out var allocated))
                {
                    assigned[component.Component].AddRange(allocated[0].Zones);
                    sizeGoalSubdivisionUsed = true;
                    continue;
                }
                if (useSizeGoals && request.SizeMix.Count != 0 && initial.Components.Count == 1)
                {
                    // Charge every inspected witness cell even when rectangular fitting fails.
                    foreach (var ignored in component.Cells)
                    {
                        if (expanded >= maximumExpandedCells)
                            return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "GroupPartitionExpansionBudget");
                        expanded++;
                    }
                    var layouts = KsProcgenSizeGoalSubdivision.Propose(
                        $"{request.RequestId}/EntranceNetwork/{component.Component}", component.Cells,
                        request.SizeMix, minimumRoomCells);
                    if (layouts.Count != 0)
                    {
                        assigned[component.Component].AddRange(layouts[0].Zones);
                        goalLayouts.Add(component.Component, layouts);
                        sizeGoalSubdivisionUsed = true;
                        continue;
                    }
                }
                var remaining = component.Cells.ToHashSet();
                var index = 0;
                while (remaining.Count != 0)
                {
                    if (++zoneCount > 256)
                        return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "GroupPartitionZoneBudget");
                    var cells = new List<Vector2i>();
                    var frontier = new Queue<Vector2i>();
                    var start = KsProcgenGeometry.SortCells(remaining)[0];
                    remaining.Remove(start);
                    frontier.Enqueue(start);
                    while (frontier.TryDequeue(out var cell))
                    {
                        if (expanded >= maximumExpandedCells)
                            return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "GroupPartitionExpansionBudget");
                        expanded++;
                        cells.Add(cell);
                        if (cells.Count >= preferredMaxRoomCells)
                            break;
                        foreach (var step in Cardinal)
                            if (remaining.Remove(cell + step))
                                frontier.Enqueue(cell + step);
                    }
                    while (frontier.TryDequeue(out var deferred))
                        remaining.Add(deferred);
                    var entireNetworkIsTiny = component.Cells.Count < minimumRoomCells || !component.Cells.Any(cell =>
                        initialFloor.Contains(cell + new Vector2i(1, 0)) && initialFloor.Contains(cell + new Vector2i(0, 1)) &&
                        initialFloor.Contains(cell + new Vector2i(1, 1)));
                    assigned[component.Component].Add(new($"{request.RequestId}/EntranceNetwork/{component.Component}/Zone/{index++}",
                        entireNetworkIsTiny ? KsProcgenZoneKind.Passage : KsProcgenZoneKind.RoomProposal,
                        KsProcgenGeometry.SortCells(cells)));
                }
            }
        }
        var regions = new List<KsProcgenOwnedPartition>();
        var walls = floor.WallCells.ToHashSet();
        var finalFloor = initialFloor.ToHashSet();
        var seamWork = new KsProcgenPartitionWorkBudget(maximumExpandedCells: maximumExpandedCells - expanded,
            maximumSeamProbes: maximumSeamProbes);
        var rejectedAllocations = new HashSet<string>(StringComparer.Ordinal);
        List<KsProcgenOwnedPartition>? firstFallbackRegions = null;
        HashSet<Vector2i>? firstFallbackWalls = null;
        HashSet<Vector2i>? firstFallbackFloor = null;
        while (true)
        {
            if (allocationId.Length != 0) allocationAttempts++;
            foreach (var component in initial.Components)
            {
                var componentZones = assigned[component.Component].OrderBy(zone => zone.Id, StringComparer.Ordinal).ToReadOnlyList();
                var protectedCells = component.Cells.Where(cell => floor.ProtectedCells.Contains(cell) || !shape.CanWrite(cell)).ToFrozenSet();
                var alternatives = goalLayouts.TryGetValue(component.Component, out var candidates)
                    ? candidates : new[] { new KsProcgenSizeGoalLayout(true, componentZones) };
                KsProcgenPureFillResult fill = null!;
                KsProcgenPartitionResult partition = null!;
                KsProcgenPureFillResult? fallbackFill = null;
                KsProcgenPartitionResult? fallbackPartition = null;
                foreach (var layout in alternatives)
                {
                    fill = new() { Status = KsProcgenPureFillStatus.Proposed,
                        Zones = layout.Zones, ProceduralCells = component.Cells.Count };
                    partition = KsProcgenPartitionPlanner.Plan(fill, layout.Zones.Where(zone => zone.Kind == KsProcgenZoneKind.Passage)
                        .SelectMany(zone => zone.Cells).ToFrozenSet(), maxMergeAttempts: maximumMergeAttempts - merges,
                        protectedFloorCells: protectedCells, workBudget: seamWork);
                    expanded += partition.ExpandedCells;
                    seamProbes += partition.SeamProbes;
                    merges += partition.MergeAttempts;
                    if (candidates != null)
                        sizeGoalAttempts.Add(new(layout.AcrossX, partition.Status, partition.Issue?.Code,
                            partition.ExpandedCells, partition.SeamProbes, partition.MergeAttempts)
                            { WidthVariant = layout.WidthVariant,
                                Reflected = layout.Reflected,
                                AllocationOrdinal = allocationAttempts == 0 ? 0 : allocationAttempts - 1,
                                NetworkId = $"{request.RequestId}/EntranceNetwork/{component.Component}" });
                    if (partition.Status == KsProcgenPartitionStatus.BudgetExceeded)
                        return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, partition.Issue!.Code);
                    if (partition.Status == KsProcgenPartitionStatus.InvalidInput || partition.WallCells.Any(cell => !shape.CanWrite(cell)) ||
                        protectedCells.Any(cell => !partition.FloorCells.Contains(cell)))
                        return Fail(KsProcgenEntranceSceneStatus.Rejected, "InvalidOwnedPartitionProposal");
                    if (partition.Status == KsProcgenPartitionStatus.Proposed) break;
                    fallbackFill ??= fill;
                    fallbackPartition ??= partition;
                }
                // Prefer an unmerged alternative, then retain the first fallback deterministically.
                // Failed orientations still consume the shared allowances and remain in diagnostics.
                if (partition.Status != KsProcgenPartitionStatus.Proposed && fallbackPartition != null)
                {
                    fill = fallbackFill!;
                    partition = fallbackPartition;
                }
                // Freeze even legacy solver lists before exposing an accepted network artifact.
                partition = new()
                {
                    Status = partition.Status, Issue = partition.Issue, FloorCells = KsProcgenReadOnly.Copy(partition.FloorCells),
                    WallCells = KsProcgenReadOnly.Copy(partition.WallCells), DoorOpenings = KsProcgenReadOnly.Copy(partition.DoorOpenings),
                    MergedZoneGroups = partition.MergedZoneGroups.Select(KsProcgenReadOnly.Copy).ToReadOnlyList(),
                    RejectedSplits = partition.RejectedSplits, ExpandedCells = partition.ExpandedCells,
                    SeamProbes = partition.SeamProbes, MergeAttempts = partition.MergeAttempts,
                };
                walls.UnionWith(partition.WallCells);
                finalFloor.ExceptWith(partition.WallCells);
                regions.Add(new($"{request.RequestId}/EntranceNetwork/{component.Component}", component.GroupIds,
                    fill, partition, KsProcgenGeometry.SortCells(protectedCells)));
            }
            if (allocationId.Length == 0 || regions.All(region => region.Partition.Status == KsProcgenPartitionStatus.Proposed))
                break;
            firstFallbackRegions ??= regions;
            firstFallbackWalls ??= walls;
            firstFallbackFloor ??= finalFloor;
            rejectedAllocations.Add(allocationId);
            // Complete assignments are retried atomically. Reinspection/repeated fits still count
            // against the same metadata caps; all seam solves retain the original shared budget.
            var next = allocationAttempts >= 8 ? null : KsProcgenSizeGoalAllocator.Allocate(request.RequestId,
                initial.Components, request.SizeMix, minimumRoomCells, rejectedAllocations: rejectedAllocations,
                previousQuotaProbes: quotaProbes, previousFitProbes: fitProbes);
            if (next != null)
            {
                quotaProbes = next.QuotaProbes;
                fitProbes = next.FitProbes;
            }
            if (next == null || next.Layouts.Count == 0)
            {
                // Preserve the first complete fallback, including its matching region geometry.
                regions = firstFallbackRegions;
                walls = firstFallbackWalls!;
                finalFloor = firstFallbackFloor!;
                break;
            }
            allocationId = next.AllocationId;
            goalLayouts.Clear();
            foreach (var entry in next.Layouts)
            {
                goalLayouts.Add(entry.Key, entry.Value);
                assigned[entry.Key] = entry.Value[0].Zones.ToList();
            }
            regions = new List<KsProcgenOwnedPartition>();
            walls = floor.WallCells.ToHashSet();
            finalFloor = initialFloor.ToHashSet();
        }
        var scene = new KsProcgenEntranceScene(finalFloor.ToFrozenSet(), floor.Scene.CleanHostCells.ToFrozenSet(),
            floor.Scene.BlockedThresholdCells.ToFrozenSet(), floor.Scene.HostContextComplete);
        var analysis = KsProcgenEntranceSceneAnalyzer.AnalyzeNormalized(shape, request.ConnectivityPolicy, request.RootCells,
            configurationId, scene, Math.Min(131_072, maximumExpandedCells - expanded));
        expanded += analysis.ExpandedCells;
        if (analysis.Status != KsProcgenEntranceSceneStatus.Candidate)
            return Fail(analysis.Status, analysis.Issue!.Code);
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-group-partition-v1");
        hash.AddString(floor.FloorHash.ToString(CultureInfo.InvariantCulture));
        hash.AddString(analysis.SceneHash.ToString(CultureInfo.InvariantCulture));
        foreach (var region in regions)
        {
            hash.AddString(region.Id);
            hash.AddInt((int) region.Partition.Status);
            hash.AddInt(region.GroupIds.Count);
            foreach (var groupId in region.GroupIds) hash.AddString(groupId);
            hash.AddInt(region.ProtectedCells.Count);
            foreach (var cell in region.ProtectedCells) { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
            hash.AddInt(region.Fill.Zones.Count);
            foreach (var zone in region.Fill.Zones)
            {
                hash.AddString(zone.Id);
                hash.AddInt((int) zone.Kind);
                hash.AddString(zone.SizeGoalId ?? "");
                hash.AddInt(zone.Cells.Count);
                foreach (var cell in zone.Cells) { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
            }
            hash.AddInt(region.Partition.MergedZoneGroups.Count);
            foreach (var merged in region.Partition.MergedZoneGroups)
            {
                hash.AddInt(merged.Count);
                foreach (var id in merged) hash.AddString(id);
            }
            hash.AddInt(region.Partition.DoorOpenings.Count);
            foreach (var door in region.Partition.DoorOpenings)
            {
                hash.AddString(door.FirstZoneId); hash.AddString(door.SecondZoneId);
                hash.AddInt(door.Threshold.X); hash.AddInt(door.Threshold.Y);
                hash.AddInt(door.InsideApproach.X); hash.AddInt(door.InsideApproach.Y);
                hash.AddInt(door.OutsideApproach.X); hash.AddInt(door.OutsideApproach.Y);
            }
        }
        return new()
        {
            Status = KsProcgenEntranceSceneStatus.Candidate, Regions = regions.ToReadOnlyList(), FloorCells = KsProcgenGeometry.SortCells(finalFloor),
            WallCells = KsProcgenGeometry.SortCells(walls), ProtectedCells = regions.SelectMany(region => region.ProtectedCells).ToFrozenSet(),
            Scene = scene, Analysis = analysis, ExpandedCells = expanded, MergeAttempts = merges,
            SeamProbes = seamProbes, PartitionHash = hash.Value,
            SizeGoalSubdivisionUsed = sizeGoalSubdivisionUsed,
            SizeGoalQuotaProbes = quotaProbes, SizeGoalFitProbes = fitProbes,
            SizeGoalAllocationAttempts = allocationAttempts,
            SizeGoalAttempts = KsProcgenReadOnly.Copy(sizeGoalAttempts),
        };
    }
}
