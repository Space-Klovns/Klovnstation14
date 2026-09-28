using System.Linq;

namespace Content.Shared._KS14.Procedural;

/// <summary>
/// Counts final themed room interiors after partition fallback. The earlier pure-fill outcomes
/// describe proposals only; walls and merges can change both area and room count.
/// </summary>
public static class KsProcgenSizeMixAnalyzer
{
    public static IReadOnlyList<KsProcgenSizeMixOutcome> Analyze(
        IReadOnlyList<KsProcgenRoomSizeGoal> goals,
        IReadOnlyList<KsProcgenThemedRegion> regions)
    {
        if (goals == null || regions == null)
            throw new ArgumentNullException(goals == null ? nameof(goals) : nameof(regions));

        var roomAreas = regions.Where(region => region.Kind == KsProcgenZoneKind.RoomProposal)
            .Select(region => region.FloorCells.Count).ToArray();
        return goals.Select(goal => new KsProcgenSizeMixOutcome(goal.Id, goal.TargetCount,
            roomAreas.Count(area => area >= goal.MinCells && area <= goal.MaxCells))).ToArray();
    }
}
