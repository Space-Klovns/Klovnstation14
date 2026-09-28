using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenPureFillStatus : byte
{
    Proposed,
    InvalidInput,
    BudgetExceeded,
}

public enum KsProcgenZoneKind : byte
{
    Passage,
    RoomProposal,
}

/// <summary>
/// A connected logical zone. Room proposals have no physical partition, door, or hull yet.
/// </summary>
public sealed record KsProcgenZone(string Id, KsProcgenZoneKind Kind, IReadOnlyList<Vector2i> Cells);

public sealed class KsProcgenPureFillResult
{
    public KsProcgenPureFillStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenZone> Zones { get; init; } = [];
    public IReadOnlyList<(string First, string Second)> CardinalInterfaces { get; init; } = [];
    public int ProceduralCells { get; init; }
    public int TinyPassageComponents { get; init; }
}

/// <summary>
/// Exact-cell, bounded region proposals after packing. Reserved routes are never room cells.
/// This stage does not place floors, walls, doors, lights, or furniture.
/// </summary>
public static class KsProcgenPureFillPlanner
{
    private static readonly Vector2i[] Cardinal = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public static KsProcgenPureFillResult Plan(
        KsProcgenNormalizedShape shape,
        KsProcgenPackingResult packing,
        int seed,
        int preferredMaxRoomCells = 24,
        int minimumRoomCells = 4,
        int maxProceduralCells = 65_536)
    {
        if (shape == null || packing == null || packing.Status != KsProcgenPackingStatus.GeometryReady ||
            packing.CellClaims.Count != shape.TargetCells.Count ||
            preferredMaxRoomCells < minimumRoomCells || minimumRoomCells < 1 ||
            preferredMaxRoomCells > 65_536 || maxProceduralCells <= 0 || maxProceduralCells > 65_536 ||
            packing.ResidualRouting is { Status: not KsProcgenResidualStatus.PreliminaryReady })
            return Failure(KsProcgenPureFillStatus.InvalidInput, "InvalidPureFillInput");

        var claims = new HashSet<Vector2i>();
        var procedural = new HashSet<Vector2i>();
        foreach (var (cell, claim) in packing.CellClaims)
        {
            if (!shape.ContainsTarget(cell) || !claims.Add(cell))
                return Failure(KsProcgenPureFillStatus.InvalidInput, "InvalidPureFillClaims");
            if (claim.Disposition == KsProcgenCellDisposition.ProceduralFloor)
            {
                if (shape.ContainsPreserved(cell))
                    return Failure(KsProcgenPureFillStatus.InvalidInput, "InvalidPureFillClaims");
                procedural.Add(cell);
            }
        }

        if (procedural.Count > maxProceduralCells)
            return Failure(KsProcgenPureFillStatus.BudgetExceeded, "PureFillCellBudget");

        var reserved = packing.ResidualRouting?.ReservedPassageCells ?? new HashSet<Vector2i>();
        if (reserved.Any(cell => !procedural.Contains(cell)))
            return Failure(KsProcgenPureFillStatus.InvalidInput, "InvalidPureFillRoute");

        var zones = new List<KsProcgenZone>();
        var tinyComponents = 0;
        var nextZone = 0;
        foreach (var component in KsProcgenGeometry.ConnectedComponents(procedural))
        {
            var componentSet = new HashSet<Vector2i>(component);
            var tiny = component.Count < minimumRoomCells || !HasTwoByTwo(componentSet);
            if (tiny)
            {
                zones.Add(new KsProcgenZone($"zone-{nextZone++}", KsProcgenZoneKind.Passage, component));
                tinyComponents++;
                continue;
            }

            var passage = new HashSet<Vector2i>(component.Where(reserved.Contains));
            foreach (var passageComponent in KsProcgenGeometry.ConnectedComponents(passage))
                zones.Add(new KsProcgenZone($"zone-{nextZone++}", KsProcgenZoneKind.Passage, passageComponent));

            var unassigned = new HashSet<Vector2i>(component.Where(cell => !passage.Contains(cell)));
            var random = KsProcgenRandom.ForStage(seed, "pure-fill-partition", $"{component[0].X},{component[0].Y}");
            var nextStart = 0;
            while (unassigned.Count > 0)
            {
                while (!unassigned.Contains(component[nextStart]))
                    nextStart++;
                var start = component[nextStart];
                var maxSize = Math.Min(preferredMaxRoomCells,
                    Math.Max(minimumRoomCells, preferredMaxRoomCells / 2 + random.NextInt(preferredMaxRoomCells / 2 + 1)));
                var queue = new Queue<Vector2i>();
                var cells = new List<Vector2i>();
                unassigned.Remove(start);
                queue.Enqueue(start);
                while (queue.TryDequeue(out var cell))
                {
                    cells.Add(cell);
                    if (cells.Count >= maxSize)
                        break;
                    foreach (var offset in Cardinal)
                    {
                        var neighbor = cell + offset;
                        if (unassigned.Remove(neighbor))
                            queue.Enqueue(neighbor);
                    }
                }

                // Frontier cells already removed from the global set still need their own zone.
                while (queue.TryDequeue(out var deferred))
                    unassigned.Add(deferred);
                zones.Add(new KsProcgenZone($"zone-{nextZone++}", KsProcgenZoneKind.RoomProposal,
                    KsProcgenGeometry.SortCells(cells)));
            }
        }

        var owners = new Dictionary<Vector2i, string>();
        foreach (var zone in zones)
        foreach (var cell in zone.Cells)
            owners.Add(cell, zone.Id);

        var interfaces = new HashSet<(string First, string Second)>();
        foreach (var (cell, owner) in owners)
        foreach (var offset in Cardinal)
        {
            if (!owners.TryGetValue(cell + offset, out var other) || owner == other)
                continue;
            var pair = string.CompareOrdinal(owner, other) < 0 ? (owner, other) : (other, owner);
            interfaces.Add(pair);
        }

        return new KsProcgenPureFillResult
        {
            Status = KsProcgenPureFillStatus.Proposed,
            Zones = zones,
            CardinalInterfaces = interfaces.OrderBy(pair => pair.First, StringComparer.Ordinal)
                .ThenBy(pair => pair.Second, StringComparer.Ordinal).ToArray(),
            ProceduralCells = procedural.Count,
            TinyPassageComponents = tinyComponents,
        };
    }

    private static bool HasTwoByTwo(IReadOnlySet<Vector2i> cells) =>
        cells.Any(cell => cells.Contains(cell + new Vector2i(1, 0)) &&
                          cells.Contains(cell + new Vector2i(0, 1)) &&
                          cells.Contains(cell + new Vector2i(1, 1)));

    private static KsProcgenPureFillResult Failure(KsProcgenPureFillStatus status, string code) => new()
    {
        Status = status,
        Issue = new KsProcgenIssue(code, "Pure-fill partition input or work budget could not be satisfied."),
    };
}
