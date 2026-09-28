using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenTacticalStatus : byte
{
    Complete,
    DetailsTruncated,
    InvalidInput,
    BudgetExceeded,
}

public sealed record KsProcgenChokeSide(int CleanCells, int RequiredTerminals);
public sealed record KsProcgenChokeDetail(Vector2i Cell, IReadOnlyList<KsProcgenChokeSide> Sides);
public sealed record KsProcgenBridgeEdge(Vector2i First, Vector2i Second);

/// <summary>
/// Exact cardinal movement graph facts. Vision and projectile cover require separate models.
/// </summary>
public sealed class KsProcgenTacticalResult
{
    public KsProcgenTacticalStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<Vector2i> ArticulationCells { get; init; } = [];
    public IReadOnlyList<KsProcgenBridgeEdge> BridgeEdges { get; init; } = [];
    public IReadOnlyList<KsProcgenChokeDetail> ChokeDetails { get; init; } = [];
    public int CleanCells { get; init; }
}

public static class KsProcgenTacticalAnalyzer
{
    private static readonly Vector2i[] Cardinal = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    private sealed class Frame(Vector2i cell)
    {
        public Vector2i Cell = cell;
        public int NextNeighbor;
    }

    public static KsProcgenTacticalResult Analyze(
        IReadOnlySet<Vector2i> walkableCells,
        IReadOnlySet<Vector2i> vaultOnlyCells,
        IReadOnlySet<Vector2i> requiredTerminals,
        int maxCells = 65_536,
        int maxDetailedChokes = 128)
    {
        if (walkableCells == null || vaultOnlyCells == null || requiredTerminals == null ||
            maxCells <= 0 || maxCells > 65_536 || maxDetailedChokes < 0 || maxDetailedChokes > 512 ||
            vaultOnlyCells.Any(cell => !walkableCells.Contains(cell)))
            return Failure(KsProcgenTacticalStatus.InvalidInput, "InvalidTacticalInput");

        var clean = new HashSet<Vector2i>(walkableCells);
        clean.ExceptWith(vaultOnlyCells);
        if (clean.Count > maxCells)
            return Failure(KsProcgenTacticalStatus.BudgetExceeded, "TacticalCellBudget");
        if (requiredTerminals.Any(cell => !clean.Contains(cell)))
            return Failure(KsProcgenTacticalStatus.InvalidInput, "InvalidTacticalTerminal");

        var discovery = new Dictionary<Vector2i, int>();
        var low = new Dictionary<Vector2i, int>();
        var parent = new Dictionary<Vector2i, Vector2i>();
        var childCount = new Dictionary<Vector2i, int>();
        var articulations = new HashSet<Vector2i>();
        var bridges = new List<KsProcgenBridgeEdge>();
        var clock = 0;
        foreach (var start in KsProcgenGeometry.SortCells(clean))
        {
            if (discovery.ContainsKey(start))
                continue;
            discovery.Add(start, ++clock);
            low.Add(start, clock);
            childCount.Add(start, 0);
            var stack = new Stack<Frame>();
            stack.Push(new Frame(start));
            while (stack.Count > 0)
            {
                var frame = stack.Peek();
                if (frame.NextNeighbor < Cardinal.Length)
                {
                    var next = frame.Cell + Cardinal[frame.NextNeighbor++];
                    if (!clean.Contains(next))
                        continue;
                    if (!discovery.ContainsKey(next))
                    {
                        parent.Add(next, frame.Cell);
                        childCount[frame.Cell]++;
                        childCount.Add(next, 0);
                        discovery.Add(next, ++clock);
                        low.Add(next, clock);
                        stack.Push(new Frame(next));
                    }
                    else if (!parent.TryGetValue(frame.Cell, out var previous) || previous != next)
                    {
                        low[frame.Cell] = Math.Min(low[frame.Cell], discovery[next]);
                    }

                    continue;
                }

                stack.Pop();
                if (parent.TryGetValue(frame.Cell, out var ancestor))
                {
                    low[ancestor] = Math.Min(low[ancestor], low[frame.Cell]);
                    if (parent.ContainsKey(ancestor) && low[frame.Cell] >= discovery[ancestor])
                        articulations.Add(ancestor);
                    if (low[frame.Cell] > discovery[ancestor])
                    {
                        var pair = Before(ancestor, frame.Cell)
                            ? new KsProcgenBridgeEdge(ancestor, frame.Cell)
                            : new KsProcgenBridgeEdge(frame.Cell, ancestor);
                        bridges.Add(pair);
                    }
                }
                else if (childCount[frame.Cell] > 1)
                {
                    articulations.Add(frame.Cell);
                }
            }
        }

        var orderedChokes = KsProcgenGeometry.SortCells(articulations);
        var details = new List<KsProcgenChokeDetail>();
        var components = KsProcgenGeometry.ConnectedComponents(clean);
        foreach (var choke in orderedChokes.Take(maxDetailedChokes))
        {
            var component = components.First(cells => cells.Contains(choke));
            var remaining = new HashSet<Vector2i>(component);
            remaining.Remove(choke);
            var sides = KsProcgenGeometry.ConnectedComponents(remaining)
                .Select(cells => new KsProcgenChokeSide(cells.Count, cells.Count(requiredTerminals.Contains)))
                .OrderByDescending(side => side.CleanCells)
                .ThenByDescending(side => side.RequiredTerminals).ToArray();
            details.Add(new KsProcgenChokeDetail(choke, sides));
        }

        var truncated = orderedChokes.Count > maxDetailedChokes;
        return new KsProcgenTacticalResult
        {
            Status = truncated ? KsProcgenTacticalStatus.DetailsTruncated : KsProcgenTacticalStatus.Complete,
            Issue = truncated ? new KsProcgenIssue("TacticalDetailsTruncated",
                "All chokes were identified, but detailed side sizes reached the configured limit.") : null,
            ArticulationCells = orderedChokes,
            BridgeEdges = bridges.OrderBy(edge => edge.First.Y).ThenBy(edge => edge.First.X)
                .ThenBy(edge => edge.Second.Y).ThenBy(edge => edge.Second.X).ToArray(),
            ChokeDetails = details,
            CleanCells = clean.Count,
        };
    }

    private static bool Before(Vector2i first, Vector2i second) =>
        first.Y < second.Y || first.Y == second.Y && first.X < second.X;

    private static KsProcgenTacticalResult Failure(KsProcgenTacticalStatus status, string code) => new()
    {
        Status = status,
        Issue = new KsProcgenIssue(code, "Movement choke analysis input or budget is invalid."),
    };
}
