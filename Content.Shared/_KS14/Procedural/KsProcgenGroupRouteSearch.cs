using System.Linq;

namespace Content.Shared._KS14.Procedural;

public sealed class KsProcgenGroupRouteSearchResult
{
    public KsProcgenEntranceSceneStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenGroupRouteResult? Accepted { get; init; }
    public int OrderProbes { get; init; }
    public int ExpandedCells { get; init; }
}

public static partial class KsProcgenGroupRoutePlanner
{
    /// <summary>
    /// Retry complete route proposals in deterministic group orders. Does not search alternate trees
    /// within an order or prove exhaustive geometric infeasibility. Rejected claims never survive.
    /// </summary>
    public static KsProcgenGroupRouteSearchResult Search(KsProcgenRequest request, string configurationId,
        KsProcgenGroupRouteContext context, int maximumOrderProbes = 64, int maximumExpandedCells = 131_072)
    {
        var probes = 0;
        var expanded = 0;
        KsProcgenGroupRouteSearchResult Fail(KsProcgenEntranceSceneStatus status, KsProcgenIssue? issue) => new()
        {
            Status = status, Issue = issue, OrderProbes = probes, ExpandedCells = expanded,
        };
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out var issue))
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, issue);
        var configuration = shape!.EntranceContract?.Configurations.SingleOrDefault(configuration => configuration.Id == configurationId);
        if (configuration == null || configuration.Weight <= 0f || maximumOrderProbes is < 0 or > 64 ||
            maximumExpandedCells is < 0 or > 1_000_000)
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput,
                new("InvalidGroupRouteSearch", "A positive-weight configuration and bounded search allowances are required."));
        foreach (var order in GroupOrders(configuration.Groups))
        {
            if (probes >= maximumOrderProbes)
                return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded,
                    new("GroupRouteOrderProbeBudget", "Complete group-order proposals exhausted their shared allowance."));
            probes++;
            var proposal = Plan(request, configurationId, context, maximumExpandedCells: maximumExpandedCells - expanded,
                groupOrder: order);
            expanded += proposal.ExpandedCells;
            if (proposal.Status == KsProcgenEntranceSceneStatus.Candidate)
                return new() { Status = proposal.Status, Accepted = proposal, OrderProbes = probes, ExpandedCells = expanded };
            // Only routing failures can benefit from changing greedy group order. Invariant input,
            // mandatory-terminal, preserved-component and final scene failures keep their diagnostic.
            if (proposal.Status != KsProcgenEntranceSceneStatus.Rejected ||
                proposal.Issue?.Code is not ("NoGroupRouteProposal" or "ForbiddenGroupRouteTerminal"))
                return Fail(proposal.Status, proposal.Issue);
            issue = proposal.Issue;
        }
        return Fail(KsProcgenEntranceSceneStatus.Rejected, issue);
    }

    /// <summary>Canonical order, reverse, then the remaining lexicographic permutations, lazily.</summary>
    private static IEnumerable<IReadOnlyList<string>> GroupOrders(IReadOnlyList<KsProcgenEntranceGroup> groups)
    {
        var canonical = groups.Select(group => group.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        yield return canonical;
        if (canonical.Length < 2)
            yield break;
        var reverse = canonical.Reverse().ToArray();
        yield return reverse;
        var current = canonical.ToArray();
        while (true)
        {
            var pivot = current.Length - 2;
            while (pivot >= 0 && StringComparer.Ordinal.Compare(current[pivot], current[pivot + 1]) >= 0)
                pivot--;
            if (pivot < 0)
                yield break;
            var successor = current.Length - 1;
            while (StringComparer.Ordinal.Compare(current[successor], current[pivot]) <= 0)
                successor--;
            (current[pivot], current[successor]) = (current[successor], current[pivot]);
            Array.Reverse(current, pivot + 1, current.Length - pivot - 1);
            if (!current.SequenceEqual(reverse))
                yield return current.ToArray();
        }
    }
}
