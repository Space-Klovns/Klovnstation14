using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>
/// Carves one mandatory utility room per already inspected entrance network before seam solving.
/// All candidates and rejected connectivity checks share the caller's partition work allowance.
/// A completed allocation is still subject to the ordinary physical partition and scene audits.
/// </summary>
public static class KsProcgenUtilityRoomAllocator
{
    public const int AlgorithmVersion = 3;
    private static readonly Vector2i[] Cardinal = [new(0, -1), new(-1, 0), new(1, 0), new(0, 1)];

    public static bool TryAllocate(string networkId, IReadOnlyList<KsProcgenZone> source,
        KsProcgenNormalizedShape shape, IReadOnlySet<Vector2i> protectedFloor,
        KsProcgenElectricalDistribution distribution, KsProcgenPartitionWorkBudget work,
        out IReadOnlyList<KsProcgenZone> zones, out KsProcgenIssue? issue,
        int candidateOrdinal = 0, int preferredInteriorCells = 0)
    {
        zones = []; issue = null;
        if (string.IsNullOrWhiteSpace(networkId) || networkId.Length > 512 || source == null ||
            source.Count is < 1 or > 256 || source.Any(zone => zone == null || zone.UtilityRoom != null ||
                string.IsNullOrWhiteSpace(zone.Id) || zone.Cells.Count is < 1 or > 4096 || !Enum.IsDefined(zone.Kind)) ||
            source.Select(zone => zone.Id).Distinct(StringComparer.Ordinal).Count() != source.Count ||
            source.Sum(zone => (long) zone.Cells.Count) > 4096 || shape == null || protectedFloor == null || distribution == null || work == null ||
            candidateOrdinal is < 0 or > 65_535 || preferredInteriorCells is < 0 or > 64)
        { issue = new("InvalidUtilityRoomAllocation", "Utility allocation needs bounded, unreserved owned zones and shared work."); return false; }
        var floor = new HashSet<Vector2i>();
        foreach (var cell in source.SelectMany(zone => zone.Cells))
        {
            if (!work.Probe()) return Budget(out issue);
            if (!shape.ContainsTarget(cell) || !floor.Add(cell))
            { issue = new("InvalidUtilityRoomAllocation", "Utility allocation cannot overlap zones or leave the owned target."); return false; }
        }
        if (!protectedFloor.IsSubsetOf(floor))
        { issue = new("InvalidUtilityRoomAllocation", "Protected routes must belong to this network."); return false; }
        if (!work.TryConnectedComponents(floor, out var sourceComponents)) return Budget(out issue);
        if (sourceComponents.Count != 1)
        { issue = new("InvalidUtilityRoomAllocation", "Utility rooms cannot join separate entrance networks."); return false; }
        var utilityId = networkId + "/UtilityRoom";
        if (source.Any(zone => zone.Id == utilityId))
        { issue = new("InvalidUtilityRoomAllocation", "The reserved utility room ID collides with an ordinary zone."); return false; }
        var sizes = distribution.ClosetInteriorSizes.SelectMany(size => new[] { size, new Vector2i(size.Y, size.X) })
            .Distinct().OrderBy(size => Math.Max(0, preferredInteriorCells - size.X * size.Y))
            .ThenBy(size => size.X * size.Y).ThenBy(size => size.X).ToArray();
        // Prefer enclosing bands along the network edge, then use a stable spatial order. The
        // protected route and real door approach are hard checks, independent of this preference.
        var reuseHull = shape.EnvelopeCells.Count != 0;
        var origins = (reuseHull ? floor.Concat(floor.Select(cell => cell - Vector2i.One)) : floor)
            .Distinct().ToArray();
        var minimum = new Vector2i(floor.Min(cell => cell.X), floor.Min(cell => cell.Y));
        var maximum = new Vector2i(floor.Max(cell => cell.X), floor.Max(cell => cell.Y));
        var accepted = 0;
        // Prefer adequate interior space before moving the enclosing band farther into the map.
        foreach (var size in sizes)
        foreach (var origin in origins.OrderByDescending(cell => reuseHull ?
                     (cell.X == minimum.X - 1 ? 1 : 0) + (cell.Y == minimum.Y - 1 ? 1 : 0) +
                     (cell.X + size.X + 1 == maximum.X + 1 ? 1 : 0) + (cell.Y + size.Y + 1 == maximum.Y + 1 ? 1 : 0) : 0)
                 .ThenByDescending(cell => Cardinal.Count(step => !floor.Contains(cell + step)))
                 .ThenBy(cell => cell.Y).ThenBy(cell => cell.X))
        {
            var interior = new List<Vector2i>(); var enclosure = new List<Vector2i>(); var occupied = new HashSet<Vector2i>();
            var reusedHull = new List<Vector2i>();
            var legal = true;
            for (var y = 0; y < size.Y + 2; y++)
            for (var x = 0; x < size.X + 2; x++)
            {
                if (!work.Probe()) return Budget(out issue);
                var cell = origin + new Vector2i(x, y);
                var inside = x > 0 && x <= size.X && y > 0 && y <= size.Y;
                if (!shape.CanWrite(cell) || protectedFloor.Contains(cell)) legal = false;
                if (floor.Contains(cell)) occupied.Add(cell);
                else if (!inside && !shape.ContainsTarget(cell) && shape.ContainsEnvelope(cell)) reusedHull.Add(cell);
                else legal = false;
                if (inside) interior.Add(cell);
                else enclosure.Add(cell);
            }
            if (!legal) continue;
            var remainder = floor.Where(cell => !occupied.Contains(cell)).ToHashSet();
            if (remainder.Count == 0) continue;
            if (!work.TryConnectedComponents(remainder, out var remainderComponents)) return Budget(out issue);
            if (remainderComponents.Count != 1) continue;
            foreach (var inside in interior)
            foreach (var step in Cardinal)
            {
                if (!work.Probe()) return Budget(out issue);
                var threshold = inside + step; var outside = threshold + step;
                if (!enclosure.Contains(threshold) || reusedHull.Contains(threshold) || !remainder.Contains(outside)) continue;
                var carved = new List<KsProcgenZone>();
                foreach (var zone in source.OrderBy(zone => zone.Id, StringComparer.Ordinal))
                {
                    var remaining = new HashSet<Vector2i>();
                    foreach (var cell in zone.Cells)
                    {
                        if (!work.Probe()) return Budget(out issue);
                        if (!occupied.Contains(cell)) remaining.Add(cell);
                    }
                    if (!work.TryConnectedComponents(remaining, out var fragments)) return Budget(out issue);
                    for (var index = 0; index < fragments.Count; index++)
                        carved.Add(zone with { Id = fragments.Count == 1 ? zone.Id : zone.Id + "/UtilityRemainder/" + index,
                            Cells = fragments[index] });
                }
                // One fragmented reservation may exceed the zone limit while another fits.
                if (carved.Count >= 256) continue;
                var reservation = new KsProcgenUtilityRoomReservation(distribution.ClosetTheme, origin + Vector2i.One,
                    size, threshold, inside, outside, reusedHullCells: reusedHull);
                carved.Add(new(utilityId, KsProcgenZoneKind.RoomProposal, KsProcgenGeometry.SortCells(occupied)) { UtilityRoom = reservation });
                if (accepted++ < candidateOrdinal) continue;
                zones = KsProcgenReadOnly.Copy(carved.OrderBy(zone => zone.Id, StringComparer.Ordinal));
                return true;
            }
        }
        issue = new("UtilityRoomNoPlacement", "No enclosed utility-room candidate fits this network and its routes; other layouts may still be possible.");
        return false;
    }

    private static bool Budget(out KsProcgenIssue? issue)
    { issue = new("UtilityRoomAllocationBudget", "Utility-room placement exhausted the shared partition allowance."); return false; }
}
