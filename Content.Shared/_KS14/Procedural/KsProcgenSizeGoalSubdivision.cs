using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

internal sealed record KsProcgenSizeGoalLayout(bool AcrossX, IReadOnlyList<KsProcgenZone> Zones)
{
    public int WidthVariant { get; init; }
    public bool Reflected { get; init; }
}

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
        if (width * height != cells.Count)
            return KsProcgenIrregularSizeGoalSubdivision.Propose(networkId, cells, goals, minimumRoomCells);
        if (width < 2 || height < 2)
            return [];
        var ordered = goals.OrderByDescending(goal => goal.MaxCells)
            .ThenBy(goal => goal.Id, StringComparer.Ordinal)
            .SelectMany(goal => Enumerable.Repeat(goal, goal.TargetCount)).ToArray();
        var layouts = new List<KsProcgenSizeGoalLayout>();
        foreach (var reflected in new[] { false, true })
        {
            if (reflected && ordered.Length == 1) break;
            foreach (var horizontal in new[] { true, false })
            {
                var length = (int) (horizontal ? width : height);
                var crossSection = (int) (horizontal ? height : width);
                var widths = new int[ordered.Length];
                var minimums = new int[ordered.Length];
                var maximums = new int[ordered.Length];
                var minimumTotal = 0;
                var maximumTotal = 0;
                var feasible = true;
                for (var index = 0; index < ordered.Length; index++)
                {
                    var firstSpatialRoom = reflected ? index == ordered.Length - 1 : index == 0;
                    var wallLoss = firstSpatialRoom ? 0 : crossSection - 1;
                    var minimum = Math.Max(minimumRoomCells, ordered[index].MinCells);
                    widths[index] = Math.Max(2, (minimum + wallLoss + crossSection - 1) / crossSection);
                    minimums[index] = widths[index];
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
                var variants = WidthVariants(widths, minimums, maximums);
                for (var variantIndex = 0; variantIndex < variants.Count; variantIndex++)
                {
                    var proposed = new List<KsProcgenZone>();
                    var start = 0;
                    for (var index = 0; index < widths.Length; index++)
                    {
                        var end = start + variants[variantIndex][index];
                        var selected = cells.Where(cell =>
                        {
                            var coordinate = horizontal ? cell.X - minimumX : cell.Y - minimumY;
                            if (reflected) coordinate = length - 1 - coordinate;
                            return coordinate >= start && coordinate < end;
                        });
                        // Keep room identities stable across width variants. Selected cell membership
                        // enters semantic identity; discarded layout ordinals do not.
                        var spatialIndex = reflected ? ordered.Length - 1 - index : index;
                        proposed.Add(new($"{networkId}/SizeGoal/{spatialIndex:D3}", KsProcgenZoneKind.RoomProposal,
                            KsProcgenGeometry.SortCells(selected)));
                        start = end;
                    }
                    layouts.Add(new(horizontal, proposed.OrderBy(zone => zone.Id, StringComparer.Ordinal).ToReadOnlyList())
                        { WidthVariant = variantIndex, Reflected = reflected });
                }
                // One-room layouts are identical on both axes, including IDs and membership.
                if (ordered.Length == 1) break;
            }
        }
        return KsProcgenReadOnly.Copy(layouts);
    }

    /// <summary>
    /// Deterministic breadth-first neighbors redistribute one column between two rooms. Every
    /// vector respects the same width intervals and total length. At most eight vectors and
    /// 4096 donor/recipient checks per axis bound metadata work independently of seam solving.
    /// </summary>
    private static IReadOnlyList<int[]> WidthVariants(int[] first, int[] minimums, int[] maximums)
    {
        var variants = new List<int[]> { first };
        var probes = 0;
        for (var cursor = 0; cursor < variants.Count; cursor++)
        {
            var current = variants[cursor];
            for (var donor = 0; donor < current.Length; donor++)
            {
                if (current[donor] <= minimums[donor]) continue;
                for (var recipient = 0; recipient < current.Length; recipient++)
                {
                    if (donor == recipient) continue;
                    if (probes++ >= 4096) return variants;
                    if (current[recipient] >= maximums[recipient]) continue;
                    var next = current.ToArray();
                    next[donor]--;
                    next[recipient]++;
                    if (variants.Any(previous => previous.SequenceEqual(next))) continue;
                    variants.Add(next);
                    if (variants.Count >= 8) return variants;
                }
            }
        }
        return variants;
    }
}
