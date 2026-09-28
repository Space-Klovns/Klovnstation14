using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>
/// The writable mask is the only geometry a route may excavate. Existing clean passages may be reused.
/// Terminals must belong to the union of those two masks.
/// </summary>
public sealed class KsProcgenRouteRequest
{
    public HashSet<Vector2i> WritableCells { get; } = new();
    public HashSet<Vector2i> ExistingPassageCells { get; } = new();
    public List<Vector2i> Terminals { get; } = new();
    public int NewCellCost { get; set; } = 4;
    public int ExistingCellCost { get; set; } = 1;
    public int MaxExpandedCells { get; set; } = 100_000;
}

public enum KsProcgenRouteStatus : byte
{
    Connected,
    InvalidInput,
    NoRoute,
    BudgetExceeded,
}

public sealed class KsProcgenRouteResult
{
    public KsProcgenRouteStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlySet<Vector2i> ReservedCells { get; init; } = new HashSet<Vector2i>();
    public int ExpandedCells { get; init; }
    public long TotalCost { get; init; }
}

/// <summary>
/// Incremental least-cost tree for required terminals. Never crosses cells outside the approved masks.
/// The caller remains responsible for validating port destinations and final actor clearance.
/// </summary>
public static class KsProcgenRoutePlanner
{
    private static readonly Vector2i[] CardinalOffsets =
    [
        new(1, 0),
        new(0, 1),
        new(-1, 0),
        new(0, -1),
    ];

    public static KsProcgenRouteResult Connect(KsProcgenRouteRequest request)
    {
        if (request.NewCellCost <= 0 || request.NewCellCost > 1_000 ||
            request.ExistingCellCost <= 0 || request.ExistingCellCost > 1_000 ||
            request.MaxExpandedCells <= 0 || request.MaxExpandedCells > 1_000_000 ||
            request.Terminals.Count == 0 || request.Terminals.Count > 1_000 ||
            request.Terminals.Any(cell =>
                !request.WritableCells.Contains(cell) && !request.ExistingPassageCells.Contains(cell)))
        {
            return new KsProcgenRouteResult
            {
                Status = KsProcgenRouteStatus.InvalidInput,
                Issue = new KsProcgenIssue("InvalidRouteRequest", "Route costs, budget, or terminals are invalid."),
            };
        }

        var allowed = new HashSet<Vector2i>(request.WritableCells);
        allowed.UnionWith(request.ExistingPassageCells);
        var terminals = request.Terminals.Distinct().ToHashSet();
        var root = request.Terminals[0];
        var reserved = new HashSet<Vector2i>();
        var tree = new HashSet<Vector2i> { root };
        if (!request.ExistingPassageCells.Contains(root))
            reserved.Add(root);
        var expanded = 0;
        long totalCost = 0;

        while (!terminals.IsSubsetOf(tree))
        {
            var frontier = new PriorityQueue<Vector2i, (long Cost, int Y, int X)>();
            var costs = new Dictionary<Vector2i, long>();
            var parents = new Dictionary<Vector2i, Vector2i>();
            foreach (var cell in KsProcgenGeometry.SortCells(tree))
            {
                costs.Add(cell, 0);
                frontier.Enqueue(cell, (0, cell.Y, cell.X));
            }

            Vector2i? reached = null;
            while (frontier.TryDequeue(out var cell, out var priority))
            {
                if (priority.Cost != costs[cell])
                    continue;

                if (expanded >= request.MaxExpandedCells)
                {
                    return new KsProcgenRouteResult
                    {
                        Status = KsProcgenRouteStatus.BudgetExceeded,
                        Issue = new KsProcgenIssue("RouteBudgetExceeded", "Required route search exhausted its cell budget."),
                        ExpandedCells = expanded,
                    };
                }

                expanded++;
                if (terminals.Contains(cell) && !tree.Contains(cell))
                {
                    reached = cell;
                    break;
                }

                foreach (var offset in CardinalOffsets)
                {
                    var next = cell + offset;
                    if (!allowed.Contains(next))
                        continue;

                    var edgeCost = request.ExistingPassageCells.Contains(next)
                        ? request.ExistingCellCost
                        : request.NewCellCost;
                    var nextCost = costs[cell] + edgeCost;
                    if (costs.TryGetValue(next, out var oldCost) && oldCost <= nextCost)
                        continue;

                    costs[next] = nextCost;
                    parents[next] = cell;
                    frontier.Enqueue(next, (nextCost, next.Y, next.X));
                }
            }

            if (reached == null)
            {
                return new KsProcgenRouteResult
                {
                    Status = KsProcgenRouteStatus.NoRoute,
                    Issue = new KsProcgenIssue("RequiredTerminalsDisconnected", "No authorized cardinal route joins all terminals."),
                    ExpandedCells = expanded,
                };
            }

            var pathCell = reached.Value;
            totalCost += costs[pathCell];
            while (!tree.Contains(pathCell))
            {
                tree.Add(pathCell);
                if (!request.ExistingPassageCells.Contains(pathCell))
                    reserved.Add(pathCell);
                pathCell = parents[pathCell];
            }

        }

        return new KsProcgenRouteResult
        {
            Status = KsProcgenRouteStatus.Connected,
            ReservedCells = reserved,
            ExpandedCells = expanded,
            TotalCost = totalCost,
        };
    }

}
