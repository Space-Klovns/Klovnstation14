using System.Linq;

namespace Content.Shared._KS14.Procedural;

/// <summary>Shared request and authoring goal validation; no geometry or engine work.</summary>
public static class KsProcgenGoalValidation
{
    public static bool TryValidate(IReadOnlyList<KsProcgenRoomSizeGoal>? sizeMix, KsProcgenWindowGoal? windowGoal,
        out KsProcgenIssue? issue)
    {
        issue = null;
        if (sizeMix == null)
        {
            issue = new KsProcgenIssue("InvalidSizeMix", "Size goals cannot be null.");
            return false;
        }
        if (windowGoal == null ||
            !float.IsFinite(windowGoal.ExteriorWindowFraction) ||
            windowGoal.ExteriorWindowFraction is < 0f or > 1f ||
            windowGoal.ToleranceCells is < 0 or > 65_536 ||
            windowGoal.MinimumCount is < 0 or > 65_536 ||
            windowGoal.MaximumCount is < 0 or > 65_536 ||
            windowGoal.MaximumCount.HasValue &&
            windowGoal.MaximumCount.Value < windowGoal.MinimumCount)
        {
            issue = new KsProcgenIssue("InvalidWindowGoal",
                "Window fraction, tolerance, and count bounds must be finite and supported.");
            return false;
        }
        var sizeIds = new HashSet<string>(StringComparer.Ordinal);
        var requestedRoomCount = 0L;
        foreach (var goal in sizeMix)
        {
            if (goal == null || string.IsNullOrWhiteSpace(goal.Id) || !sizeIds.Add(goal.Id) ||
                goal.MinCells <= 0 || goal.MaxCells < goal.MinCells ||
                goal.MaxCells > 65_536 || goal.TargetCount <= 0)
            {
                issue = new KsProcgenIssue("InvalidSizeMix",
                    "Size goals need unique IDs, positive ordered area bounds, and positive counts.");
                return false;
            }
            requestedRoomCount += goal.TargetCount;
            if (requestedRoomCount > 4_096)
            {
                issue = new KsProcgenIssue("SizeMixBudget",
                    "At most 4096 preliminary room zones may be requested.");
                return false;
            }
        }
        var orderedSizes = sizeMix.OrderBy(goal => goal.MinCells).ToArray();
        for (var index = 1; index < orderedSizes.Length; index++)
        {
            if (orderedSizes[index - 1].MaxCells < orderedSizes[index].MinCells)
                continue;
            issue = new KsProcgenIssue("OverlappingSizeMix",
                "Size bands must not overlap so each final room has one size class.");
            return false;
        }

        return true;
    }
}
