using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenGasEdgeState : byte
{
    Open,
    Blocked,
    Unknown,
}

public enum KsProcgenGasClosureStatus : byte
{
    PreliminarilyClosed,
    LeakToVacuum,
    UnverifiedBoundary,
    InvalidInput,
    BudgetExceeded,
}

public sealed record KsProcgenGasEdge(Vector2i First, Vector2i Second,
    KsProcgenGasEdgeState State);

/// <summary>
/// Explicit snapshot of gas adjacency in one nominated door state. An omitted edge is unknown.
/// These facts must come from material/engine inspection, not movement collision or sprites.
/// </summary>
public sealed class KsProcgenGasSnapshot
{
    public IReadOnlyCollection<Vector2i> GasCells { get; init; } = [];
    public IReadOnlyCollection<Vector2i> ProtectedCells { get; init; } = [];
    public IReadOnlyCollection<Vector2i> VacuumCells { get; init; } = [];
    public IReadOnlyCollection<KsProcgenGasEdge> Edges { get; init; } = [];
}

public sealed class KsProcgenGasClosureResult
{
    public KsProcgenGasClosureStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<Vector2i> ReachedGasCells { get; init; } = [];
    public IReadOnlyList<KsProcgenGasEdge> LeakEdges { get; init; } = [];
    public IReadOnlyList<KsProcgenGasEdge> UnknownEdges { get; init; } = [];
    public bool MaterializedEngineVerified => false;
}

public static class KsProcgenGasClosure
{
    private static readonly Vector2i[] Cardinal = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public static KsProcgenGasClosureResult Check(KsProcgenGasSnapshot snapshot,
        int maxGasCells = 65_536)
    {
        if (snapshot == null || snapshot.GasCells == null || snapshot.ProtectedCells == null ||
            snapshot.VacuumCells == null || snapshot.Edges == null || maxGasCells <= 0 ||
            maxGasCells > 65_536)
            return Failure(KsProcgenGasClosureStatus.InvalidInput, "InvalidGasSnapshot");

        var gas = new HashSet<Vector2i>(snapshot.GasCells);
        var protectedCells = new HashSet<Vector2i>(snapshot.ProtectedCells);
        var vacuum = new HashSet<Vector2i>(snapshot.VacuumCells);
        if (gas.Count != snapshot.GasCells.Count || protectedCells.Count != snapshot.ProtectedCells.Count ||
            vacuum.Count != snapshot.VacuumCells.Count || !protectedCells.IsSubsetOf(gas) ||
            protectedCells.Count == 0 || gas.Overlaps(vacuum) || gas.Count + vacuum.Count > 65_536)
            return Failure(KsProcgenGasClosureStatus.InvalidInput, "InvalidGasCells");
        if (gas.Count > maxGasCells || snapshot.Edges.Count > 4 * maxGasCells)
            return Failure(KsProcgenGasClosureStatus.BudgetExceeded, "GasSnapshotBudget");

        var edges = new Dictionary<(Vector2i First, Vector2i Second), KsProcgenGasEdgeState>();
        foreach (var edge in snapshot.Edges)
        {
            if (edge == null || !Enum.IsDefined(edge.State) ||
                Math.Abs((long) edge.First.X - edge.Second.X) +
                Math.Abs((long) edge.First.Y - edge.Second.Y) != 1 ||
                !(gas.Contains(edge.First) || gas.Contains(edge.Second)) ||
                edge.State == KsProcgenGasEdgeState.Open &&
                (!(gas.Contains(edge.First) || vacuum.Contains(edge.First)) ||
                 !(gas.Contains(edge.Second) || vacuum.Contains(edge.Second))))
                return Failure(KsProcgenGasClosureStatus.InvalidInput, "InvalidGasEdge");
            var key = Pair(edge.First, edge.Second);
            if (!edges.TryAdd(key, edge.State))
                return Failure(KsProcgenGasClosureStatus.InvalidInput, "DuplicateGasEdge");
        }

        var reached = new HashSet<Vector2i>(protectedCells);
        var queue = new Queue<Vector2i>(KsProcgenGeometry.SortCells(protectedCells));
        var leaks = new List<KsProcgenGasEdge>();
        var unknown = new List<KsProcgenGasEdge>();
        var unknownPairs = new HashSet<(Vector2i First, Vector2i Second)>();
        while (queue.TryDequeue(out var cell))
        {
            foreach (var offset in Cardinal)
            {
                var neighbor = cell + offset;
                var state = edges.GetValueOrDefault(Pair(cell, neighbor), KsProcgenGasEdgeState.Unknown);
                if (state == KsProcgenGasEdgeState.Blocked)
                    continue;
                if (state == KsProcgenGasEdgeState.Unknown || !gas.Contains(neighbor) &&
                    !vacuum.Contains(neighbor))
                {
                    if (unknownPairs.Add(Pair(cell, neighbor)))
                        unknown.Add(new KsProcgenGasEdge(cell, neighbor, KsProcgenGasEdgeState.Unknown));
                    continue;
                }
                if (vacuum.Contains(neighbor))
                {
                    leaks.Add(new KsProcgenGasEdge(cell, neighbor, KsProcgenGasEdgeState.Open));
                    continue;
                }
                if (reached.Add(neighbor))
                    queue.Enqueue(neighbor);
            }
        }

        var status = leaks.Count > 0 ? KsProcgenGasClosureStatus.LeakToVacuum :
            unknown.Count > 0 ? KsProcgenGasClosureStatus.UnverifiedBoundary :
            KsProcgenGasClosureStatus.PreliminarilyClosed;
        return new KsProcgenGasClosureResult
        {
            Status = status,
            Issue = status switch
            {
                KsProcgenGasClosureStatus.LeakToVacuum => new KsProcgenIssue("GasLeakToVacuum",
                    "A protected gas cell reaches an explicit vacuum sink."),
                KsProcgenGasClosureStatus.UnverifiedBoundary => new KsProcgenIssue("UnverifiedGasBoundary",
                    "A protected gas cell reaches a boundary with unknown gas behavior."),
                _ => null,
            },
            ReachedGasCells = KsProcgenGeometry.SortCells(reached),
            LeakEdges = Sort(leaks),
            UnknownEdges = Sort(unknown),
        };
    }

    private static (Vector2i First, Vector2i Second) Pair(Vector2i first, Vector2i second) =>
        first.Y < second.Y || first.Y == second.Y && first.X <= second.X
            ? (first, second) : (second, first);

    private static KsProcgenGasEdge[] Sort(IEnumerable<KsProcgenGasEdge> edges) =>
        edges.OrderBy(edge => edge.First.Y).ThenBy(edge => edge.First.X)
            .ThenBy(edge => edge.Second.Y).ThenBy(edge => edge.Second.X).ToArray();

    private static KsProcgenGasClosureResult Failure(KsProcgenGasClosureStatus status, string code) => new()
    {
        Status = status,
        Issue = new KsProcgenIssue(code, "Gas adjacency input or budget could not be satisfied."),
    };
}
