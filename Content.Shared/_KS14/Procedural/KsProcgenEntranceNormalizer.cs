using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>Declared host-grid geometry; live doors and exterior landings require later inspection.</summary>
public sealed record KsProcgenEntranceMarker(string PortId, string Channel, IReadOnlyList<Vector2i> ThresholdCells,
    Vector2i InwardNormal, string? BlobId = null, bool OptionalSealable = false);

public sealed record KsProcgenAreaEntrance(string BlobId, string PortId, string Channel,
    IReadOnlyList<Vector2i> ThresholdCells, IReadOnlyList<Vector2i> InsideApproach,
    IReadOnlyList<Vector2i> OutsideApproach, Vector2i InwardNormal, bool OptionalSealable);

public sealed class KsProcgenEntranceNormalization
{
    public KsProcgenStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenAreaEntrance> Entrances { get; init; } = [];
}

/// <summary>Resolve complete directed entrance spans without granting any threshold write permission.</summary>
public static class KsProcgenEntranceNormalizer
{
    public static KsProcgenEntranceNormalization Normalize(IReadOnlyList<KsProcgenAreaBlob> blobs,
        IReadOnlyList<KsProcgenEntranceMarker> markers, int maximumEntrances = 1024)
    {
        KsProcgenEntranceNormalization Fail(string code, KsProcgenEntranceMarker? marker = null) => new()
        {
            Status = KsProcgenStatus.InvalidInput,
            Issue = new(code, marker == null ? "Invalid entrance normalization input." :
                $"Entrance '{marker.PortId}' in channel '{marker.Channel}', blob '{marker.BlobId ?? "inferred"}', " +
                $"threshold '{string.Join(",", marker.ThresholdCells.Take(4))}' could not be normalized."),
        };
        if (maximumEntrances is < 0 or > 1024 || blobs.Count > 4096)
            return Fail("InvalidEntranceInput");
        if (markers.Count > maximumEntrances)
            return new() { Status = KsProcgenStatus.BudgetExceeded, Issue = new("EntranceMarkerBudget", "Too many entrance markers.") };
        var owners = new Dictionary<(string Channel, Vector2i Cell), KsProcgenAreaBlob>();
        var definitions = new Dictionary<string, KsProcgenAreaBlob>(StringComparer.Ordinal);
        var expandedCells = 0;
        foreach (var blob in blobs)
        {
            if (!KsProcgenAreaNormalizer.ValidId(blob.Id) || !KsProcgenAreaNormalizer.ValidId(blob.Channel) ||
                !definitions.TryAdd(blob.Id, blob) || blob.Cells.Count == 0 || blob.Cells.Count > 65_536 - expandedCells)
                return Fail("InvalidEntranceDomain");
            expandedCells += blob.Cells.Count;
            foreach (var cell in blob.Cells)
                if (!ValidCell(cell) || !owners.TryAdd((blob.Channel, cell), blob))
                    return Fail("InvalidEntranceDomain");
        }
        var labels = new HashSet<(string Blob, string Port)>();
        var thresholds = new HashSet<(string Blob, Vector2i Cell)>();
        var entrances = new List<KsProcgenAreaEntrance>();
        foreach (var marker in markers)
        {
            if (!KsProcgenAreaNormalizer.ValidId(marker.PortId) || !KsProcgenAreaNormalizer.ValidId(marker.Channel) ||
                marker.BlobId != null && !KsProcgenAreaNormalizer.ValidId(marker.BlobId) ||
                Math.Abs((long) marker.InwardNormal.X) + Math.Abs((long) marker.InwardNormal.Y) != 1 ||
                marker.ThresholdCells.Count is < 1 or > 64 || marker.ThresholdCells.Any(cell => !ValidCell(cell)))
                return Fail("InvalidEntranceMarker", marker: marker);
            var ordered = KsProcgenGeometry.SortCells(marker.ThresholdCells);
            for (var index = 1; index < ordered.Count; index++)
            {
                var delta = ordered[index] - ordered[index - 1];
                if (Math.Abs(delta.X) + Math.Abs(delta.Y) != 1 ||
                    delta.X * marker.InwardNormal.X + delta.Y * marker.InwardNormal.Y != 0)
                    return Fail("InvalidEntranceSpan", marker: marker);
            }
            var inside = ordered.Select(cell => cell + marker.InwardNormal).ToArray();
            var outside = ordered.Select(cell => cell - marker.InwardNormal).ToArray();
            if (inside.Any(cell => !ValidCell(cell)) || outside.Any(cell => !ValidCell(cell)))
                return Fail("InvalidEntranceApproach", marker: marker);
            KsProcgenAreaBlob? owner = null;
            foreach (var cell in inside)
            {
                if (!owners.TryGetValue((marker.Channel, cell), out var cellOwner))
                    return Fail("OrphanEntranceMarker", marker: marker);
                if (owner != null && owner.Id != cellOwner.Id)
                    return Fail("AmbiguousEntranceOwner", marker: marker);
                owner = cellOwner;
            }
            if (marker.BlobId != null && marker.BlobId != owner!.Id)
                return Fail("EntranceOwnerMismatch", marker: marker);
            var domain = owner!;
            if (outside.Any(cell => owners.TryGetValue((marker.Channel, cell), out var exteriorOwner) && exteriorOwner.Id == domain.Id))
                return Fail("EntranceNotAtBoundary", marker: marker);
            if (!labels.Add((domain.Id, marker.PortId)))
                return Fail("DuplicateEntrancePort", marker: marker);
            if (ordered.Any(cell => !thresholds.Add((domain.Id, cell))))
                return Fail("OverlappingEntranceThresholds", marker: marker);
            entrances.Add(new(domain.Id, marker.PortId, marker.Channel, ordered.ToList().AsReadOnly(),
                Array.AsReadOnly(inside), Array.AsReadOnly(outside), marker.InwardNormal, marker.OptionalSealable));
        }
        return new()
        {
            Status = entrances.Count == 0 ? KsProcgenStatus.NoOp : KsProcgenStatus.Success,
            Entrances = entrances.OrderBy(entrance => entrance.BlobId, StringComparer.Ordinal)
                .ThenBy(entrance => entrance.PortId, StringComparer.Ordinal).ToList().AsReadOnly(),
        };
    }

    private static bool ValidCell(Vector2i cell) => Math.Abs((long) cell.X) <= 1_000_000 && Math.Abs((long) cell.Y) <= 1_000_000;
}
