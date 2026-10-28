using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>
/// Bounded strip search for irregular networks, including split columns around holes.
/// Prefix areas and overlapping boundary spans predict final floor sizes. The normal seam solver
/// remains authoritative for protected routes, wall ownership and clean door approaches.
/// </summary>
internal static class KsProcgenIrregularSizeGoalSubdivision
{
    internal static IReadOnlyList<KsProcgenSizeGoalLayout> Propose(string networkId,
        IReadOnlyList<Vector2i> cells, IReadOnlyList<KsProcgenRoomSizeGoal> goals, int minimumRoomCells,
        KsProcgenPartitionWorkBudget? workBudget = null)
    {
        var count = goals.Sum(goal => (long) goal.TargetCount);
        if (cells.Count is < 1 or > 4096 || count is < 1 or > 64) return [];
        var ordered = goals.OrderByDescending(goal => goal.MaxCells).ThenBy(goal => goal.Id, StringComparer.Ordinal)
            .SelectMany(goal => Enumerable.Repeat(goal, goal.TargetCount)).ToArray();
        var layouts = new List<KsProcgenSizeGoalLayout>();
        foreach (var reflected in new[] { false, true })
        {
            if (reflected && count == 1) break;
            var spatialGoals = reflected ? ordered.Reverse().ToArray() : ordered;
            foreach (var acrossX in new[] { true, false })
            {
                int Coordinate(Vector2i cell) => acrossX ? cell.X : cell.Y;
                int CrossCoordinate(Vector2i cell) => acrossX ? cell.Y : cell.X;
                var minimum = cells.Min(Coordinate);
                var length = (long) cells.Max(Coordinate) - minimum + 1;
                if (length is < 2 or > 4096 || length < count * 2) continue;
                var columns = cells.GroupBy(Coordinate).OrderBy(group => group.Key).ToArray();
                if (columns.Length != length) continue;
                var spans = columns.Select(group => group.Select(CrossCoordinate).ToHashSet()).ToArray();
                var splitColumns = spans.Any(span => (long) span.Max() - span.Min() + 1 != span.Count);
                // Non-convex strips need graph witnesses. Never add an unmetered flood to a caller
                // that only requested the historical bounded column-convex metadata heuristic.
                if (splitColumns && workBudget == null) continue;
                var overlaps = new int[columns.Length];
                var prefix = new int[columns.Length + 1];
                var connected = true;
                for (var index = 0; index < columns.Length; index++)
                {
                    prefix[index + 1] = prefix[index] + columns[index].Count();
                    if (index == 0) continue;
                    overlaps[index] = spans[index].Count(cross => spans[index - 1].Contains(cross));
                    if (overlaps[index] < 1) connected = false;
                }
                if (!connected) continue;
                var cuts = new int[spatialGoals.Length + 1];
                var probes = 0;
                var vectors = new List<int[]>();
                var connectivity = new Dictionary<(int Start, int End), bool>();
                bool Connected(int start, int end)
                {
                    if (!splitColumns) return true;
                    if (connectivity.TryGetValue((start, end), out var connectedStrip)) return connectedStrip;
                    var strip = columns.Skip(start).Take(end - start).SelectMany(column => column).ToHashSet();
                    if (!workBudget!.TryConnectedComponents(strip, out var components)) return false;
                    connectedStrip = components.Count == 1;
                    connectivity.Add((start, end), connectedStrip);
                    return connectedStrip;
                }
                void Search(int roomIndex, int start)
                {
                    var goal = spatialGoals[roomIndex];
                    var last = roomIndex == spatialGoals.Length - 1;
                    var maximumEnd = columns.Length - (spatialGoals.Length - roomIndex - 1) * 2;
                    var loss = start == 0 ? 0 : overlaps[start] - 1;
                    // The threshold and both cardinal approach tiles must share a row/column.
                    if (start != 0 && !spans[start].Any(cross =>
                            spans[start - 1].Contains(cross) && spans[start + 1].Contains(cross))) return;
                    for (var end = last ? columns.Length : start + 2; end <= maximumEnd; end++)
                    {
                        if (probes >= 4096 || vectors.Count >= 8 || workBudget?.ExhaustedCode != null) return;
                        probes++;
                        var size = prefix[end] - prefix[start] - loss;
                        if (size > goal.MaxCells) break;
                        if (size < Math.Max(minimumRoomCells, goal.MinCells)) continue;
                        if (!Connected(start, end)) continue;
                        cuts[roomIndex + 1] = end;
                        if (last) vectors.Add(cuts.ToArray());
                        else Search(roomIndex + 1, end);
                    }
                }
                Search(0, 0);
                if (workBudget?.ExhaustedCode != null) return [];
                for (var variant = 0; variant < vectors.Count; variant++)
                {
                    var zones = new List<KsProcgenZone>();
                    for (var index = 0; index < spatialGoals.Length; index++)
                    {
                        var start = vectors[variant][index];
                        var end = vectors[variant][index + 1];
                        zones.Add(new($"{networkId}/SizeGoal/{index:D3}", KsProcgenZoneKind.RoomProposal,
                            KsProcgenGeometry.SortCells(cells.Where(cell => Coordinate(cell) - minimum >= start &&
                                Coordinate(cell) - minimum < end))));
                    }
                    layouts.Add(new(acrossX, KsProcgenReadOnly.Copy(zones)) { Reflected = reflected, WidthVariant = variant });
                }
                if (count == 1 && vectors.Count != 0) break;
            }
        }
        return KsProcgenReadOnly.Copy(layouts);
    }
}
