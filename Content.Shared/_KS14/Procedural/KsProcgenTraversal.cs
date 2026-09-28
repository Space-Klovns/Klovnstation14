using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>
/// One-cell port contract for the first traversal slice. A later adapter handles wider spans.
/// </summary>
public sealed record KsProcgenPortGeometry(
    string Id,
    string RoomId,
    Vector2i Threshold,
    Vector2i OutwardNormal,
    Vector2i InsideApproach,
    Vector2i OutsideApproach);

public sealed record KsProcgenInteractionApproach(string Id, string RoomId, Vector2i Cell);

/// <summary>
/// Inspected operational-state geometry. Walkable cells must already account for actual door access.
/// Vault-only cells are excluded even if the actor can traverse them by vaulting.
/// </summary>
public sealed class KsProcgenTraversalSnapshot
{
    public HashSet<Vector2i> WalkableCells { get; } = new();
    public HashSet<Vector2i> VaultOnlyCells { get; } = new();
    public Dictionary<string, HashSet<Vector2i>> RoomCells { get; } = new(StringComparer.Ordinal);
    public List<KsProcgenPortGeometry> Ports { get; } = new();
    public List<KsProcgenInteractionApproach> InteractionApproaches { get; } = new();
    public List<Vector2i> Roots { get; } = new();
    public bool RequireAllWalkableCellsConnected { get; set; } = true;
}

public sealed class KsProcgenTraversalReport
{
    public IReadOnlyList<KsProcgenIssue> Issues { get; }
    public bool ExternalRootSupplied { get; }
    public int ReachableCells { get; }
    public bool Valid => Issues.Count == 0;

    internal KsProcgenTraversalReport(IReadOnlyList<KsProcgenIssue> issues, bool externalRootSupplied, int reachableCells)
    {
        Issues = issues;
        ExternalRootSupplied = externalRootSupplied;
        ReachableCells = reachableCells;
    }
}

/// <summary>
/// Cardinal clean-passage validation for room entrances and functional furnishing approaches.
/// </summary>
public static class KsProcgenTraversal
{
    private static readonly Vector2i[] CardinalOffsets =
    [
        new(1, 0),
        new(0, 1),
        new(-1, 0),
        new(0, -1),
    ];

    public static KsProcgenTraversalReport Validate(KsProcgenTraversalSnapshot snapshot)
    {
        var issues = new List<KsProcgenIssue>();
        var cleanCells = new HashSet<Vector2i>(snapshot.WalkableCells);
        cleanCells.ExceptWith(snapshot.VaultOnlyCells);

        var portIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var port in snapshot.Ports)
        {
            if (string.IsNullOrWhiteSpace(port.Id) || !portIds.Add(port.Id) ||
                string.IsNullOrWhiteSpace(port.RoomId) || !snapshot.RoomCells.TryGetValue(port.RoomId, out var roomCells))
            {
                issues.Add(new KsProcgenIssue("InvalidPort", $"Port {port.Id} has an unknown room or duplicate ID."));
                continue;
            }

            if (!IsCardinal(port.OutwardNormal) ||
                port.InsideApproach != port.Threshold - port.OutwardNormal ||
                port.OutsideApproach != port.Threshold + port.OutwardNormal ||
                !roomCells.Contains(port.InsideApproach) ||
                !cleanCells.Contains(port.Threshold) ||
                !cleanCells.Contains(port.InsideApproach) ||
                !cleanCells.Contains(port.OutsideApproach))
            {
                issues.Add(new KsProcgenIssue("PortApproachBlocked", $"Port {port.Id} lacks a clean cardinal approach."));
            }
        }

        var interactionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var interaction in snapshot.InteractionApproaches)
        {
            if (string.IsNullOrWhiteSpace(interaction.Id) || !interactionIds.Add(interaction.Id) ||
                !snapshot.RoomCells.TryGetValue(interaction.RoomId, out var roomCells) ||
                !roomCells.Contains(interaction.Cell) || !cleanCells.Contains(interaction.Cell))
            {
                issues.Add(new KsProcgenIssue("InteractionApproachBlocked",
                    $"Functional approach {interaction.Id} is blocked or outside its room."));
            }
        }

        foreach (var (roomId, roomCells) in snapshot.RoomCells.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var roomPorts = snapshot.Ports.Where(port => port.RoomId == roomId).ToArray();
            if (roomPorts.Length == 0)
            {
                issues.Add(new KsProcgenIssue("RoomWithoutEntrance", $"Room {roomId} has no operational entrance."));
                continue;
            }

            var validRoomCells = new HashSet<Vector2i>(roomCells);
            validRoomCells.IntersectWith(cleanCells);
            var firstApproach = roomPorts[0].InsideApproach;
            var reachableInside = Flood(firstApproach, validRoomCells);
            if (roomPorts.Any(port => !reachableInside.Contains(port.InsideApproach)))
            {
                issues.Add(new KsProcgenIssue("RoomPortsDisconnected",
                    $"Room {roomId} has entrances that do not connect through its interior."));
            }

            if (snapshot.InteractionApproaches.Any(interaction =>
                    interaction.RoomId == roomId && !reachableInside.Contains(interaction.Cell)))
            {
                issues.Add(new KsProcgenIssue("RoomInteractionDisconnected",
                    $"Room {roomId} has a chair or machine approach without a clean passage."));
            }
        }

        IReadOnlyList<Vector2i> roots = snapshot.Roots.Count == 0
            ? KsProcgenGeometry.SortCells(cleanCells).Take(1).ToArray()
            : snapshot.Roots;
        var rootCell = roots.Count > 0 ? roots[0] : (Vector2i?) null;
        var reachable = rootCell.HasValue ? Flood(rootCell.Value, cleanCells) : new HashSet<Vector2i>();

        foreach (var root in roots)
        {
            if (!reachable.Contains(root))
                issues.Add(new KsProcgenIssue("RootDisconnected", $"Root {root} is not in the clean network."));
        }

        foreach (var port in snapshot.Ports)
        {
            if (!reachable.Contains(port.Threshold) ||
                !reachable.Contains(port.InsideApproach) ||
                !reachable.Contains(port.OutsideApproach))
            {
                issues.Add(new KsProcgenIssue("PortDisconnected", $"Port {port.Id} cannot reach the network root."));
            }
        }

        foreach (var interaction in snapshot.InteractionApproaches)
        {
            if (!reachable.Contains(interaction.Cell))
                issues.Add(new KsProcgenIssue("InteractionDisconnected",
                    $"Chair or machine approach {interaction.Id} cannot reach the network root."));
        }

        if (snapshot.RequireAllWalkableCellsConnected && reachable.Count != cleanCells.Count)
        {
            issues.Add(new KsProcgenIssue("DisconnectedWalkableCells",
                $"{cleanCells.Count - reachable.Count} clean cells cannot reach the network root."));
        }

        return new KsProcgenTraversalReport(issues, snapshot.Roots.Count != 0, reachable.Count);
    }

    public static IReadOnlyList<Vector2i> FindCleanPath(
        Vector2i start,
        Vector2i goal,
        IReadOnlySet<Vector2i> walkableCells,
        IReadOnlySet<Vector2i> vaultOnlyCells)
    {
        if (!walkableCells.Contains(start) || !walkableCells.Contains(goal) ||
            vaultOnlyCells.Contains(start) || vaultOnlyCells.Contains(goal))
            return [];

        var parents = new Dictionary<Vector2i, Vector2i>();
        var visited = new HashSet<Vector2i> { start };
        var queue = new Queue<Vector2i>();
        queue.Enqueue(start);

        while (queue.TryDequeue(out var cell))
        {
            if (cell == goal)
            {
                var path = new List<Vector2i> { goal };
                while (cell != start)
                {
                    cell = parents[cell];
                    path.Add(cell);
                }

                path.Reverse();
                return path;
            }

            foreach (var offset in CardinalOffsets)
            {
                var next = cell + offset;
                if (!walkableCells.Contains(next) || vaultOnlyCells.Contains(next) || !visited.Add(next))
                    continue;

                parents.Add(next, cell);
                queue.Enqueue(next);
            }
        }

        return [];
    }

    private static HashSet<Vector2i> Flood(Vector2i start, IReadOnlySet<Vector2i> allowedCells)
    {
        var visited = new HashSet<Vector2i>();
        if (!allowedCells.Contains(start))
            return visited;

        var queue = new Queue<Vector2i>();
        queue.Enqueue(start);
        visited.Add(start);
        while (queue.TryDequeue(out var cell))
        {
            foreach (var offset in CardinalOffsets)
            {
                var next = cell + offset;
                if (allowedCells.Contains(next) && visited.Add(next))
                    queue.Enqueue(next);
            }
        }

        return visited;
    }

    private static bool IsCardinal(Vector2i normal) =>
        (normal.X == 0 && (normal.Y == 1 || normal.Y == -1)) ||
        (normal.Y == 0 && (normal.X == 1 || normal.X == -1));
}
