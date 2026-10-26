using System.Collections.Frozen;
using System.Globalization;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenOwnedBoundaryKind : byte
{
    ProposedTargetWall,
    WritableEnvelope,
    OperationalEntrance,
    SealedEntrance,
    CleanHostOpening,
    ExistingBlockedUnknown,
    PreservedUnknown,
    ExplicitVoid,
    MissingFootprintHull,
    UnknownHostContext,
}

public sealed record KsProcgenOwnedHullEdge(Vector2i FloorCell, Vector2i BoundaryCell,
    int Component, IReadOnlyList<string> GroupIds, KsProcgenOwnedBoundaryKind Kind, string? PortId);

/// <summary>A complete geometric inventory, including unresolved edges. No pressure claim is made.</summary>
public sealed class KsProcgenGroupHullBoundaryResult
{
    public KsProcgenEntranceSceneStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenOwnedHullEdge> Edges { get; init; } = [];
    public IReadOnlySet<Vector2i> RequiredEnvelopeCells { get; init; } = FrozenSet<Vector2i>.Empty;
    public IReadOnlySet<Vector2i> ReservedEnvelopeCells { get; init; } = FrozenSet<Vector2i>.Empty;
    public int ExpandedCells { get; init; }
    public ulong BoundaryHash { get; init; }
    public bool HasUnresolvedBoundary => Status != KsProcgenEntranceSceneStatus.Candidate || Edges.Any(edge => edge.Kind is not
        (KsProcgenOwnedBoundaryKind.ProposedTargetWall or KsProcgenOwnedBoundaryKind.WritableEnvelope));
    public bool GasClosureVerified => false;
    public bool EngineBoundaryVerified => false;
}

/// <summary>
/// Audit all cardinal edges of an owned partition, including read-only floor. Entrance spans,
/// known clean host passages and blocked threshold context reserve envelope cells before any
/// wall claims. A movement blocker, sealed entrance or proposed wall is never a gas-seal witness.
/// </summary>
public static class KsProcgenGroupHullBoundaryAnalyzer
{
    private static readonly Vector2i[] Cardinal = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public static KsProcgenGroupHullBoundaryResult Analyze(KsProcgenRequest request,
        KsProcgenGroupPartitionResult partition, int maximumExpandedCells = 262_144)
    {
        var expanded = 0;
        KsProcgenGroupHullBoundaryResult Fail(KsProcgenEntranceSceneStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Owned hull boundary inventory did not validate."), ExpandedCells = expanded,
        };
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out var issue))
            return new() { Status = KsProcgenEntranceSceneStatus.InvalidInput, Issue = issue };
        if (partition == null || partition.Status != KsProcgenEntranceSceneStatus.Candidate || partition.Scene == null ||
            partition.Analysis?.Status != KsProcgenEntranceSceneStatus.Candidate || partition.PartitionHash == 0 ||
            shape!.EntranceContract == null || shape.TargetCells.Count > 4_096 || shape.EnvelopeCells.Count > 16_384 ||
            partition.FloorCells.Count > 4_096 || partition.WallCells.Count > 4_096 ||
            maximumExpandedCells is < 0 or > 1_000_000)
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "InvalidGroupHullBoundaryInput");
        var analysis = KsProcgenEntranceSceneAnalyzer.AnalyzeNormalized(shape, request.ConnectivityPolicy, request.RootCells,
            partition.Analysis.ConfigurationId!, partition.Scene, Math.Min(131_072, maximumExpandedCells));
        expanded += analysis.ExpandedCells;
        if (analysis.Status != KsProcgenEntranceSceneStatus.Candidate)
            return Fail(analysis.Status, analysis.Issue!.Code);
        var floor = partition.FloorCells.ToHashSet();
        var walls = partition.WallCells.ToHashSet();
        if (analysis.SceneHash != partition.Analysis.SceneHash || floor.Count != partition.FloorCells.Count ||
            !floor.SetEquals(partition.Scene.CleanDomainCells) || walls.Count != partition.WallCells.Count ||
            !walls.SetEquals(shape.TargetCells.Where(cell => shape.CanWrite(cell) && !floor.Contains(cell))))
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "StaleGroupHullBoundarySource");
        var configuration = shape.EntranceContract.Configurations.Single(candidate => candidate.Id == analysis.ConfigurationId);
        var sealedPorts = configuration.SealedPorts.ToHashSet(StringComparer.Ordinal);
        var operationalEdges = new Dictionary<(Vector2i Floor, Vector2i Boundary), string>();
        var sealedThresholds = new Dictionary<Vector2i, string>();
        var reserved = partition.Scene.CleanHostCells.Concat(partition.Scene.BlockedThresholdCells)
            .Concat(shape.PreservedCells).ToHashSet();
        foreach (var entrance in shape.Entrances)
        {
            reserved.UnionWith(entrance.ThresholdCells);
            reserved.UnionWith(entrance.OutsideApproach);
            reserved.UnionWith(entrance.InsideApproach);
            foreach (var threshold in entrance.ThresholdCells)
            {
                if (sealedPorts.Contains(entrance.PortId))
                {
                    sealedThresholds.Add(threshold, entrance.PortId);
                    continue;
                }
                var inside = threshold + entrance.InwardNormal;
                var outside = threshold - entrance.InwardNormal;
                if (floor.Contains(inside) && !floor.Contains(threshold))
                    operationalEdges.Add((inside, threshold), entrance.PortId);
                if (floor.Contains(threshold) && !floor.Contains(outside))
                    operationalEdges.Add((threshold, outside), entrance.PortId);
            }
        }
        var edges = new List<KsProcgenOwnedHullEdge>();
        var claims = new HashSet<Vector2i>();
        foreach (var component in analysis.Components.OrderBy(component => component.Component))
        foreach (var cell in component.Cells)
        {
            // One expansion inventories all four neighbors. Normalization bounds arithmetic.
            if (expanded >= maximumExpandedCells)
                return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "GroupHullBoundaryExpansionBudget");
            expanded++;
            foreach (var offset in Cardinal)
            {
                var neighbor = cell + offset;
                if (floor.Contains(neighbor))
                    continue;
                string? portId = null;
                KsProcgenOwnedBoundaryKind kind;
                if (operationalEdges.TryGetValue((cell, neighbor), out portId))
                    kind = KsProcgenOwnedBoundaryKind.OperationalEntrance;
                else if (sealedThresholds.TryGetValue(neighbor, out portId))
                    kind = KsProcgenOwnedBoundaryKind.SealedEntrance;
                else if (partition.Scene.CleanHostCells.Contains(neighbor))
                    kind = KsProcgenOwnedBoundaryKind.CleanHostOpening;
                else if (partition.Scene.BlockedThresholdCells.Contains(neighbor))
                    kind = KsProcgenOwnedBoundaryKind.ExistingBlockedUnknown;
                else if (walls.Contains(neighbor))
                    kind = KsProcgenOwnedBoundaryKind.ProposedTargetWall;
                else if (shape.ContainsEnvelope(neighbor) && shape.CanWrite(neighbor) && !reserved.Contains(neighbor))
                {
                    kind = KsProcgenOwnedBoundaryKind.WritableEnvelope;
                    claims.Add(neighbor);
                }
                else if (shape.ContainsVoid(neighbor))
                    kind = KsProcgenOwnedBoundaryKind.ExplicitVoid;
                else if (shape.ContainsPreserved(neighbor) || shape.ContainsTarget(neighbor) || reserved.Contains(neighbor))
                    kind = KsProcgenOwnedBoundaryKind.PreservedUnknown;
                else
                    kind = request.GeometryMode == KsProcgenGeometryMode.InteriorFill
                        ? KsProcgenOwnedBoundaryKind.UnknownHostContext : KsProcgenOwnedBoundaryKind.MissingFootprintHull;
                edges.Add(new(cell, neighbor, component.Component, KsProcgenReadOnly.Copy(component.GroupIds), kind, portId));
            }
        }
        // Utility enclosures also own their diagonal structural corners. Those cells have no
        // cardinal exposure edge and must not become fictitious movement or atmosphere edges.
        var utilityHull = new HashSet<Vector2i>();
        foreach (var region in partition.Regions)
        foreach (var zone in region.Fill.Zones.Where(zone => zone.UtilityRoom != null))
        {
            var room = zone.UtilityRoom!;
            if (!room.Matches(zone) || !room.Satisfied(zone.Id, region.Partition))
                return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "InvalidUtilityHullReservation");
            foreach (var cell in room.ReusedHullCells)
            {
                if (expanded >= maximumExpandedCells)
                    return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "UtilityHullClaimBudget");
                expanded++;
                if (shape!.ContainsTarget(cell) || !shape.ContainsEnvelope(cell) || !shape.CanWrite(cell) ||
                    reserved.Contains(cell) || floor.Contains(cell) || walls.Contains(cell))
                    return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "InvalidUtilityHullClaim");
                utilityHull.Add(cell); claims.Add(cell);
            }
        }
        var ordered = edges.OrderBy(edge => edge.FloorCell.Y).ThenBy(edge => edge.FloorCell.X)
            .ThenBy(edge => edge.BoundaryCell.Y).ThenBy(edge => edge.BoundaryCell.X).ToReadOnlyList();
        var envelopeReservations = reserved.Where(shape.ContainsEnvelope).ToFrozenSet();
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-group-hull-boundary-v1");
        hash.AddString(analysis.SceneHash.ToString(CultureInfo.InvariantCulture));
        hash.AddInt((int) request.GeometryMode);
        hash.AddInt(ordered.Count);
        foreach (var edge in ordered)
        {
            AddCell(edge.FloorCell); AddCell(edge.BoundaryCell); hash.AddInt(edge.Component);
            hash.AddInt((int) edge.Kind); hash.AddString(edge.PortId ?? "");
            hash.AddInt(edge.GroupIds.Count);
            foreach (var id in edge.GroupIds) hash.AddString(id);
        }
        hash.AddInt(envelopeReservations.Count);
        foreach (var cell in KsProcgenGeometry.SortCells(envelopeReservations)) AddCell(cell);
        if (utilityHull.Count != 0)
        {
            hash.AddString("utility-generated-hull-v1"); hash.AddInt(utilityHull.Count);
            foreach (var cell in KsProcgenGeometry.SortCells(utilityHull)) AddCell(cell);
        }
        void AddCell(Vector2i cell) { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
        return new()
        {
            Status = KsProcgenEntranceSceneStatus.Candidate, Edges = ordered,
            RequiredEnvelopeCells = claims.ToFrozenSet(), ReservedEnvelopeCells = envelopeReservations,
            ExpandedCells = expanded, BoundaryHash = hash.Value,
        };
    }
}
