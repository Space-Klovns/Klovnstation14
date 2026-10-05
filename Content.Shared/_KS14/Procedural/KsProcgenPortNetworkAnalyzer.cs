using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenPortNetworkStatus : byte
{
    Connected,
    Disconnected,
    InvalidInput,
    BudgetExceeded,
}

public sealed record KsProcgenPortNetworkGroup(
    IReadOnlyList<string> RoomIds,
    int PassageCells,
    int RootCells);

/// <summary>
/// Abstract port/passage connectivity only. It assumes declared room ports are internally usable;
/// actual prefab collision and operational doors still need inspection.
/// </summary>
public sealed class KsProcgenPortNetworkResult
{
    public KsProcgenPortNetworkStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenPortNetworkGroup> Groups { get; init; } = [];
    public IReadOnlyList<string> RoomsWithoutDeclaredPorts { get; init; } = [];
    public bool EngineAccessVerified => false;
}

public static class KsProcgenPortNetworkAnalyzer
{
    public static KsProcgenPortNetworkResult Analyze(
        KsProcgenNormalizedShape shape,
        KsProcgenPackingResult packing,
        IReadOnlySet<Vector2i> inspectedExistingPassages,
        IReadOnlyList<Vector2i> roots,
        int maxNodes = 65_536) => AnalyzeCore(shape, packing, inspectedExistingPassages,
        roots, null, maxNodes);

    public static KsProcgenPortNetworkResult AnalyzePartitioned(
        KsProcgenNormalizedShape shape,
        KsProcgenPackingResult packing,
        KsProcgenPartitionResult partition,
        IReadOnlySet<Vector2i> inspectedExistingPassages,
        IReadOnlyList<Vector2i> roots,
        int maxNodes = 65_536)
    {
        if (partition == null || inspectedExistingPassages == null || packing == null ||
            partition.FloorCells == null || partition.WallCells == null || partition.DoorOpenings == null ||
            !Enum.IsDefined(partition.Status) || partition.Status == KsProcgenPartitionStatus.InvalidInput)
            return Failure(KsProcgenPortNetworkStatus.InvalidInput, "InvalidPartitionedPortNetwork");

        var floor = partition.FloorCells.ToHashSet();
        var walls = partition.WallCells.ToHashSet();
        var procedural = packing.CellClaims.Where(entry =>
            entry.Claim.Disposition == KsProcgenCellDisposition.ProceduralFloor)
            .Select(entry => entry.Cell).ToHashSet();
        if (floor.Count != partition.FloorCells.Count || walls.Count != partition.WallCells.Count ||
            floor.Overlaps(walls) || !floor.IsSubsetOf(procedural) || !walls.IsSubsetOf(procedural) ||
            floor.Count + walls.Count != procedural.Count || walls.Overlaps(inspectedExistingPassages) ||
            packing.ResidualRouting?.ReservedPassageCells.Any(cell => !floor.Contains(cell)) == true ||
            partition.DoorOpenings.Any(door => !floor.Contains(door.Threshold) ||
                !floor.Contains(door.InsideApproach) || !floor.Contains(door.OutsideApproach) ||
                !IsCardinalStep(door.Threshold, door.InsideApproach) ||
                !IsCardinalStep(door.Threshold, door.OutsideApproach) ||
                door.InsideApproach + door.OutsideApproach != door.Threshold + door.Threshold))
            return Failure(KsProcgenPortNetworkStatus.InvalidInput, "InvalidPartitionedPortNetwork");

        return AnalyzeCore(shape, packing, inspectedExistingPassages, roots, floor, maxNodes);
    }

    private static KsProcgenPortNetworkResult AnalyzeCore(
        KsProcgenNormalizedShape shape,
        KsProcgenPackingResult packing,
        IReadOnlySet<Vector2i> inspectedExistingPassages,
        IReadOnlyList<Vector2i> roots,
        IReadOnlySet<Vector2i>? proceduralWalkableCells,
        int maxNodes)
    {
        if (shape?.EntranceContract != null)
            return Failure(KsProcgenPortNetworkStatus.InvalidInput, "UnsupportedEntranceAwarePortNetwork");
        if (shape == null || packing == null || inspectedExistingPassages == null || roots == null ||
            packing.Status != KsProcgenPackingStatus.GeometryReady ||
            packing.ResidualRouting?.Status != KsProcgenResidualStatus.PreliminaryReady ||
            maxNodes <= 0 || maxNodes > 131_072 ||
            inspectedExistingPassages.Any(cell => !shape.ContainsTarget(cell)) ||
            roots.Any(cell => !shape.ContainsTarget(cell)))
            return Failure(KsProcgenPortNetworkStatus.InvalidInput, "InvalidPortNetworkInput");

        var claims = new Dictionary<Vector2i, KsProcgenCellClaim>();
        var allowed = new HashSet<Vector2i>(inspectedExistingPassages);
        var roomIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (cell, claim) in packing.CellClaims)
        {
            if (!shape.ContainsTarget(cell) || !claims.TryAdd(cell, claim))
                return Failure(KsProcgenPortNetworkStatus.InvalidInput, "InvalidPortNetworkClaims");
            if (claim.Disposition == KsProcgenCellDisposition.ProceduralFloor &&
                (proceduralWalkableCells == null || proceduralWalkableCells.Contains(cell)))
                allowed.Add(cell);
            if (claim.Disposition == KsProcgenCellDisposition.Prefab ||
                claim.Disposition == KsProcgenCellDisposition.Preserved &&
                claim.OwnerId.StartsWith("constant:", StringComparison.Ordinal))
                roomIds.Add(claim.OwnerId);
        }
        if (claims.Count != shape.TargetCells.Count ||
            roots.Any(cell => !allowed.Contains(cell)))
            return Failure(KsProcgenPortNetworkStatus.InvalidInput, "InvalidPortNetworkRoots");

        var ports = packing.Placements.SelectMany(placement => placement.Ports)
            .Concat(shape.ConstantRegions.SelectMany(region => region.Ports)).ToArray();
        var byPortId = new Dictionary<string, KsProcgenPortGeometry>(StringComparer.Ordinal);
        foreach (var port in ports)
        {
            if (string.IsNullOrWhiteSpace(port.Id) || !byPortId.TryAdd(port.Id, port) ||
                !roomIds.Contains(port.RoomId))
                return Failure(KsProcgenPortNetworkStatus.InvalidInput, "InvalidPortNetworkPorts");
        }

        if (roomIds.Count + allowed.Count > maxNodes)
            return Failure(KsProcgenPortNetworkStatus.BudgetExceeded, "PortNetworkNodeBudget");
        var components = KsProcgenGeometry.ConnectedComponents(allowed);
        var componentByCell = new Dictionary<Vector2i, int>();
        for (var index = 0; index < components.Count; index++)
        foreach (var cell in components[index])
            componentByCell.Add(cell, index);

        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var roomId in roomIds)
            parent.Add(RoomNode(roomId), RoomNode(roomId));
        for (var index = 0; index < components.Count; index++)
            parent.Add(PassageNode(index), PassageNode(index));

        string Find(string node)
        {
            while (parent[node] != node)
                node = parent[node];
            return node;
        }

        void Union(string first, string second)
        {
            first = Find(first);
            second = Find(second);
            if (first == second)
                return;
            if (string.CompareOrdinal(first, second) > 0)
                (first, second) = (second, first);
            parent[second] = first;
        }

        var pairedPorts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (firstId, secondId) in packing.ResidualRouting.DirectPortPairs)
        {
            if (!byPortId.TryGetValue(firstId, out var first) ||
                !byPortId.TryGetValue(secondId, out var second) || firstId == secondId ||
                !pairedPorts.Add(firstId) || !pairedPorts.Add(secondId) ||
                first.OutsideApproach != second.Threshold ||
                second.OutsideApproach != first.Threshold ||
                first.OutwardNormal != -second.OutwardNormal)
                return Failure(KsProcgenPortNetworkStatus.InvalidInput, "InvalidPortNetworkPair");
            Union(RoomNode(first.RoomId), RoomNode(second.RoomId));
        }

        foreach (var port in ports)
        {
            if (pairedPorts.Contains(port.Id))
                continue;
            if (!componentByCell.TryGetValue(port.OutsideApproach, out var component))
                return Failure(KsProcgenPortNetworkStatus.InvalidInput, "UnroutedPortNetworkPort");
            Union(RoomNode(port.RoomId), PassageNode(component));
        }

        // An inspected clean cell inside an authored room is a known room/passage landing.
        // Without this link a root inside a constant-only room appears disconnected from its owner.
        foreach (var cell in inspectedExistingPassages)
        {
            var claim = claims[cell];
            if (roomIds.Contains(claim.OwnerId))
                Union(RoomNode(claim.OwnerId), PassageNode(componentByCell[cell]));
        }

        var rootCounts = new Dictionary<int, int>();
        foreach (var root in roots)
        {
            var component = componentByCell[root];
            rootCounts[component] = rootCounts.GetValueOrDefault(component) + 1;
        }

        var groups = new Dictionary<string, (List<string> Rooms, int PassageCells, int Roots)>(
            StringComparer.Ordinal);
        foreach (var roomId in roomIds)
        {
            var key = Find(RoomNode(roomId));
            if (!groups.TryGetValue(key, out var group))
                group = ([], 0, 0);
            group.Rooms.Add(roomId);
            groups[key] = group;
        }
        for (var index = 0; index < components.Count; index++)
        {
            var key = Find(PassageNode(index));
            if (!groups.TryGetValue(key, out var group))
                group = ([], 0, 0);
            group.PassageCells += components[index].Count;
            group.Roots += rootCounts.GetValueOrDefault(index);
            groups[key] = group;
        }

        var ordered = groups.Values.Select(group => new KsProcgenPortNetworkGroup(
                group.Rooms.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                group.PassageCells, group.Roots))
            .OrderBy(group => group.RoomIds.FirstOrDefault() ?? "~", StringComparer.Ordinal)
            .ThenBy(group => group.PassageCells).ToArray();
        var withoutPorts = roomIds.Except(ports.Select(port => port.RoomId), StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var disconnected = ordered.Length > 1 || roots.Count > 0 &&
            ordered.Any(group => group.RootCells == 0);
        return new KsProcgenPortNetworkResult
        {
            Status = disconnected ? KsProcgenPortNetworkStatus.Disconnected :
                KsProcgenPortNetworkStatus.Connected,
            Issue = disconnected ? new KsProcgenIssue("DisconnectedPortNetwork",
                "Declared rooms and passage components do not form one preliminary network.") : null,
            Groups = ordered,
            RoomsWithoutDeclaredPorts = withoutPorts,
        };
    }

    private static string RoomNode(string id) => $"room:{id}";
    private static string PassageNode(int index) => $"passage:{index}";
    private static bool IsCardinalStep(Vector2i first, Vector2i second) =>
        Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y) == 1;

    private static KsProcgenPortNetworkResult Failure(KsProcgenPortNetworkStatus status, string code) => new()
    {
        Status = status,
        Issue = new KsProcgenIssue(code, "Port-network facts or budget could not be reconciled."),
    };
}
