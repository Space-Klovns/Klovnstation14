using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenAlternateRouteStatus : byte
{
    Complete,
    InvalidInput,
    BudgetExceeded,
}

public sealed record KsProcgenRoutePair(string Id, Vector2i First, Vector2i Second);

public sealed record KsProcgenChokeRouteOutcome(
    Vector2i Choke,
    bool EndpointRemoved,
    bool ConnectedWithoutChoke,
    int ShortestStepsWithoutChoke);

public sealed record KsProcgenAlternateRoutePairResult(
    string Id,
    Vector2i First,
    Vector2i Second,
    int BaselineShortestSteps,
    IReadOnlyList<KsProcgenChokeRouteOutcome> Chokes)
{
    public int ChokesWithAlternateRoute => Chokes.Count(outcome => outcome.ConnectedWithoutChoke);
}

/// <summary>
/// Bounded cardinal clean-route measurements. Door operation and reference-actor collision must
/// already be reflected in the supplied walkable mask; this does not verify either in-engine.
/// </summary>
public sealed class KsProcgenAlternateRouteResult
{
    public KsProcgenAlternateRouteStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenAlternateRoutePairResult> Pairs { get; init; } = [];
    public int Searches { get; init; }
    public int ExpandedCells { get; init; }
    public bool EngineTraversalVerified => false;
}

/// <summary>
/// For each named terminal pair, remove each nominated choke cell separately and search for a
/// remaining clean cardinal route. An endpoint removed by a choke is reported explicitly.
/// </summary>
public static class KsProcgenAlternateRouteAnalyzer
{
    private static readonly Vector2i[] Cardinal =
        [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    private enum SearchStatus : byte
    {
        Connected,
        Disconnected,
        BudgetExceeded,
    }

    public static KsProcgenAlternateRouteResult Analyze(
        IReadOnlySet<Vector2i> walkableCells,
        IReadOnlySet<Vector2i> vaultOnlyCells,
        IReadOnlyList<KsProcgenRoutePair> pairs,
        IReadOnlySet<Vector2i> nominatedChokes,
        int maxCells = 65_536,
        int maxPairs = 64,
        int maxChokes = 64,
        int maxSearches = 4_096,
        int maxExpandedCells = 1_000_000)
    {
        if (walkableCells == null || vaultOnlyCells == null || pairs == null ||
            nominatedChokes == null || maxCells <= 0 || maxCells > 65_536 ||
            maxPairs <= 0 || maxPairs > 256 || maxChokes <= 0 || maxChokes > 256 ||
            maxSearches <= 0 || maxSearches > 65_536 ||
            maxExpandedCells <= 0 || maxExpandedCells > 4_000_000 ||
            vaultOnlyCells.Any(cell => !walkableCells.Contains(cell)))
            return Failure(KsProcgenAlternateRouteStatus.InvalidInput, "InvalidAlternateRouteInput");

        var clean = new HashSet<Vector2i>(walkableCells);
        clean.ExceptWith(vaultOnlyCells);
        if (clean.Count > maxCells || pairs.Count > maxPairs || nominatedChokes.Count > maxChokes ||
            (long) pairs.Count * (nominatedChokes.Count + 1) > maxSearches)
            return Failure(KsProcgenAlternateRouteStatus.BudgetExceeded, "AlternateRouteBudget");
        if (pairs.Any(pair => pair == null || string.IsNullOrWhiteSpace(pair.Id) ||
                              pair.First == pair.Second || !clean.Contains(pair.First) ||
                              !clean.Contains(pair.Second)) ||
            pairs.Select(pair => pair.Id).Distinct(StringComparer.Ordinal).Count() != pairs.Count ||
            nominatedChokes.Any(choke => !clean.Contains(choke)))
            return Failure(KsProcgenAlternateRouteStatus.InvalidInput, "InvalidAlternateRouteContract");

        var orderedChokes = nominatedChokes.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
        var results = new List<KsProcgenAlternateRoutePairResult>();
        var searches = 0;
        var expanded = 0;
        foreach (var pair in pairs.OrderBy(pair => pair.Id, StringComparer.Ordinal))
        {
            searches++;
            var baselineStatus = Search(clean, pair.First, pair.Second, null,
                maxExpandedCells, ref expanded, out var baselineSteps);
            if (baselineStatus == SearchStatus.BudgetExceeded)
                return Failure(KsProcgenAlternateRouteStatus.BudgetExceeded,
                    "AlternateRouteExpansionBudget");
            if (baselineStatus == SearchStatus.Disconnected)
                return Failure(KsProcgenAlternateRouteStatus.InvalidInput,
                    "RoutePairDisconnected");

            var outcomes = new List<KsProcgenChokeRouteOutcome>();
            foreach (var choke in orderedChokes)
            {
                if (choke == pair.First || choke == pair.Second)
                {
                    outcomes.Add(new KsProcgenChokeRouteOutcome(choke, true, false, -1));
                    continue;
                }

                searches++;
                var status = Search(clean, pair.First, pair.Second, choke,
                    maxExpandedCells, ref expanded, out var steps);
                if (status == SearchStatus.BudgetExceeded)
                    return Failure(KsProcgenAlternateRouteStatus.BudgetExceeded,
                        "AlternateRouteExpansionBudget");
                outcomes.Add(new KsProcgenChokeRouteOutcome(choke, false,
                    status == SearchStatus.Connected, steps));
            }
            results.Add(new KsProcgenAlternateRoutePairResult(pair.Id, pair.First,
                pair.Second, baselineSteps, outcomes));
        }

        return new KsProcgenAlternateRouteResult
        {
            Status = KsProcgenAlternateRouteStatus.Complete,
            Pairs = results,
            Searches = searches,
            ExpandedCells = expanded,
        };
    }

    private static SearchStatus Search(
        IReadOnlySet<Vector2i> clean,
        Vector2i first,
        Vector2i second,
        Vector2i? removed,
        int maximumExpanded,
        ref int expanded,
        out int steps)
    {
        steps = -1;
        var visited = new HashSet<Vector2i> { first };
        var queue = new Queue<(Vector2i Cell, int Steps)>();
        queue.Enqueue((first, 0));
        while (queue.TryDequeue(out var current))
        {
            if (expanded >= maximumExpanded)
                return SearchStatus.BudgetExceeded;
            expanded++;
            if (current.Cell == second)
            {
                steps = current.Steps;
                return SearchStatus.Connected;
            }

            foreach (var offset in Cardinal)
            {
                if (!TryAdd(current.Cell, offset, out var neighbor) ||
                    neighbor == removed || !clean.Contains(neighbor) || !visited.Add(neighbor))
                    continue;
                queue.Enqueue((neighbor, current.Steps + 1));
            }
        }

        return SearchStatus.Disconnected;
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

    private static KsProcgenAlternateRouteResult Failure(
        KsProcgenAlternateRouteStatus status, string code) => new()
    {
        Status = status,
        Issue = new KsProcgenIssue(code,
            "Named clean routes or the configured alternate-route work budget are invalid."),
    };
}
