using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenRouteRedundancyStatus : byte
{
    Complete,
    InvalidInput,
    BudgetExceeded,
}

public sealed record KsProcgenRouteRedundancyPairResult(
    string Id,
    Vector2i First,
    Vector2i Second,
    int IndependentRoutes,
    bool AtLeastConfiguredCap);

/// <summary>
/// Counts internally vertex-disjoint clean cardinal routes per named pair. A result at the cap
/// is a lower bound; a lower count is exact for the supplied graph. Engine actor clearance is
/// not verified.
/// </summary>
public sealed class KsProcgenRouteRedundancyResult
{
    public KsProcgenRouteRedundancyStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenRouteRedundancyPairResult> Pairs { get; init; } = [];
    public int ExaminedFlowEdges { get; init; }
    public bool EngineTraversalVerified => false;
}

/// <summary>
/// Bounded unit-capacity flow on a node-split grid graph. Each nonterminal cell and each
/// cardinal edge can carry one route, so duplicate walks cannot inflate redundancy.
/// </summary>
public static class KsProcgenRouteRedundancyAnalyzer
{
    private static readonly Vector2i[] Cardinal =
        [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    private sealed class FlowEdge(int target, int reverseIndex, int capacity)
    {
        public int Target = target;
        public int ReverseIndex = reverseIndex;
        public int Capacity = capacity;
    }

    private enum AugmentStatus : byte
    {
        Found,
        NoRoute,
        BudgetExceeded,
    }

    public static KsProcgenRouteRedundancyResult Analyze(
        IReadOnlySet<Vector2i> walkableCells,
        IReadOnlySet<Vector2i> vaultOnlyCells,
        IReadOnlyList<KsProcgenRoutePair> pairs,
        int maxCells = 2_048,
        int maxPairs = 16,
        int maxIndependentRoutes = 4,
        int maxExaminedFlowEdges = 1_000_000)
    {
        if (walkableCells == null || vaultOnlyCells == null || pairs == null ||
            maxCells <= 0 || maxCells > 8_192 || maxPairs <= 0 || maxPairs > 64 ||
            maxIndependentRoutes <= 0 || maxIndependentRoutes > 8 ||
            maxExaminedFlowEdges <= 0 || maxExaminedFlowEdges > 8_000_000 ||
            vaultOnlyCells.Any(cell => !walkableCells.Contains(cell)))
            return Failure(KsProcgenRouteRedundancyStatus.InvalidInput,
                "InvalidRouteRedundancyInput");

        var clean = new HashSet<Vector2i>(walkableCells);
        clean.ExceptWith(vaultOnlyCells);
        if (clean.Count > maxCells || pairs.Count > maxPairs)
            return Failure(KsProcgenRouteRedundancyStatus.BudgetExceeded,
                "RouteRedundancySizeBudget");
        if (pairs.Any(pair => pair == null || string.IsNullOrWhiteSpace(pair.Id) ||
                              pair.First == pair.Second || !clean.Contains(pair.First) ||
                              !clean.Contains(pair.Second)) ||
            pairs.Select(pair => pair.Id).Distinct(StringComparer.Ordinal).Count() != pairs.Count)
            return Failure(KsProcgenRouteRedundancyStatus.InvalidInput,
                "InvalidRouteRedundancyPair");

        var cells = clean.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
        var indices = new Dictionary<Vector2i, int>(cells.Length);
        for (var index = 0; index < cells.Length; index++)
            indices.Add(cells[index], index);

        var results = new List<KsProcgenRouteRedundancyPairResult>();
        var examined = 0;
        foreach (var pair in pairs.OrderBy(pair => pair.Id, StringComparer.Ordinal))
        {
            var graph = BuildGraph(cells, indices);
            var source = indices[pair.First] * 2 + 1;
            var sink = indices[pair.Second] * 2;
            var routes = 0;
            while (routes < maxIndependentRoutes)
            {
                var augment = Augment(graph, source, sink,
                    maxExaminedFlowEdges, ref examined);
                if (augment == AugmentStatus.BudgetExceeded)
                    return Failure(KsProcgenRouteRedundancyStatus.BudgetExceeded,
                        "RouteRedundancyFlowBudget");
                if (augment == AugmentStatus.NoRoute)
                    break;
                routes++;
            }

            if (routes == 0)
                return Failure(KsProcgenRouteRedundancyStatus.InvalidInput,
                    "RoutePairDisconnected");
            results.Add(new KsProcgenRouteRedundancyPairResult(pair.Id,
                pair.First, pair.Second, routes, routes == maxIndependentRoutes));
        }

        return new KsProcgenRouteRedundancyResult
        {
            Status = KsProcgenRouteRedundancyStatus.Complete,
            Pairs = results,
            ExaminedFlowEdges = examined,
        };
    }

    private static List<FlowEdge>[] BuildGraph(
        IReadOnlyList<Vector2i> cells,
        IReadOnlyDictionary<Vector2i, int> indices)
    {
        var graph = new List<FlowEdge>[cells.Count * 2];
        for (var node = 0; node < graph.Length; node++)
            graph[node] = new List<FlowEdge>();
        for (var index = 0; index < cells.Count; index++)
        {
            AddEdge(graph, index * 2, index * 2 + 1, 1);
            foreach (var offset in Cardinal)
            {
                if (!TryAdd(cells[index], offset, out var neighbor) ||
                    !indices.TryGetValue(neighbor, out var neighborIndex))
                    continue;
                AddEdge(graph, index * 2 + 1, neighborIndex * 2, 1);
            }
        }
        return graph;
    }

    private static void AddEdge(List<FlowEdge>[] graph, int first, int second, int capacity)
    {
        var forward = new FlowEdge(second, graph[second].Count, capacity);
        var reverse = new FlowEdge(first, graph[first].Count, 0);
        graph[first].Add(forward);
        graph[second].Add(reverse);
    }

    private static AugmentStatus Augment(
        List<FlowEdge>[] graph,
        int source,
        int sink,
        int maximumExamined,
        ref int examined)
    {
        var parents = new int[graph.Length];
        var parentEdges = new int[graph.Length];
        Array.Fill(parents, -1);
        parents[source] = source;
        var queue = new Queue<int>();
        queue.Enqueue(source);
        while (queue.TryDequeue(out var node))
        {
            for (var edgeIndex = 0; edgeIndex < graph[node].Count; edgeIndex++)
            {
                if (examined >= maximumExamined)
                    return AugmentStatus.BudgetExceeded;
                examined++;
                var edge = graph[node][edgeIndex];
                if (edge.Capacity <= 0 || parents[edge.Target] >= 0)
                    continue;
                parents[edge.Target] = node;
                parentEdges[edge.Target] = edgeIndex;
                if (edge.Target == sink)
                {
                    for (var current = sink; current != source; current = parents[current])
                    {
                        var selected = graph[parents[current]][parentEdges[current]];
                        selected.Capacity--;
                        graph[current][selected.ReverseIndex].Capacity++;
                    }
                    return AugmentStatus.Found;
                }
                queue.Enqueue(edge.Target);
            }
        }
        return AugmentStatus.NoRoute;
    }

    private static bool TryAdd(Vector2i first, Vector2i second, out Vector2i result)
    {
        var x = (long) first.X + second.X;
        var y = (long) first.Y + second.Y;
        if (x is < int.MinValue or > int.MaxValue || y is < int.MinValue or > int.MaxValue)
        {
            result = default;
            return false;
        }
        result = new Vector2i((int) x, (int) y);
        return true;
    }

    private static KsProcgenRouteRedundancyResult Failure(
        KsProcgenRouteRedundancyStatus status, string code) => new()
    {
        Status = status,
        Issue = new KsProcgenIssue(code,
            "Named route redundancy input or the configured flow work budget is invalid."),
    };
}
