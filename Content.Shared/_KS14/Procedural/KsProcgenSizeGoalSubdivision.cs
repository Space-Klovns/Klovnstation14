using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

internal sealed record KsProcgenSizeGoalLayout(bool AcrossX, IReadOnlyList<KsProcgenZone> Zones);

/// <summary>
/// Bounded rectangular strip heuristic for one owned entrance network. Inclusive size bands count
/// final floors: every strip after the first loses one wall band except its door threshold.
/// The seam solver still decides whether protected routes, approach tiles and connectivity permit
/// these boundaries. This is not an arbitrary-shape partition search.
/// </summary>
internal static class KsProcgenSizeGoalSubdivision
{
    internal static IReadOnlyList<KsProcgenSizeGoalLayout> Propose(string networkId, IReadOnlyList<Vector2i> cells,
        IReadOnlyList<KsProcgenRoomSizeGoal> goals, int minimumRoomCells)
    {
        var count = goals.Sum(goal => (long) goal.TargetCount);
        if (cells.Count == 0 || count is < 1 or > 256)
            return [];
        var minimumX = cells.Min(cell => cell.X);
        var minimumY = cells.Min(cell => cell.Y);
        var width = (long) cells.Max(cell => cell.X) - minimumX + 1;
        var height = (long) cells.Max(cell => cell.Y) - minimumY + 1;
        // The caller supplies unique connected graph witness cells. Equal bounding area proves
        // complete rectangular coverage, without probing or allocating a potentially huge box.
        if (width * height != cells.Count || width < 2 || height < 2)
            return [];
        var ordered = goals.OrderByDescending(goal => goal.MaxCells)
            .ThenBy(goal => goal.Id, StringComparer.Ordinal)
            .SelectMany(goal => Enumerable.Repeat(goal, goal.TargetCount)).ToArray();
        var layouts = new List<KsProcgenSizeGoalLayout>();
        foreach (var horizontal in new[] { true, false })
        {
            var length = (int) (horizontal ? width : height);
            var crossSection = (int) (horizontal ? height : width);
            var widths = new int[ordered.Length];
            var maximums = new int[ordered.Length];
            var minimumTotal = 0;
            var maximumTotal = 0;
            var feasible = true;
            for (var index = 0; index < ordered.Length; index++)
            {
                var wallLoss = index == 0 ? 0 : crossSection - 1;
                var minimum = Math.Max(minimumRoomCells, ordered[index].MinCells);
                widths[index] = Math.Max(2, (minimum + wallLoss + crossSection - 1) / crossSection);
                maximums[index] = (ordered[index].MaxCells + wallLoss) / crossSection;
                if (widths[index] > maximums[index]) feasible = false;
                minimumTotal += widths[index];
                maximumTotal += maximums[index];
            }
            if (!feasible || minimumTotal > length || maximumTotal < length)
                continue;
            var spare = length - minimumTotal;
            for (var index = 0; index < widths.Length; index++)
            {
                var addition = Math.Min(spare, maximums[index] - widths[index]);
                widths[index] += addition;
                spare -= addition;
            }
            var proposed = new List<KsProcgenZone>();
            var start = 0;
            for (var index = 0; index < widths.Length; index++)
            {
                var end = start + widths[index];
                var selected = cells.Where(cell =>
                {
                    var coordinate = horizontal ? cell.X - minimumX : cell.Y - minimumY;
                    return coordinate >= start && coordinate < end;
                });
                // Padding keeps ordinal ID order equal to spatial strip order beyond ten rooms:
                // the seam solver assigns the wall to the ordinally second room.
                proposed.Add(new($"{networkId}/SizeGoal/{index:D3}", KsProcgenZoneKind.RoomProposal,
                    KsProcgenGeometry.SortCells(selected)));
                start = end;
            }
            layouts.Add(new(horizontal, KsProcgenReadOnly.Copy(proposed)));
            // One-room layouts are identical on both axes, including IDs and membership.
            if (ordered.Length == 1) break;
        }
        return KsProcgenReadOnly.Copy(layouts);
    }
}
