using System.Linq;

namespace Content.Shared._KS14.Procedural;

internal sealed record KsProcgenSizeGoalAllocation(
    IReadOnlyDictionary<int, IReadOnlyList<KsProcgenSizeGoalLayout>> Layouts, int QuotaProbes, int FitProbes)
{
    public string AllocationId { get; init; } = string.Empty;
}

/// <summary>
/// Assigns request-wide room counts to separate entrance networks. Search is atomic:
/// either every network receives a feasible quota, or no goal layouts are returned. Metadata work
/// is capped separately from the graph and seam allowances used by the eventual partition solves.
/// </summary>
internal static class KsProcgenSizeGoalAllocator
{
    internal static KsProcgenSizeGoalAllocation Allocate(string requestId,
        IReadOnlyList<KsProcgenEntranceComponentWitness> components,
        IReadOnlyList<KsProcgenRoomSizeGoal> goals, int minimumRoomCells,
        IReadOnlySet<string>? rejectedAllocations = null, int previousQuotaProbes = 0, int previousFitProbes = 0,
        KsProcgenPartitionWorkBudget? workBudget = null)
    {
        var selected = new Dictionary<int, IReadOnlyList<KsProcgenSizeGoalLayout>>();
        var quotaProbes = previousQuotaProbes;
        var fitProbes = previousFitProbes;
        var selectedCounts = new Dictionary<int, int[]>();
        var allocationId = string.Empty;
        var roomCount = goals.Sum(goal => (long) goal.TargetCount);
        if (components.Count is < 2 or > 32 || roomCount < components.Count || roomCount > 256)
            return new(selected, quotaProbes, fitProbes);
        var networks = components.OrderBy(component => component.Component).ToArray();
        var orderedGoals = goals.OrderBy(goal => goal.Id, StringComparer.Ordinal).ToArray();
        var remaining = orderedGoals.Select(goal => goal.TargetCount).ToArray();

        bool Search(int networkIndex)
        {
            var counts = new int[orderedGoals.Length];
            var lastNetwork = networkIndex == networks.Length - 1;
            if (lastNetwork) remaining.CopyTo(counts, 0);
            do
            {
                if (quotaProbes >= 4096 || fitProbes >= 64 || workBudget?.ExhaustedCode != null) return false;
                quotaProbes++;
                var assignedCount = counts.Sum();
                // Every remaining network needs at least one room; quotas are never duplicated.
                if (assignedCount > 0 && remaining.Sum() - assignedCount >= networks.Length - networkIndex - 1)
                {
                    fitProbes++;
                    var quota = orderedGoals.Select((goal, index) => new KsProcgenRoomSizeGoal
                    {
                        Id = goal.Id, MinCells = goal.MinCells, MaxCells = goal.MaxCells,
                        TargetCount = counts[index],
                    }).Where(goal => goal.TargetCount != 0).ToArray();
                    var network = networks[networkIndex];
                    var layouts = KsProcgenSizeGoalSubdivision.Propose(
                        $"{requestId}/EntranceNetwork/{network.Component}", network.Cells, quota, minimumRoomCells, workBudget: workBudget);
                    if (layouts.Count != 0)
                    {
                        selected[network.Component] = layouts;
                        selectedCounts[network.Component] = counts.ToArray();
                        for (var index = 0; index < remaining.Length; index++) remaining[index] -= counts[index];
                        if (lastNetwork)
                        {
                            var identity = KsProcgenStableHash.Create();
                            identity.AddString("ks-procgen-size-quota-v1");
                            foreach (var goal in orderedGoals) identity.AddString(goal.Id);
                            foreach (var component in networks)
                            {
                                identity.AddInt(component.Component);
                                foreach (var count in selectedCounts[component.Component]) identity.AddInt(count);
                            }
                            allocationId = identity.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            if (rejectedAllocations?.Contains(allocationId) != true) return true;
                        }
                        else if (Search(networkIndex + 1)) return true;
                        for (var index = 0; index < remaining.Length; index++) remaining[index] += counts[index];
                        selected.Remove(network.Component);
                        selectedCounts.Remove(network.Component);
                    }
                }
                if (lastNetwork) break;
                // Bounded mixed-radix enumeration; first goal count changes fastest. The remaining
                // quota is restored before advancing, so backtracking cannot corrupt later vectors.
                var digit = 0;
                while (digit < counts.Length && counts[digit] == remaining[digit]) counts[digit++] = 0;
                if (digit == counts.Length) break;
                counts[digit]++;
            } while (true);
            return false;
        }

        if (!Search(0))
        {
            selected.Clear();
            allocationId = string.Empty;
        }
        return new(selected, quotaProbes, fitProbes) { AllocationId = allocationId };
    }
}
