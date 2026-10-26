using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>
/// Mandatory interior and enclosing band, optionally sharing writable generated hull. This is a geometric
/// reservation, not a placed substation or an engine access/atmosphere certificate.
/// </summary>
public sealed class KsProcgenUtilityRoomReservation
{
    internal KsProcgenUtilityRoomReservation(string themeId, Vector2i minimum, Vector2i size,
        Vector2i threshold, Vector2i inside, Vector2i outside, IReadOnlyList<Vector2i>? reusedHullCells = null)
    {
        ThemeId = themeId; Minimum = minimum; Size = size;
        Threshold = threshold; InsideApproach = inside; OutsideApproach = outside;
        var interior = new List<Vector2i>(); var enclosure = new List<Vector2i>();
        for (var y = -1; y <= size.Y; y++)
        for (var x = -1; x <= size.X; x++)
        {
            var cell = minimum + new Vector2i(x, y);
            if (x >= 0 && x < size.X && y >= 0 && y < size.Y) interior.Add(cell);
            else enclosure.Add(cell);
        }
        InteriorCells = KsProcgenGeometry.SortCells(interior);
        EnclosureCells = KsProcgenGeometry.SortCells(enclosure);
        ReusedHullCells = KsProcgenGeometry.SortCells(reusedHullCells ?? []);
        PartitionEnclosureCells = KsProcgenGeometry.SortCells(enclosure.Except(ReusedHullCells));
    }

    public string ThemeId { get; }
    public Vector2i Minimum { get; }
    public Vector2i Size { get; }
    public Vector2i Threshold { get; }
    public Vector2i InsideApproach { get; }
    public Vector2i OutsideApproach { get; }
    public IReadOnlyList<Vector2i> InteriorCells { get; }
    /// <summary>Includes the entrance threshold, which must remain floor rather than a wall.</summary>
    public IReadOnlyList<Vector2i> EnclosureCells { get; }
    /// <summary>Explicit writable envelope claims, never borrowed target or preserved cells.</summary>
    public IReadOnlyList<Vector2i> ReusedHullCells { get; }
    public IReadOnlyList<Vector2i> PartitionEnclosureCells { get; }

    internal bool Matches(KsProcgenZone zone) => KsProcgenAreaNormalizer.ValidId(ThemeId) &&
        zone.Kind == KsProcgenZoneKind.RoomProposal &&
        (Size == new Vector2i(1, 2) || Size == new Vector2i(2, 1) || Size == new Vector2i(2, 2)) &&
        ReusedHullCells.Distinct().Count() == ReusedHullCells.Count && ReusedHullCells.All(EnclosureCells.Contains) &&
        !ReusedHullCells.Contains(Threshold) &&
        zone.Cells.Count == InteriorCells.Count + PartitionEnclosureCells.Count &&
        zone.Cells.ToHashSet().SetEquals(InteriorCells.Concat(PartitionEnclosureCells)) &&
        InteriorCells.Contains(InsideApproach) && EnclosureCells.Contains(Threshold) &&
        Threshold - InsideApproach == OutsideApproach - Threshold &&
        Math.Abs(Threshold.X - InsideApproach.X) + Math.Abs(Threshold.Y - InsideApproach.Y) == 1 &&
        !zone.Cells.Contains(OutsideApproach);

    internal bool Satisfied(string zoneId, KsProcgenPartitionResult partition)
    {
        var floor = partition.FloorCells.ToHashSet(); var walls = partition.WallCells.ToHashSet();
        return InteriorCells.All(floor.Contains) && floor.Contains(Threshold) && floor.Contains(OutsideApproach) &&
            PartitionEnclosureCells.Where(cell => cell != Threshold).All(walls.Contains) &&
            ReusedHullCells.All(cell => !floor.Contains(cell) && !walls.Contains(cell)) &&
            !partition.MergedZoneGroups.Any(group => group.Contains(zoneId, StringComparer.Ordinal)) &&
            partition.DoorOpenings.Count(door => door.FirstZoneId == zoneId || door.SecondZoneId == zoneId) == 1 &&
            partition.DoorOpenings.Any(door => (door.FirstZoneId == zoneId || door.SecondZoneId == zoneId) &&
                door.Threshold == Threshold && door.InsideApproach == InsideApproach && door.OutsideApproach == OutsideApproach);
    }

    internal void AddHash(ref KsProcgenStableHash hash)
    {
        hash.AddString("ks-utility-room-v1"); hash.AddString(ThemeId);
        foreach (var cell in new[] { Minimum, Size, Threshold, InsideApproach, OutsideApproach })
        { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
        if (ReusedHullCells.Count == 0) return;
        hash.AddString("generated-hull-reuse-v1"); hash.AddInt(ReusedHullCells.Count);
        foreach (var cell in ReusedHullCells) { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
    }
}
