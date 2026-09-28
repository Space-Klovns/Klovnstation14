using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenPartitionStatus : byte
{
    Proposed,
    MergedFallback,
    OpenFallback,
    InvalidInput,
}

/// <summary>
/// One tile of the owning region is reserved as an opening in a tile-thick partition.
/// The engine must still instantiate and verify a usable door at this position.
/// </summary>
public sealed record KsProcgenPartitionDoor(
    string FirstZoneId,
    string SecondZoneId,
    Vector2i Threshold,
    Vector2i InsideApproach,
    Vector2i OutsideApproach);

public sealed class KsProcgenPartitionResult
{
    public KsProcgenPartitionStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<Vector2i> WallCells { get; init; } = [];
    public IReadOnlyList<Vector2i> FloorCells { get; init; } = [];
    public IReadOnlyList<KsProcgenPartitionDoor> DoorOpenings { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<string>> MergedZoneGroups { get; init; } = [];
    public int RejectedSplits { get; init; }
}

/// <summary>
/// Conservative tile-thick seams. A failed split merges its adjacent logical zones and retries.
/// This is geometry, not a verified engine door, hull, or operational passage.
/// </summary>
public static class KsProcgenPartitionPlanner
{
    private static readonly Vector2i[] Cardinal = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public static KsProcgenPartitionResult Plan(
        KsProcgenPureFillResult fill,
        IReadOnlySet<Vector2i> requiredPassageCells,
        int maxMergeAttempts = 256)
    {
        if (fill == null || requiredPassageCells == null || fill.Status != KsProcgenPureFillStatus.Proposed ||
            maxMergeAttempts < 0 || maxMergeAttempts > 65_536)
            return Invalid("InvalidPartitionInput");

        var ownerByCell = new Dictionary<Vector2i, KsProcgenZone>();
        var zonesById = new Dictionary<string, KsProcgenZone>(StringComparer.Ordinal);
        foreach (var zone in fill.Zones)
        {
            if (string.IsNullOrWhiteSpace(zone.Id) || zone.Cells.Count == 0 ||
                !Enum.IsDefined(zone.Kind) || !zonesById.TryAdd(zone.Id, zone) ||
                KsProcgenGeometry.ConnectedComponents(zone.Cells.ToHashSet()).Count != 1)
                return Invalid("InvalidPartitionZone");
            foreach (var cell in zone.Cells)
            {
                if (!ownerByCell.TryAdd(cell, zone))
                    return Invalid("OverlappingPartitionZones");
            }
        }

        if (ownerByCell.Count != fill.ProceduralCells ||
            requiredPassageCells.Any(cell => !ownerByCell.TryGetValue(cell, out var zone) ||
                                             zone.Kind != KsProcgenZoneKind.Passage))
            return Invalid("InvalidPartitionCoverage");

        var parent = zonesById.Keys.ToDictionary(id => id, id => id, StringComparer.Ordinal);
        string Root(string id)
        {
            while (parent[id] != id)
                id = parent[id];
            return id;
        }

        var rejected = 0;
        while (true)
        {
            var groups = fill.Zones.GroupBy(zone => Root(zone.Id))
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var groupByCell = ownerByCell.ToDictionary(entry => entry.Key, entry => Root(entry.Value.Id));
            var interfaces = BuildInterfaces(groupByCell);
            var walls = new HashSet<Vector2i>();
            var doors = new List<KsProcgenPartitionDoor>();
            var doorThresholds = new HashSet<Vector2i>();
            (string First, string Second)? rejectedPair = null;

            foreach (var (key, edges) in OrderedInterfaces(interfaces))
            {
                var firstHasPassage = groups[key.First].Any(zone => zone.Kind == KsProcgenZoneKind.Passage);
                var secondHasPassage = groups[key.Second].Any(zone => zone.Kind == KsProcgenZoneKind.Passage);
                if (firstHasPassage && secondHasPassage)
                    continue;

                // Keep passage cells free. For two rooms, the second group owns the wall band.
                var wallOwner = firstHasPassage ? key.Second : secondHasPassage ? key.First : key.Second;
                var boundaryCells = new HashSet<Vector2i>();
                var possibleDoors = new List<KsProcgenPartitionDoor>();
                foreach (var edge in edges)
                {
                    var threshold = wallOwner == key.First ? edge.First : edge.Second;
                    var outside = wallOwner == key.First ? edge.Second : edge.First;
                    boundaryCells.Add(threshold);
                    var inside = threshold + (threshold - outside);
                    if (groupByCell.TryGetValue(inside, out var insideGroup) && insideGroup == wallOwner)
                        possibleDoors.Add(new KsProcgenPartitionDoor(key.First, key.Second,
                            threshold, inside, outside));
                }

                KsProcgenPartitionDoor? chosen = null;
                HashSet<Vector2i>? proposedWalls = null;
                foreach (var door in possibleDoors.OrderBy(choice => choice.Threshold.Y)
                             .ThenBy(choice => choice.Threshold.X)
                             .ThenBy(choice => choice.OutsideApproach.Y)
                             .ThenBy(choice => choice.OutsideApproach.X))
                {
                    if (walls.Contains(door.Threshold) || doorThresholds.Contains(door.Threshold))
                        continue;
                    var candidateWalls = new HashSet<Vector2i>(boundaryCells);
                    candidateWalls.Remove(door.Threshold);
                    if (candidateWalls.Count == 0 || candidateWalls.Overlaps(requiredPassageCells) ||
                        candidateWalls.Overlaps(doorThresholds))
                        continue;
                    chosen = door;
                    proposedWalls = candidateWalls;
                    break;
                }

                if (chosen == null || proposedWalls == null)
                {
                    rejectedPair = key;
                    break;
                }

                walls.UnionWith(proposedWalls);
                doors.Add(chosen);
                doorThresholds.Add(chosen.Threshold);
            }

            var floor = new HashSet<Vector2i>(ownerByCell.Keys);
            floor.ExceptWith(walls);
            if (rejectedPair == null)
            {
                var blockedDoor = doors.FirstOrDefault(door =>
                    !floor.Contains(door.InsideApproach) || !floor.Contains(door.OutsideApproach) ||
                    !floor.Contains(door.Threshold));
                if (blockedDoor != null)
                    rejectedPair = (blockedDoor.FirstZoneId, blockedDoor.SecondZoneId);
            }

            if (rejectedPair == null)
            {
                foreach (var (groupId, members) in groups.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    var usable = new HashSet<Vector2i>(members.SelectMany(zone => zone.Cells));
                    usable.ExceptWith(walls);
                    if (usable.Count != 0 && KsProcgenGeometry.ConnectedComponents(usable).Count == 1)
                        continue;
                    rejectedPair = FirstInterfaceFor(groupId, interfaces);
                    break;
                }
            }

            if (rejectedPair == null)
            {
                foreach (var component in KsProcgenGeometry.ConnectedComponents(ownerByCell.Keys.ToHashSet()))
                {
                    var componentFloor = new HashSet<Vector2i>(component);
                    componentFloor.IntersectWith(floor);
                    if (componentFloor.Count != 0 && KsProcgenGeometry.ConnectedComponents(componentFloor).Count == 1)
                        continue;
                    var groupsInComponent = new HashSet<string>(component.Select(cell => groupByCell[cell]));
                    rejectedPair = OrderedInterfaces(interfaces)
                        .Select(entry => ((string First, string Second)?) entry.Key)
                        .FirstOrDefault(pair => pair.HasValue && groupsInComponent.Contains(pair.Value.First));
                    break;
                }
            }

            if (rejectedPair == null)
            {
                var mergedGroups = groups.Values.Where(members => members.Length > 1)
                    .Select(members => (IReadOnlyList<string>) members.Select(zone => zone.Id)
                        .OrderBy(id => id, StringComparer.Ordinal).ToArray())
                    .OrderBy(members => members[0], StringComparer.Ordinal).ToArray();
                var status = rejected == 0 ? KsProcgenPartitionStatus.Proposed :
                    walls.Count == 0 ? KsProcgenPartitionStatus.OpenFallback :
                    KsProcgenPartitionStatus.MergedFallback;
                return new KsProcgenPartitionResult
                {
                    Status = status,
                    Issue = rejected == 0 ? null : new KsProcgenIssue(
                        status == KsProcgenPartitionStatus.OpenFallback ? "PartitionOpenFallback" : "PartitionMergedFallback",
                        "One or more unfit splits were merged into open connected regions."),
                    FloorCells = KsProcgenGeometry.SortCells(floor),
                    WallCells = KsProcgenGeometry.SortCells(walls),
                    DoorOpenings = doors,
                    MergedZoneGroups = mergedGroups,
                    RejectedSplits = rejected,
                };
            }

            if (rejected >= maxMergeAttempts || rejectedPair.Value.First == rejectedPair.Value.Second)
                return Open(ownerByCell.Keys, rejected + 1, "PartitionMergeBudget");

            // This pair is a cardinal interface, so merging maintains connected groups.
            var firstRoot = Root(rejectedPair.Value.First);
            var secondRoot = Root(rejectedPair.Value.Second);
            if (string.CompareOrdinal(firstRoot, secondRoot) > 0)
                (firstRoot, secondRoot) = (secondRoot, firstRoot);
            parent[secondRoot] = firstRoot;
            rejected++;
        }
    }

    private static Dictionary<(string First, string Second), List<(Vector2i First, Vector2i Second)>>
        BuildInterfaces(IReadOnlyDictionary<Vector2i, string> groupByCell)
    {
        var interfaces = new Dictionary<(string First, string Second), List<(Vector2i First, Vector2i Second)>>();
        foreach (var (cell, group) in groupByCell)
        foreach (var offset in Cardinal)
        {
            var neighbor = cell + offset;
            if (!groupByCell.TryGetValue(neighbor, out var other) || group == other ||
                string.CompareOrdinal(group, other) > 0)
                continue;
            var key = (group, other);
            if (!interfaces.TryGetValue(key, out var edges))
                interfaces.Add(key, edges = new List<(Vector2i, Vector2i)>());
            edges.Add((cell, neighbor));
        }

        return interfaces;
    }

    private static IEnumerable<KeyValuePair<(string First, string Second), List<(Vector2i First, Vector2i Second)>>> 
        OrderedInterfaces(Dictionary<(string First, string Second), List<(Vector2i First, Vector2i Second)>> interfaces) =>
        interfaces.OrderBy(entry => entry.Key.First, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key.Second, StringComparer.Ordinal);

    private static (string First, string Second)? FirstInterfaceFor(
        string groupId,
        Dictionary<(string First, string Second), List<(Vector2i First, Vector2i Second)>> interfaces) =>
        OrderedInterfaces(interfaces).Select(entry => ((string First, string Second)?) entry.Key)
            .FirstOrDefault(pair => pair.HasValue &&
                                    (pair.Value.First == groupId || pair.Value.Second == groupId));

    private static KsProcgenPartitionResult Open(IEnumerable<Vector2i> cells, int rejected, string code) => new()
    {
        Status = KsProcgenPartitionStatus.OpenFallback,
        FloorCells = KsProcgenGeometry.SortCells(cells),
        RejectedSplits = rejected,
        Issue = new KsProcgenIssue(code, "The split budget was exhausted; procedural floor remains open."),
    };

    private static KsProcgenPartitionResult Invalid(string code) => new()
    {
        Status = KsProcgenPartitionStatus.InvalidInput,
        Issue = new KsProcgenIssue(code, "Partition input is invalid."),
    };
}
