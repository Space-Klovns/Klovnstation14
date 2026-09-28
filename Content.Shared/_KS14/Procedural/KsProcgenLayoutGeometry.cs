using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public readonly record struct KsProcgenBoundaryEdge(Vector2i Cell, Vector2i OutwardNormal);

/// <summary>
/// One-cell entrance declared in room-local coordinates. Threshold occupies the room boundary.
/// </summary>
public sealed record KsProcgenRoomPort(string Id, Vector2i Threshold, Vector2i OutwardNormal);

/// <summary>
/// Geometry already inspected from a future room map adapter. No entities are copied by this type.
/// </summary>
public sealed class KsProcgenRoomShape
{
    public string Id { get; }
    public IReadOnlyList<Vector2i> Cells { get; }
    public IReadOnlyList<KsProcgenBoundaryEdge> ExteriorFacades { get; }
    public IReadOnlyList<KsProcgenRoomPort> Ports { get; }

    public KsProcgenRoomShape(
        string id,
        IEnumerable<Vector2i> cells,
        IEnumerable<KsProcgenBoundaryEdge>? exteriorFacades = null,
        IEnumerable<KsProcgenRoomPort>? ports = null)
    {
        Id = id;
        Cells = KsProcgenGeometry.SortCells(cells.Distinct());
        ExteriorFacades = Array.AsReadOnly((exteriorFacades ?? []).Distinct().ToArray());
        Ports = Array.AsReadOnly((ports ?? []).ToArray());
    }
}

public sealed class KsProcgenLayoutOption
{
    public string Id { get; }
    public IReadOnlyList<Vector2i> ReservationCells { get; }
    public IReadOnlyList<Vector2i> GenerateCells { get; }
    public IReadOnlyList<KsProcgenRoomShape> Rooms { get; }

    public KsProcgenLayoutOption(
        string id,
        IEnumerable<Vector2i> reservationCells,
        IEnumerable<KsProcgenRoomShape> rooms,
        IEnumerable<Vector2i>? generateCells = null)
    {
        Id = id;
        ReservationCells = KsProcgenGeometry.SortCells(reservationCells.Distinct());
        GenerateCells = KsProcgenGeometry.SortCells((generateCells ?? []).Distinct());
        Rooms = Array.AsReadOnly(rooms.ToArray());
    }
}

public sealed class KsProcgenLayoutFamily
{
    public string Id { get; }
    public IReadOnlyList<KsProcgenLayoutOption> Options { get; }
    public IReadOnlyList<int> AllowedQuarterTurns { get; }

    public KsProcgenLayoutFamily(string id, IEnumerable<KsProcgenLayoutOption> options, IEnumerable<int>? allowedQuarterTurns = null)
    {
        Id = id;
        Options = Array.AsReadOnly(options.ToArray());
        AllowedQuarterTurns = Array.AsReadOnly((allowedQuarterTurns ?? [0]).Distinct().ToArray());
    }

    public bool TryValidate(out KsProcgenIssue? issue)
    {
        issue = null;
        if (string.IsNullOrWhiteSpace(Id) || Options.Count == 0 || AllowedQuarterTurns.Count == 0 ||
            AllowedQuarterTurns.Any(turn => turn < 0 || turn > 3))
        {
            issue = new KsProcgenIssue("InvalidLayoutFamily", "A family needs an ID, options, and supported quarter turns.");
            return false;
        }

        var optionIds = new HashSet<string>(StringComparer.Ordinal);
        var referenceReservation = new HashSet<Vector2i>(Options[0].ReservationCells);
        if (referenceReservation.Count == 0)
        {
            issue = new KsProcgenIssue("InvalidLayoutFamily", "The family reservation cannot be empty.");
            return false;
        }

        if (referenceReservation.Any(cell => cell.X < -1_000_000 || cell.X > 1_000_000 ||
                                             cell.Y < -1_000_000 || cell.Y > 1_000_000))
        {
            issue = new KsProcgenIssue("InvalidLayoutFamily", "Local cells exceed the supported coordinate range.");
            return false;
        }

        foreach (var option in Options)
        {
            if (string.IsNullOrWhiteSpace(option.Id) || !optionIds.Add(option.Id) ||
                !referenceReservation.SetEquals(option.ReservationCells))
            {
                issue = new KsProcgenIssue("InvalidLayoutFamily", "Options need distinct IDs and the same reservation mask.");
                return false;
            }

            var assignedCells = new HashSet<Vector2i>();
            var roomIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var room in option.Rooms)
            {
                if (string.IsNullOrWhiteSpace(room.Id) || !roomIds.Add(room.Id) || room.Cells.Count == 0)
                {
                    issue = new KsProcgenIssue("InvalidLayoutRoom", $"Option {option.Id} has an empty or duplicate room.");
                    return false;
                }

                var roomSet = new HashSet<Vector2i>(room.Cells);
                foreach (var cell in room.Cells)
                {
                    if (!referenceReservation.Contains(cell) || !assignedCells.Add(cell))
                    {
                        issue = new KsProcgenIssue("LayoutOverlap", $"Option {option.Id} has overlapping or unreserved room cells.");
                        return false;
                    }
                }

                foreach (var edge in room.ExteriorFacades)
                {
                    if (!roomSet.Contains(edge.Cell) ||
                        !IsCardinalUnit(edge.OutwardNormal) ||
                        roomSet.Contains(edge.Cell + edge.OutwardNormal))
                    {
                        issue = new KsProcgenIssue("InvalidFacade", $"Room {room.Id} declares a non-boundary exterior edge.");
                        return false;
                    }
                }

                var portIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var port in room.Ports)
                {
                    if (port == null || string.IsNullOrWhiteSpace(port.Id) || !portIds.Add(port.Id) ||
                        !IsCardinalUnit(port.OutwardNormal) ||
                        !roomSet.Contains(port.Threshold) ||
                        !roomSet.Contains(port.Threshold - port.OutwardNormal) ||
                        roomSet.Contains(port.Threshold + port.OutwardNormal))
                    {
                        issue = new KsProcgenIssue("InvalidRoomPort",
                            $"Room {room.Id} has a duplicate, noncardinal, or blocked port.");
                        return false;
                    }
                }
            }

            foreach (var cell in option.GenerateCells)
            {
                if (!referenceReservation.Contains(cell) || !assignedCells.Add(cell))
                {
                    issue = new KsProcgenIssue("LayoutOverlap", $"Option {option.Id} has overlapping or unreserved generated cells.");
                    return false;
                }
            }

            if (!assignedCells.SetEquals(referenceReservation))
            {
                issue = new KsProcgenIssue("LayoutGap", $"Option {option.Id} leaves undeclared reservation cells.");
                return false;
            }
        }

        return true;
    }

    private static bool IsCardinalUnit(Vector2i normal) =>
        (normal.X == 0 && (normal.Y == 1 || normal.Y == -1)) ||
        (normal.Y == 0 && (normal.X == 1 || normal.X == -1));
}

public sealed class KsProcgenPlacementCandidate
{
    public string FamilyId { get; }
    public string OptionId { get; }
    public Vector2i Origin { get; }
    public int QuarterTurns { get; }
    public IReadOnlyList<(Vector2i Cell, KsProcgenCellClaim Claim)> Claims { get; }
    public IReadOnlyList<KsProcgenPortGeometry> Ports { get; }
    public int MatchedExteriorEdges { get; }

    internal KsProcgenPlacementCandidate(
        string familyId,
        string optionId,
        Vector2i origin,
        int quarterTurns,
        IReadOnlyList<(Vector2i Cell, KsProcgenCellClaim Claim)> claims,
        IReadOnlyList<KsProcgenPortGeometry> ports,
        int matchedExteriorEdges)
    {
        FamilyId = familyId;
        OptionId = optionId;
        Origin = origin;
        QuarterTurns = quarterTurns;
        Claims = claims;
        Ports = ports;
        MatchedExteriorEdges = matchedExteriorEdges;
    }
}

/// <summary>
/// Geometric proposal and atomic claim handling. Routing, hull, and map inspection follow later.
/// </summary>
public static class KsProcgenLayoutGeometry
{
    public static IReadOnlyList<KsProcgenPlacementCandidate> FindCandidatesCovering(
        KsProcgenLayoutFamily family,
        KsProcgenNormalizedShape shape,
        Vector2i pivot,
        int maximumProbes,
        out bool enumerationComplete)
    {
        return FindCandidatesCovering(family, shape, pivot, maximumProbes,
            out enumerationComplete, out _);
    }

    public static IReadOnlyList<KsProcgenPlacementCandidate> FindCandidatesCovering(
        KsProcgenLayoutFamily family,
        KsProcgenNormalizedShape shape,
        Vector2i pivot,
        int maximumProbes,
        out bool enumerationComplete,
        out int probesUsed)
    {
        if (maximumProbes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumProbes));

        if (!family.TryValidate(out var issue))
            throw new ArgumentException(issue!.Message, nameof(family));

        enumerationComplete = true;
        probesUsed = 0;
        if (!shape.ContainsTarget(pivot) || !shape.CanWrite(pivot))
            return [];

        var candidates = new List<KsProcgenPlacementCandidate>();
        var seen = new HashSet<(string OptionId, int Turns, Vector2i Origin)>();
        foreach (var option in family.Options.OrderBy(option => option.Id, StringComparer.Ordinal))
        foreach (var turn in family.AllowedQuarterTurns.OrderBy(turn => turn))
        foreach (var localPivot in option.ReservationCells)
        {
            if (probesUsed >= maximumProbes)
            {
                enumerationComplete = false;
                return SortCandidates(candidates);
            }

            probesUsed++;

            // origin + R(localPivot) = pivot; the source anchor is the local (0, 0) cell.
            var rotated = KsProcgenGeometry.TransformCell(localPivot, default, default, turn);
            var originX = (long) pivot.X - rotated.X;
            var originY = (long) pivot.Y - rotated.Y;
            if (originX < int.MinValue || originX > int.MaxValue || originY < int.MinValue || originY > int.MaxValue)
                continue;

            var origin = new Vector2i((int) originX, (int) originY);
            if (!seen.Add((option.Id, turn, origin)))
                continue;

            if (TryBuildCandidate(family.Id, option, shape, origin, turn, out var candidate))
                candidates.Add(candidate!);
        }

        return SortCandidates(candidates);
    }

    private static IReadOnlyList<KsProcgenPlacementCandidate> SortCandidates(
        IEnumerable<KsProcgenPlacementCandidate> candidates) =>
        candidates.OrderByDescending(candidate => candidate.MatchedExteriorEdges)
            .ThenBy(candidate => candidate.OptionId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.QuarterTurns)
            .ThenBy(candidate => candidate.Origin.Y)
            .ThenBy(candidate => candidate.Origin.X)
            .ToArray();

    public static bool TryClaimCandidate(KsProcgenPlan plan, KsProcgenPlacementCandidate candidate)
    {
        var checkpoint = plan.Checkpoint();
        foreach (var (cell, claim) in candidate.Claims)
        {
            if (plan.TryClaim(cell, claim))
                continue;

            plan.Rollback(checkpoint);
            return false;
        }

        return true;
    }

    private static bool TryBuildCandidate(
        string familyId,
        KsProcgenLayoutOption option,
        KsProcgenNormalizedShape shape,
        Vector2i origin,
        int turn,
        out KsProcgenPlacementCandidate? candidate)
    {
        candidate = null;
        var claims = new List<(Vector2i Cell, KsProcgenCellClaim Claim)>();
        var ports = new List<KsProcgenPortGeometry>();
        var matchedEdges = new HashSet<KsProcgenBoundaryEdge>();

        foreach (var localCell in option.ReservationCells)
        {
            if (!TryTransform(localCell, origin, turn, out var cell) || !shape.ContainsTarget(cell) || !shape.CanWrite(cell))
                return false;
        }

        foreach (var room in option.Rooms)
        {
            var ownerId = $"{familyId}/{option.Id}@{origin.X},{origin.Y}:{turn}/{room.Id}";
            foreach (var localCell in room.Cells)
            {
                if (!TryTransform(localCell, origin, turn, out var cell))
                    return false;

                claims.Add((cell, new KsProcgenCellClaim(ownerId, KsProcgenCellDisposition.Prefab)));
            }

            foreach (var edge in room.ExteriorFacades)
            {
                if (!TryTransform(edge.Cell, origin, turn, out var cell))
                    return false;

                var normal = KsProcgenGeometry.TransformCell(edge.OutwardNormal, default, default, turn);
                if (!shape.ContainsTarget(cell + normal))
                    matchedEdges.Add(new KsProcgenBoundaryEdge(cell, normal));
            }

            foreach (var port in room.Ports)
            {
                if (!TryTransform(port.Threshold, origin, turn, out var threshold))
                    return false;

                var normal = KsProcgenGeometry.TransformCell(port.OutwardNormal, default, default, turn);
                var inside = threshold - normal;
                var outside = threshold + normal;
                if (!shape.ContainsTarget(outside) || shape.ContainsVoid(outside))
                    return false;

                ports.Add(new KsProcgenPortGeometry($"{ownerId}/{port.Id}", ownerId,
                    threshold, normal, inside, outside));
            }
        }

        var fillerId = $"{familyId}/{option.Id}@{origin.X},{origin.Y}:{turn}/Generate";
        foreach (var localCell in option.GenerateCells)
        {
            if (!TryTransform(localCell, origin, turn, out var cell))
                return false;

            claims.Add((cell, new KsProcgenCellClaim(fillerId, KsProcgenCellDisposition.ProceduralFloor)));
        }

        candidate = new KsProcgenPlacementCandidate(familyId, option.Id, origin, turn,
            claims.OrderBy(entry => entry.Cell.Y).ThenBy(entry => entry.Cell.X).ToArray(),
            ports.OrderBy(port => port.Id, StringComparer.Ordinal).ToArray(),
            matchedEdges.Count);
        return true;
    }

    private static bool TryTransform(Vector2i cell, Vector2i origin, int turn, out Vector2i transformed)
    {
        try
        {
            transformed = KsProcgenGeometry.TransformCell(cell, default, origin, turn);
            return true;
        }
        catch (OverflowException)
        {
            transformed = default;
            return false;
        }
    }
}
