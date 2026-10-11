using System.Collections.Frozen;
using System.Globalization;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>Caller-declared face context, never an engine gas/collision witness.</summary>
public sealed record KsProcgenExteriorWindowFact(Vector2i Cell, Vector2i OutwardNormal,
    bool FacesExterior, bool SupportsAirtightWindow, bool IsExistingWindow = false,
    bool RequiredStructure = false, bool WindowsDisabled = false);

public sealed class KsProcgenGroupWindowBoundaryResult
{
    public KsProcgenEntranceSceneStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenGroupHullBoundaryResult? HullBoundary { get; init; }
    public IReadOnlyList<KsProcgenWindowBoundaryCell> Cells { get; init; } = [];
    public IReadOnlyList<KsProcgenExteriorWindowFact> Facts { get; init; } = [];
    public IReadOnlySet<Vector2i> UnclassifiedStructuralCells { get; init; } = FrozenSet<Vector2i>.Empty;
    public int ExpandedCells { get; init; }
    public ulong ClassificationHash { get; init; }
    public bool ExteriorScopeComplete => Status == KsProcgenEntranceSceneStatus.Candidate && HullBoundary != null &&
        UnclassifiedStructuralCells.Count == 0 && HullBoundary.Edges.All(edge => edge.Kind is
            KsProcgenOwnedBoundaryKind.ProposedTargetWall or KsProcgenOwnedBoundaryKind.WritableEnvelope or
            KsProcgenOwnedBoundaryKind.PreservedUnknown or KsProcgenOwnedBoundaryKind.OperationalEntrance or KsProcgenOwnedBoundaryKind.SealedEntrance);
    public bool EngineExteriorVerified => false;
}

/// <summary>
/// Revalidate owned hull geometry, then classify explicit one-tile structural faces. Missing face
/// context remains unclassified. Read-only walls may contribute fixed window counts but never writes.
/// Known floor, host approaches, structure and authorized envelope cannot be declared exterior probes.
/// </summary>
public static class KsProcgenGroupWindowBoundaryAnalyzer
{
    public static KsProcgenGroupWindowBoundaryResult Analyze(KsProcgenRequest request,
        KsProcgenGroupPartitionResult partition, IReadOnlyList<KsProcgenExteriorWindowFact> facts,
        int maximumExpandedCells = 262_144)
    {
        var expanded = 0;
        KsProcgenGroupWindowBoundaryResult Fail(KsProcgenEntranceSceneStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Owned window face context did not validate."), ExpandedCells = expanded,
        };
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out var issue))
            return new() { Status = KsProcgenEntranceSceneStatus.InvalidInput, Issue = issue };
        if (facts == null || facts.Count > 16_384 || maximumExpandedCells is < 0 or > 1_000_000 ||
            facts.Any(fact => fact == null || Math.Abs((long) fact.OutwardNormal.X) + Math.Abs((long) fact.OutwardNormal.Y) != 1 ||
                Math.Abs((long) fact.Cell.X) > 1_000_000 || Math.Abs((long) fact.Cell.Y) > 1_000_000) ||
            facts.Select(fact => fact.Cell).Distinct().Count() != facts.Count)
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "InvalidOwnedWindowFacts");
        var hull = KsProcgenGroupHullBoundaryAnalyzer.Analyze(request, partition, maximumExpandedCells: maximumExpandedCells);
        expanded += hull.ExpandedCells;
        if (hull.Status != KsProcgenEntranceSceneStatus.Candidate)
            return Fail(hull.Status, hull.Issue!.Code);
        var edgesByCell = hull.Edges.GroupBy(edge => edge.BoundaryCell).ToDictionary(group => group.Key, group => group.ToArray());
        var factByCell = facts.ToDictionary(fact => fact.Cell);
        var structural = edgesByCell.Where(entry => entry.Value.All(edge => IsStructural(edge, shape!)))
            .Select(entry => entry.Key).ToHashSet();
        foreach (var fact in facts)
        {
            // Bounds checked above make the single-cell probes safe.
            var inside = fact.Cell - fact.OutwardNormal;
            var outside = fact.Cell + fact.OutwardNormal;
            if (!structural.Contains(fact.Cell) || !edgesByCell[fact.Cell].Any(edge => edge.FloorCell == inside) ||
                fact.IsExistingWindow && (shape!.CanWrite(fact.Cell) || !fact.FacesExterior || !fact.SupportsAirtightWindow) ||
                fact.FacesExterior && (shape!.ContainsTarget(outside) || shape.ContainsEnvelope(outside) || shape.ContainsPreserved(outside) ||
                    partition.Scene!.CleanHostCells.Contains(outside) || partition.Scene.BlockedThresholdCells.Contains(outside)))
                return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "ContradictoryOwnedWindowFace");
        }
        var cells = new List<KsProcgenWindowBoundaryCell>();
        foreach (var cell in KsProcgenGeometry.SortCells(structural))
        {
            if (expanded >= maximumExpandedCells)
                return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "OwnedWindowClassificationBudget");
            expanded++;
            factByCell.TryGetValue(cell, out var fact);
            var corner = edgesByCell[cell].Select(edge => cell - edge.FloorCell).Distinct().Count() > 1;
            cells.Add(new(cell, true, fact?.FacesExterior ?? false, fact?.SupportsAirtightWindow ?? false,
                Fixed: !shape!.CanWrite(cell), IsWindow: fact?.IsExistingWindow ?? false, IsCorner: corner,
                RequiredStructure: fact?.RequiredStructure ?? false, WindowsDisabled: fact?.WindowsDisabled ?? false));
        }
        var orderedFacts = facts.OrderBy(fact => fact.Cell.Y).ThenBy(fact => fact.Cell.X).ToReadOnlyList();
        var unclassified = structural.Where(cell => !factByCell.ContainsKey(cell)).ToFrozenSet();
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-group-window-boundary-v1");
        hash.AddString(hull.BoundaryHash.ToString(CultureInfo.InvariantCulture));
        hash.AddInt(orderedFacts.Count);
        foreach (var fact in orderedFacts)
        {
            hash.AddInt(fact.Cell.X); hash.AddInt(fact.Cell.Y);
            hash.AddInt(fact.OutwardNormal.X); hash.AddInt(fact.OutwardNormal.Y);
            hash.AddInt(fact.FacesExterior ? 1 : 0); hash.AddInt(fact.SupportsAirtightWindow ? 1 : 0);
            hash.AddInt(fact.IsExistingWindow ? 1 : 0); hash.AddInt(fact.RequiredStructure ? 1 : 0);
            hash.AddInt(fact.WindowsDisabled ? 1 : 0);
        }
        return new()
        {
            Status = KsProcgenEntranceSceneStatus.Candidate, HullBoundary = hull, Cells = KsProcgenReadOnly.Copy(cells),
            Facts = orderedFacts, UnclassifiedStructuralCells = unclassified, ExpandedCells = expanded, ClassificationHash = hash.Value,
        };
    }

    private static bool IsStructural(KsProcgenOwnedHullEdge edge, KsProcgenNormalizedShape shape) =>
        edge.Kind is KsProcgenOwnedBoundaryKind.ProposedTargetWall or KsProcgenOwnedBoundaryKind.WritableEnvelope ||
        edge.Kind == KsProcgenOwnedBoundaryKind.PreservedUnknown && shape.ContainsPreserved(edge.BoundaryCell) &&
        !shape.CanWrite(edge.BoundaryCell);
}
