using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenGroupHullBoundaryTests
{
    [Test]
    public void EntranceSpansReserveEnvelopeBeforeWallClaims()
    {
        var fixture = Fixture(height: 3);
        var result = Analyze(fixture);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Edges.Count, Is.EqualTo(12));
        Assert.That(result.Edges.Count(edge => edge.Kind == KsProcgenOwnedBoundaryKind.OperationalEntrance), Is.EqualTo(2));
        Assert.That(result.RequiredEnvelopeCells.Count, Is.EqualTo(10));
        Assert.That(result.ReservedEnvelopeCells, Is.EquivalentTo(new[] { new Vector2i(-1, 1), new Vector2i(3, 1) }));
        Assert.That(result.RequiredEnvelopeCells.Overlaps(result.ReservedEnvelopeCells), Is.False);
        Assert.That(result.HasUnresolvedBoundary, Is.True);
        Assert.That(result.GasClosureVerified, Is.False);
        Assert.That(result.EngineBoundaryVerified, Is.False);
        Assert.That(result.Edges.All(edge => edge.GroupIds.SequenceEqual(new[] { "All" })), Is.True);
    }

    [Test]
    public void ASealedEntranceIsNeitherAnEnvelopeClaimNorGasProof()
    {
        var fixture = Fixture(sealSecond: true);
        var result = Analyze(fixture);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        var sealedEdge = result.Edges.Single(edge => edge.BoundaryCell == new Vector2i(3, 0));
        Assert.That(sealedEdge.Kind, Is.EqualTo(KsProcgenOwnedBoundaryKind.SealedEntrance));
        Assert.That(sealedEdge.PortId, Is.EqualTo("B"));
        Assert.That(result.RequiredEnvelopeCells.Contains(new(3, 0)), Is.False);
        Assert.That(result.HasUnresolvedBoundary, Is.True);
        Assert.That(result.GasClosureVerified, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WideEntrancesReserveEveryLaneWithThresholdsInsideOrOutsideTarget(bool inside)
    {
        var fixture = Fixture(height: 3, spanWidth: 2, thresholdsInside: inside);
        var result = Analyze(fixture);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Edges.Count, Is.EqualTo(12));
        Assert.That(result.Edges.Count(edge => edge.Kind == KsProcgenOwnedBoundaryKind.OperationalEntrance), Is.EqualTo(4));
        Assert.That(result.Edges.Where(edge => edge.Kind == KsProcgenOwnedBoundaryKind.OperationalEntrance)
            .Select(edge => edge.BoundaryCell), Is.EquivalentTo(new[]
            {
                new Vector2i(-1, 1), new Vector2i(-1, 2), new Vector2i(3, 1), new Vector2i(3, 2),
            }));
        Assert.That(result.RequiredEnvelopeCells.Count, Is.EqualTo(8));
        Assert.That(result.RequiredEnvelopeCells.Overlaps(result.ReservedEnvelopeCells), Is.False);
        Assert.That(result.Edges.Count(edge => edge.PortId == "A"), Is.EqualTo(2));
        Assert.That(result.Edges.Count(edge => edge.PortId == "B"), Is.EqualTo(2));
    }

    [Test]
    public void CleanHostAndUnknownBlockedContextCannotBecomeHullWallClaims()
    {
        var fixture = Fixture(hostOpening: true, blockedNeighbor: true);
        var result = Analyze(fixture);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Edges.Single(edge => edge.BoundaryCell == new Vector2i(1, 1)).Kind,
            Is.EqualTo(KsProcgenOwnedBoundaryKind.CleanHostOpening));
        Assert.That(result.Edges.Single(edge => edge.BoundaryCell == new Vector2i(2, 1)).Kind,
            Is.EqualTo(KsProcgenOwnedBoundaryKind.ExistingBlockedUnknown));
        Assert.That(result.RequiredEnvelopeCells.Contains(new(1, 1)), Is.False);
        Assert.That(result.RequiredEnvelopeCells.Contains(new(2, 1)), Is.False);
        Assert.That(result.ReservedEnvelopeCells, Is.SupersetOf(new[] { new Vector2i(1, 1), new Vector2i(2, 1) }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReadOnlyFloorStillContributesEveryBoundaryEdge(bool constant)
    {
        var fixture = Fixture(readOnlyEntry: true, constant: constant);
        var result = Analyze(fixture);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Edges.Count, Is.EqualTo(8));
        Assert.That(result.Edges.Count(edge => edge.FloorCell == new Vector2i(0, 0)), Is.EqualTo(3));
        Assert.That(result.RequiredEnvelopeCells.Count, Is.EqualTo(6));
    }

    [Test]
    public void VoidAndPreservedEnvelopeAreUnresolvedAndNeverWritableClaims()
    {
        var fixture = Fixture(specialNeighbors: true);
        var result = Analyze(fixture);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Edges.Single(edge => edge.BoundaryCell == new Vector2i(1, 1)).Kind,
            Is.EqualTo(KsProcgenOwnedBoundaryKind.ExplicitVoid));
        Assert.That(result.Edges.Single(edge => edge.BoundaryCell == new Vector2i(2, 1)).Kind,
            Is.EqualTo(KsProcgenOwnedBoundaryKind.PreservedUnknown));
        Assert.That(result.RequiredEnvelopeCells.Count, Is.EqualTo(4));
    }

    [Test]
    public void MissingFootprintEdgesAndInteriorHostEdgesHaveDifferentIdentity()
    {
        var fixture = Fixture(envelope: false);
        var footprint = Analyze(fixture);
        Assert.That(footprint.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        Assert.That(footprint.Edges.Count(edge => edge.Kind == KsProcgenOwnedBoundaryKind.MissingFootprintHull), Is.EqualTo(6));
        fixture.Request.GeometryMode = KsProcgenGeometryMode.InteriorFill;
        var interior = Analyze(fixture);
        Assert.That(interior.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), interior.Issue?.Code);
        Assert.That(interior.Edges.Count(edge => edge.Kind == KsProcgenOwnedBoundaryKind.UnknownHostContext), Is.EqualTo(6));
        Assert.That(interior.BoundaryHash, Is.Not.EqualTo(footprint.BoundaryHash));
        Assert.That(interior.RequiredEnvelopeCells, Is.Empty);
    }

    [Test]
    public void InventoryExpansionsShareSourceValidationWorkAndFailAtomically()
    {
        var fixture = Fixture();
        var full = Analyze(fixture);
        Assert.That(full.ExpandedCells, Is.EqualTo(6)); // Three source component visits and three boundary expansions.
        var limited = KsProcgenGroupHullBoundaryAnalyzer.Analyze(fixture.Request, fixture.Partition,
            maximumExpandedCells: full.ExpandedCells - 1);
        AssertFailure(limited, "GroupHullBoundaryExpansionBudget", KsProcgenEntranceSceneStatus.BudgetExceeded);
        Assert.That(limited.ExpandedCells, Is.EqualTo(5));
        var exact = KsProcgenGroupHullBoundaryAnalyzer.Analyze(fixture.Request, fixture.Partition,
            maximumExpandedCells: full.ExpandedCells);
        Assert.That(exact.BoundaryHash, Is.EqualTo(full.BoundaryHash));
        AssertFailure(KsProcgenGroupHullBoundaryAnalyzer.Analyze(fixture.Request, fixture.Partition, maximumExpandedCells: 0),
            "EntranceSceneExpansionBudget", KsProcgenEntranceSceneStatus.BudgetExceeded);
    }

    [Test]
    public void StaleSceneAndMissingTargetWallDispositionAreRejected()
    {
        var fixture = Fixture(height: 3, separate: true);
        Assert.That(Analyze(fixture).Edges.Any(edge => edge.Kind == KsProcgenOwnedBoundaryKind.ProposedTargetWall), Is.True);
        var missing = Copy(fixture.Partition, walls: fixture.Partition.WallCells.Skip(1).ToArray());
        AssertFailure(KsProcgenGroupHullBoundaryAnalyzer.Analyze(fixture.Request, missing),
            "StaleGroupHullBoundarySource", KsProcgenEntranceSceneStatus.InvalidInput);
        fixture.Request.Shape.EnvelopeCells.Add(new(10, 10));
        AssertFailure(Analyze(fixture), "StaleGroupHullBoundarySource", KsProcgenEntranceSceneStatus.InvalidInput);
    }

    [Test]
    public void ForgedGroupMetadataDoesNotChangeGraphOwnershipAndOutputIsDetached()
    {
        var fixture = Fixture(height: 3, separate: true);
        var source = Analyze(fixture);
        var analysis = new KsProcgenEntranceSceneResult { Status = fixture.Partition.Analysis!.Status,
            ConfigurationId = fixture.Partition.Analysis.ConfigurationId, SceneHash = fixture.Partition.Analysis.SceneHash,
            Components = new[] { new KsProcgenEntranceComponentWitness(0, fixture.Partition.FloorCells, new[] { "Fake" }) } };
        var changed = Copy(fixture.Partition, analysis: analysis);
        var safe = KsProcgenGroupHullBoundaryAnalyzer.Analyze(fixture.Request, changed);
        Assert.That(safe.BoundaryHash, Is.EqualTo(source.BoundaryHash));
        Assert.That(safe.Edges.SelectMany(edge => edge.GroupIds), Does.Not.Contain("Fake"));
        fixture.Request.Shape.Cells.Clear();
        fixture.Request.Shape.EnvelopeCells.Clear();
        Assert.That(source.Edges.Count, Is.GreaterThan(0));
        Assert.That(source.RequiredEnvelopeCells.Count, Is.GreaterThan(0));
    }

    private sealed record HullFixture(KsProcgenRequest Request, KsProcgenGroupPartitionResult Partition);
    private static HullFixture Fixture(int height = 1, bool envelope = true, bool sealSecond = false,
        bool readOnlyEntry = false, bool constant = false, bool hostOpening = false, bool blockedNeighbor = false,
        bool specialNeighbors = false, bool separate = false, int spanWidth = 1, bool thresholdsInside = false)
    {
        var entryY = separate ? 0 : height / 2;
        var secondY = separate ? height - 1 : entryY;
        var request = new KsProcgenRequest
        {
            RequestId = "HullAudit", Seed = 31, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
            Shape = new() { Cells = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, height)
                .Select(y => new Vector2i(x, y))).ToList() },
            EntranceDomain = new()
            {
                Entrances = new List<KsProcgenEntranceSpec>
                {
                    new() { PortId = "A", ThresholdCells = Enumerable.Range(entryY, spanWidth)
                        .Select(y => new Vector2i(thresholdsInside ? 0 : -1, y)).ToList(), InwardNormal = new(1, 0) },
                    new() { PortId = "B", ThresholdCells = Enumerable.Range(secondY, spanWidth)
                        .Select(y => new Vector2i(thresholdsInside ? 2 : 3, y)).ToList(), InwardNormal = new(-1, 0), OptionalSealable = true },
                },
                Configurations = new List<KsProcgenEntranceConfigurationSpec> { new() { Id = "Network",
                    Groups = new List<KsProcgenEntranceGroupSpec> { new() { Id = "All", Ports = sealSecond || separate
                        ? new List<string> { "A" } : new List<string> { "A", "B" } } },
                    SealedPorts = sealSecond ? new List<string> { "B" } : new List<string>() } },
            },
        };
        if (separate) request.EntranceDomain.Configurations[0].Groups.Add(new() { Id = "Other", Ports = new List<string> { "B" } });
        var floorCells = request.Shape.Cells.ToHashSet();
        if (envelope)
            request.Shape.EnvelopeCells = floorCells.SelectMany(cell => new[] { cell + new Vector2i(1, 0), cell + new Vector2i(-1, 0),
                cell + new Vector2i(0, 1), cell + new Vector2i(0, -1) }).Where(cell => !floorCells.Contains(cell)).Distinct().ToList();
        if (readOnlyEntry)
        {
            if (constant) request.ConstantRegions.Add(new() { Id = "Entry", SourceId = "Fixture", ContentFingerprint = "Declared",
                LocalCells = new List<Vector2i> { new(0, entryY) } });
            else request.Shape.PreservedCells.Add(new(0, entryY));
        }
        if (specialNeighbors)
        {
            request.Shape.EnvelopeCells.Remove(new(1, 1)); request.Shape.VoidCells.Add(new(1, 1));
            request.Shape.EnvelopeCells.Remove(new(2, 1)); request.Shape.Cells.Add(new(2, 1));
            request.Shape.PreservedCells.Add(new(2, 1));
        }
        var host = request.EntranceDomain.Entrances.Where(port => !sealSecond || port.PortId != "B")
            .SelectMany(port => port.ThresholdCells.SelectMany(cell => new[] { cell, cell - port.InwardNormal }))
            .Where(cell => !floorCells.Contains(cell)).ToHashSet();
        if (hostOpening) host.Add(new(1, 1));
        var blocked = new HashSet<Vector2i>();
        if (sealSecond) blocked.Add(new(3, secondY));
        if (blockedNeighbor) blocked.Add(new(2, 1));
        var context = new KsProcgenGroupRouteContext(readOnlyEntry ? new HashSet<Vector2i> { new(0, entryY) } : new HashSet<Vector2i>(),
            host, blocked, true, true);
        var floor = KsProcgenGroupFloorPlanner.Plan(request, "Network", context);
        Assert.That(floor.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), floor.Issue?.Code);
        var partition = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64);
        Assert.That(partition.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), partition.Issue?.Code);
        return new(request, partition);
    }
    private static KsProcgenGroupHullBoundaryResult Analyze(HullFixture fixture) =>
        KsProcgenGroupHullBoundaryAnalyzer.Analyze(fixture.Request, fixture.Partition);
    private static KsProcgenGroupPartitionResult Copy(KsProcgenGroupPartitionResult source,
        IReadOnlyList<Vector2i>? walls = null, KsProcgenEntranceSceneResult? analysis = null) => new()
    {
        Status = source.Status, PartitionHash = source.PartitionHash, FloorCells = source.FloorCells,
        WallCells = walls ?? source.WallCells, Scene = source.Scene, Analysis = analysis ?? source.Analysis,
    };
    private static void AssertFailure(KsProcgenGroupHullBoundaryResult result, string code, KsProcgenEntranceSceneStatus status)
    {
        Assert.That(result.Status, Is.EqualTo(status), result.Issue?.Code);
        Assert.That(result.Issue!.Code, Is.EqualTo(code));
        Assert.That(result.Edges, Is.Empty);
        Assert.That(result.RequiredEnvelopeCells, Is.Empty);
        Assert.That(result.ReservedEnvelopeCells, Is.Empty);
        Assert.That(result.BoundaryHash, Is.Zero);
        Assert.That(result.HasUnresolvedBoundary, Is.True);
    }
}
