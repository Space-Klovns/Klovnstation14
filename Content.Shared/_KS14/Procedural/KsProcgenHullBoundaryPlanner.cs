using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenHullBoundaryStatus : byte
{
    Classified,
    UnresolvedBoundary,
    InvalidInput,
}

public enum KsProcgenBoundaryKind : byte
{
    WritableEnvelope,
    PreservedUnknown,
    ExplicitVoid,
    MissingFootprintHull,
    UnknownHostContext,
}

public sealed record KsProcgenHullExposure(Vector2i InteriorCell, Vector2i BoundaryCell,
    KsProcgenBoundaryKind Kind);

/// <summary>
/// A geometric inventory only. It does not inspect wall prototypes, doors, gas blocking, or host hull.
/// </summary>
public sealed class KsProcgenHullBoundaryResult
{
    public KsProcgenHullBoundaryStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenHullExposure> Edges { get; init; } = [];
    public IReadOnlyList<Vector2i> RequiredEnvelopeCells { get; init; } = [];
    public bool GasClosureVerified => false;
}

public static class KsProcgenHullBoundaryPlanner
{
    private static readonly Vector2i[] Cardinal = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public static KsProcgenHullBoundaryResult Classify(
        KsProcgenNormalizedShape shape,
        KsProcgenGeometryMode mode,
        IReadOnlyCollection<Vector2i> generatedFloor,
        IReadOnlyCollection<Vector2i> knownInteriorWalls)
    {
        if (shape == null || generatedFloor == null || knownInteriorWalls == null ||
            !Enum.IsDefined(mode) || generatedFloor.Count > 65_536 || knownInteriorWalls.Count > 65_536)
            return Invalid("InvalidHullBoundaryInput");

        var floor = new HashSet<Vector2i>(generatedFloor);
        var interiorWalls = new HashSet<Vector2i>(knownInteriorWalls);
        if (floor.Count != generatedFloor.Count || interiorWalls.Count != knownInteriorWalls.Count ||
            floor.Overlaps(interiorWalls) || floor.Any(cell => !shape.ContainsTarget(cell) ||
                shape.ContainsPreserved(cell)) || interiorWalls.Any(cell => !shape.ContainsTarget(cell) ||
                shape.ContainsPreserved(cell)))
            return Invalid("InvalidHullBoundaryCoverage");

        var edges = new List<KsProcgenHullExposure>();
        var envelopeCells = new HashSet<Vector2i>();
        foreach (var cell in KsProcgenGeometry.SortCells(floor))
        foreach (var offset in Cardinal)
        {
            var neighbor = cell + offset;
            if (floor.Contains(neighbor) || interiorWalls.Contains(neighbor))
                continue;

            KsProcgenBoundaryKind kind;
            if (shape.ContainsEnvelope(neighbor))
            {
                kind = KsProcgenBoundaryKind.WritableEnvelope;
                envelopeCells.Add(neighbor);
            }
            else if (shape.ContainsPreserved(neighbor) || shape.ContainsTarget(neighbor))
                kind = KsProcgenBoundaryKind.PreservedUnknown;
            else if (shape.ContainsVoid(neighbor))
                kind = KsProcgenBoundaryKind.ExplicitVoid;
            else
                kind = mode == KsProcgenGeometryMode.InteriorFill
                    ? KsProcgenBoundaryKind.UnknownHostContext
                    : KsProcgenBoundaryKind.MissingFootprintHull;

            edges.Add(new KsProcgenHullExposure(cell, neighbor, kind));
        }

        var unresolved = edges.Any(edge => edge.Kind != KsProcgenBoundaryKind.WritableEnvelope);
        return new KsProcgenHullBoundaryResult
        {
            Status = unresolved ? KsProcgenHullBoundaryStatus.UnresolvedBoundary :
                KsProcgenHullBoundaryStatus.Classified,
            Issue = unresolved ? new KsProcgenIssue("UnresolvedHullBoundary",
                "At least one generated floor edge lacks a writable envelope cell; host or hull inspection is required.")
                : null,
            Edges = edges.OrderBy(edge => edge.InteriorCell.Y).ThenBy(edge => edge.InteriorCell.X)
                .ThenBy(edge => edge.BoundaryCell.Y).ThenBy(edge => edge.BoundaryCell.X).ToArray(),
            RequiredEnvelopeCells = KsProcgenGeometry.SortCells(envelopeCells),
        };
    }

    private static KsProcgenHullBoundaryResult Invalid(string code) => new()
    {
        Status = KsProcgenHullBoundaryStatus.InvalidInput,
        Issue = new KsProcgenIssue(code, "Generated floor and known walls must fit the normalized target."),
    };
}
