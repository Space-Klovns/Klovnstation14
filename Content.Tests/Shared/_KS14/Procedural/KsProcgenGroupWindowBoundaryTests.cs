using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenGroupWindowBoundaryTests
{
    [Test]
    public void OnlyDeclaredFacesBecomeEligibleAndSourcePortsRemainReserved()
    {
        var fixture = Fixture();
        var facts = new[] { Fact(new(1, 1), new(0, 1)) };
        var result = Analyze(fixture, facts);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Cells.Count, Is.EqualTo(6));
        Assert.That(result.Cells.Count(cell => cell.FacesExterior), Is.EqualTo(1));
        Assert.That(result.UnclassifiedStructuralCells.Count, Is.EqualTo(5));
        Assert.That(result.ExteriorScopeComplete, Is.False);
        Assert.That(result.EngineExteriorVerified, Is.False);
        Assert.That(result.Cells.Select(cell => cell.Cell), Does.Not.Contain(new Vector2i(-1, 0)));
        Assert.That(result.HullBoundary!.ReservedEnvelopeCells, Does.Contain(new Vector2i(-1, 0)));
        var selection = KsProcgenWindowPlanner.Plan(result.Cells, 1, exteriorWindowFraction: 1f);
        Assert.That(selection.ChosenWindowCells, Is.EqualTo(new[] { new Vector2i(1, 1) }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PreservedAndConstantWallsContributeFixedCountsWithoutWritePermission(bool constant)
    {
        var fixture = Fixture(fixedWall: true, constant: constant);
        var result = Analyze(fixture, new[] { Fact(new(1, 1), new(0, 1)) with { IsExistingWindow = true } });
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        var fixedCell = result.Cells.Single(cell => cell.Cell == new Vector2i(1, 1));
        Assert.That(fixedCell.Fixed && fixedCell.IsWindow, Is.True);
        var selection = KsProcgenWindowPlanner.Plan(result.Cells, 1, exteriorWindowFraction: 1f);
        Assert.That(selection.FixedWindowCells, Is.EqualTo(1));
        Assert.That(selection.AchievedWindowCells, Is.EqualTo(1));
        Assert.That(selection.ChosenWindowCells, Is.Empty);
    }

    [Test]
    public void BadNormalsDuplicateFacesAndEditableExistingWindowsAreRejected()
    {
        var fixture = Fixture();
        AssertFailure(Analyze(fixture, new[] { Fact(new(1, 1), new(1, 1)) }), "InvalidOwnedWindowFacts");
        var fact = Fact(new(1, 1), new(0, 1));
        AssertFailure(Analyze(fixture, new[] { fact, fact }), "InvalidOwnedWindowFacts");
        AssertFailure(Analyze(fixture, new[] { fact with { IsExistingWindow = true } }), "ContradictoryOwnedWindowFace");
        AssertFailure(Analyze(fixture, new[] { fact with { OutwardNormal = new(1, 0) } }), "ContradictoryOwnedWindowFace");
        AssertFailure(Analyze(fixture, new[] { Fact(new(-1, 0), new(-1, 0)) }), "ContradictoryOwnedWindowFace");
        AssertFailure(Analyze(fixture, new[] { Fact(new(20, 20), new(0, 1)) }), "ContradictoryOwnedWindowFace");
    }

    [Test]
    public void ExteriorProbeCannotContradictKnownHostPassage()
    {
        var fixture = Fixture(hostProbe: true);
        AssertFailure(Analyze(fixture, new[] { Fact(new(1, 1), new(0, 1)) }), "ContradictoryOwnedWindowFace");
        var internalFace = Analyze(fixture, new[] { Fact(new(1, 1), new(0, 1)) with { FacesExterior = false } });
        Assert.That(internalFace.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        Assert.That(internalFace.Cells.All(cell => !cell.FacesExterior), Is.True);
    }

    [Test]
    public void CompleteNonExteriorFactsDoNotInventWindowsAndContextChangesIdentity()
    {
        var fixture = Fixture();
        var facts = Enumerable.Range(0, 3).SelectMany(x => new[]
        {
            Fact(new(x, 1), new(0, 1)) with { FacesExterior = false },
            Fact(new(x, -1), new(0, -1)) with { FacesExterior = false },
        }).ToArray();
        var result = Analyze(fixture, facts);
        Assert.That(result.ExteriorScopeComplete, Is.True);
        Assert.That(result.UnclassifiedStructuralCells, Is.Empty);
        Assert.That(result.Cells.All(cell => !cell.FacesExterior), Is.True);
        Assert.That(Analyze(fixture, facts.Reverse().ToArray()).ClassificationHash, Is.EqualTo(result.ClassificationHash));
        facts[0] = facts[0] with { FacesExterior = true };
        Assert.That(Analyze(fixture, facts).ClassificationHash, Is.Not.EqualTo(result.ClassificationHash));
        Assert.That(result.Facts.All(fact => !fact.FacesExterior), Is.True);
        fixture.Request.Shape.Cells.Clear();
        Assert.That(result.Cells.Count, Is.EqualTo(6));
    }

    [Test]
    public void ClassificationSharesHullValidationWorkAndExhaustionIsAtomic()
    {
        var fixture = Fixture();
        var result = Analyze(fixture, new KsProcgenExteriorWindowFact[] { });
        Assert.That(result.ExpandedCells, Is.EqualTo(12)); // Six hull visits plus six structural cells.
        var limited = KsProcgenGroupWindowBoundaryAnalyzer.Analyze(fixture.Request, fixture.Partition, [],
            maximumExpandedCells: result.ExpandedCells - 1);
        AssertFailure(limited, "OwnedWindowClassificationBudget", status: KsProcgenEntranceSceneStatus.BudgetExceeded);
        Assert.That(limited.ExpandedCells, Is.EqualTo(11));
        Assert.That(KsProcgenGroupWindowBoundaryAnalyzer.Analyze(fixture.Request, fixture.Partition, [],
            maximumExpandedCells: 12).ClassificationHash, Is.EqualTo(result.ClassificationHash));
        AssertFailure(KsProcgenGroupWindowBoundaryAnalyzer.Analyze(fixture.Request, fixture.Partition, [], maximumExpandedCells: 0),
            "EntranceSceneExpansionBudget", status: KsProcgenEntranceSceneStatus.BudgetExceeded);
    }

    [Test]
    public void MissingEnvelopeDoesNotBecomeACompleteExteriorScope()
    {
        var fixture = Fixture(envelope: false);
        var result = Analyze(fixture, []);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        Assert.That(result.Cells, Is.Empty);
        Assert.That(result.ExteriorScopeComplete, Is.False);
        Assert.That(result.HullBoundary!.Edges.Any(edge => edge.Kind == KsProcgenOwnedBoundaryKind.MissingFootprintHull), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AConcaveSharedBoundaryCellIsACornerAndCannotFaceThroughAnotherFloorLane(bool closedHole)
    {
        var fixture = Fixture();
        fixture.Request.Shape.Cells = closedHole ? Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 3)
            .Select(y => new Vector2i(x, y))).Where(cell => cell != new Vector2i(1, 1)).ToList()
            : new List<Vector2i> { new(0, 0), new(1, 0), new(0, 1) };
        var target = fixture.Request.Shape.Cells.ToHashSet();
        fixture.Request.Shape.EnvelopeCells = target.SelectMany(cell => new[] { cell + new Vector2i(1, 0), cell + new Vector2i(-1, 0),
            cell + new Vector2i(0, 1), cell + new Vector2i(0, -1) }).Where(cell => !target.Contains(cell)).Distinct().ToList();
        fixture.Request.EntranceDomain!.Entrances[1].ThresholdCells = new List<Vector2i> { closedHole ? new(3, 2) : new(-1, 1) };
        fixture.Request.EntranceDomain.Entrances[1].InwardNormal = closedHole ? new(-1, 0) : new(1, 0);
        var host = fixture.Request.EntranceDomain.Entrances.SelectMany(port => port.ThresholdCells
            .SelectMany(cell => new[] { cell, cell - port.InwardNormal })).ToHashSet();
        var floor = KsProcgenGroupFloorPlanner.Plan(fixture.Request, "All", new(new HashSet<Vector2i>(),
            host, new HashSet<Vector2i>(), true, true));
        var partition = KsProcgenGroupPartitionPlanner.Plan(fixture.Request, floor, preferredMaxRoomCells: 64);
        Assert.That(partition.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), partition.Issue?.Code);
        fixture = new(fixture.Request, partition);
        var fact = Fact(new(1, 1), new(0, 1)) with { FacesExterior = !closedHole };
        var result = Analyze(fixture, new[] { fact });
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Cells.Single(cell => cell.Cell == fact.Cell).IsCorner, Is.True);
        if (closedHole)
            AssertFailure(Analyze(fixture, new[] { fact with { FacesExterior = true } }), "ContradictoryOwnedWindowFace");
        else
        {
            var selection = KsProcgenWindowPlanner.Plan(result.Cells, 1, exteriorWindowFraction: 1f);
            Assert.That(selection.ChosenWindowCells, Is.Empty);
            Assert.That(selection.ExcludedCells.Single(cell => cell.Cell == fact.Cell).Reason,
                Is.EqualTo(KsProcgenWindowExclusionReason.Corner));
        }
    }

    private sealed record WindowFixture(KsProcgenRequest Request, KsProcgenGroupPartitionResult Partition);
    private static WindowFixture Fixture(bool fixedWall = false, bool constant = false, bool hostProbe = false, bool envelope = true)
    {
        var request = new KsProcgenRequest
        {
            RequestId = "WindowFaces", Seed = 31, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
            Shape = new() { Cells = new List<Vector2i> { new(0, 0), new(1, 0), new(2, 0) } },
            EntranceDomain = new()
            {
                Entrances = new List<KsProcgenEntranceSpec>
                {
                    new() { PortId = "A", ThresholdCells = new List<Vector2i> { new(-1, 0) }, InwardNormal = new(1, 0) },
                    new() { PortId = "B", ThresholdCells = new List<Vector2i> { new(3, 0) }, InwardNormal = new(-1, 0) },
                },
                Configurations = new List<KsProcgenEntranceConfigurationSpec>
                {
                    new() { Id = "All", Groups = new List<KsProcgenEntranceGroupSpec>
                        { new() { Id = "All", Ports = new List<string> { "A", "B" } } } },
                },
            },
        };
        if (envelope)
            request.Shape.EnvelopeCells = Enumerable.Range(0, 3).SelectMany(x => new[] { new Vector2i(x, 1), new Vector2i(x, -1) })
                .Concat(new[] { new Vector2i(-1, 0), new Vector2i(3, 0) }).ToList();
        if (fixedWall)
        {
            request.Shape.Cells.Add(new(1, 1)); request.Shape.EnvelopeCells.Remove(new(1, 1));
            if (constant) request.ConstantRegions.Add(new() { Id = "Fixed", SourceId = "Fixture", ContentFingerprint = "Declared",
                LocalCells = new List<Vector2i> { new(1, 1) } });
            else request.Shape.PreservedCells.Add(new(1, 1));
        }
        var host = new HashSet<Vector2i> { new(-1, 0), new(-2, 0), new(3, 0), new(4, 0) };
        if (hostProbe) host.Add(new(1, 2));
        var floor = KsProcgenGroupFloorPlanner.Plan(request, "All",
            new(new HashSet<Vector2i>(), host, new HashSet<Vector2i>(), true, true));
        Assert.That(floor.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), floor.Issue?.Code);
        var partition = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64);
        Assert.That(partition.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), partition.Issue?.Code);
        return new(request, partition);
    }
    private static KsProcgenExteriorWindowFact Fact(Vector2i cell, Vector2i normal) => new(cell, normal, true, true);
    private static KsProcgenGroupWindowBoundaryResult Analyze(WindowFixture fixture, IReadOnlyList<KsProcgenExteriorWindowFact> facts) =>
        KsProcgenGroupWindowBoundaryAnalyzer.Analyze(fixture.Request, fixture.Partition, facts);
    private static void AssertFailure(KsProcgenGroupWindowBoundaryResult result, string code,
        KsProcgenEntranceSceneStatus status = KsProcgenEntranceSceneStatus.InvalidInput)
    {
        Assert.That(result.Status, Is.EqualTo(status), result.Issue?.Code);
        Assert.That(result.Issue!.Code, Is.EqualTo(code));
        Assert.That(result.Cells, Is.Empty);
        Assert.That(result.Facts, Is.Empty);
        Assert.That(result.UnclassifiedStructuralCells, Is.Empty);
        Assert.That(result.HullBoundary, Is.Null);
        Assert.That(result.ClassificationHash, Is.Zero);
        Assert.That(result.ExteriorScopeComplete, Is.False);
    }
}
