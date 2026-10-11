using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenGroupTacticalTests
{
    [Test]
    public void IndependentCorridorsHaveOwnedChokesTerminalSidesAndRedundancy()
    {
        var fixture = Fixture();
        var result = Analyze(fixture);
        Assert.That(result.Status, Is.EqualTo(KsProcgenTacticalStatus.Complete), result.Issue?.Code);
        Assert.That(result.Networks.Count, Is.EqualTo(2));
        Assert.That(result.MeasurementScope, Is.EqualTo(KsProcgenEntranceIsolationScope.DomainLocal));
        Assert.That(result.EngineTraversalVerified, Is.False);
        foreach (var network in result.Networks)
        {
            Assert.That(network.GroupIds.Count, Is.EqualTo(1));
            Assert.That(network.RequiredTerminals.Count, Is.EqualTo(2));
            Assert.That(network.Movement.ArticulationCells.Count, Is.EqualTo(3));
            Assert.That(network.Movement.BridgeEdges.Count, Is.EqualTo(4));
            var middle = network.Movement.ChokeDetails.Single(detail => detail.Cell.X == 2);
            Assert.That(middle.Sides.Select(side => side.CleanCells), Is.EquivalentTo(new[] { 2, 2 }));
            Assert.That(middle.Sides.Select(side => side.RequiredTerminals), Is.EquivalentTo(new[] { 1, 1 }));
            Assert.That(network.AlternateRoutes.Pairs.Single().Chokes.Single().ConnectedWithoutChoke, Is.False);
            Assert.That(network.Redundancy!.Pairs.Single().IndependentRoutes, Is.EqualTo(1));
        }
    }

    [Test]
    public void OutsideHostBypassCannotAuthorizeACrossNetworkPair()
    {
        var fixture = Fixture();
        fixture.Pairs.Clear();
        fixture.Pairs.Add(new("Foreign", new(0, 0), new(4, 2)));
        var result = Analyze(fixture);
        AssertFailure(result, "CrossNetworkTacticalPair", KsProcgenTacticalStatus.InvalidInput);
        Assert.That(result.Searches, Is.Zero);
        Assert.That(result.ExaminedFlowEdges, Is.Zero);
        // The same caller-declared outside bypass becomes illegal in request-wide exact mode.
        fixture.Request.EntranceDomain!.Configurations[0].IsolationScope = KsProcgenEntranceIsolationScope.RequestNetwork;
        var rejected = Analyze(fixture);
        Assert.That(rejected.Status, Is.EqualTo(KsProcgenTacticalStatus.InvalidInput));
        Assert.That(rejected.Networks, Is.Empty);
    }

    [Test]
    public void RequiredConnectionsMayMeasurePairsAcrossMergedGroupLabels()
    {
        var fixture = Fixture(mode: KsProcgenEntranceConnectionMode.RequiredConnections);
        fixture.Pairs.Clear();
        fixture.Pairs.Add(new("Joined", new(0, 0), new(4, 2)));
        var result = Analyze(fixture);
        Assert.That(result.Status, Is.EqualTo(KsProcgenTacticalStatus.Complete), result.Issue?.Code);
        Assert.That(result.Networks.Single().GroupIds, Is.EquivalentTo(new[] { "A", "B" }));
        Assert.That(result.Networks.Single().AlternateRoutes.Pairs.Single().BaselineShortestSteps, Is.EqualTo(6));
        Assert.That(result.Networks.Single().Redundancy!.Pairs.Single().IndependentRoutes, Is.EqualTo(2));
    }

    [Test]
    public void CappedRedundancyRemainsALowerBoundAndChangesMeasurementIdentity()
    {
        var fixture = Fixture(mode: KsProcgenEntranceConnectionMode.RequiredConnections);
        fixture.Pairs.Clear();
        fixture.Pairs.Add(new("Joined", new(0, 0), new(4, 2)));
        var full = Analyze(fixture);
        var capped = KsProcgenGroupTacticalAnalyzer.Analyze(fixture.Request, fixture.Interior, fixture.Pairs, fixture.Chokes,
            measureRedundancy: true, maximumIndependentRoutes: 1);
        var count = capped.Networks.Single().Redundancy!.Pairs.Single();
        Assert.That(count.IndependentRoutes, Is.EqualTo(1));
        Assert.That(count.AtLeastConfiguredCap, Is.True);
        Assert.That(full.Networks.Single().Redundancy!.Pairs.Single().AtLeastConfiguredCap, Is.False);
        Assert.That(capped.MeasurementHash, Is.Not.EqualTo(full.MeasurementHash));
    }

    [Test]
    public void GraphSearchAndFlowBudgetsAreSharedAndFailAtomically()
    {
        var fixture = Fixture();
        var full = Analyze(fixture);
        Assert.That(full.Status, Is.EqualTo(KsProcgenTacticalStatus.Complete));
        var graphLimited = KsProcgenGroupTacticalAnalyzer.Analyze(fixture.Request, fixture.Interior, fixture.Pairs,
            fixture.Chokes, measureRedundancy: true, maximumExpandedCells: full.ExpandedCells - 1);
        AssertFailure(graphLimited, "AlternateRouteExpansionBudget", KsProcgenTacticalStatus.BudgetExceeded);
        Assert.That(graphLimited.ExpandedCells, Is.EqualTo(full.ExpandedCells - 1));
        var searchLimited = KsProcgenGroupTacticalAnalyzer.Analyze(fixture.Request, fixture.Interior, fixture.Pairs,
            fixture.Chokes, measureRedundancy: true, maximumSearches: full.Searches - 1);
        AssertFailure(searchLimited, "AlternateRouteBudget", KsProcgenTacticalStatus.BudgetExceeded);
        Assert.That(searchLimited.Searches, Is.EqualTo(2));
        var flowLimited = KsProcgenGroupTacticalAnalyzer.Analyze(fixture.Request, fixture.Interior, fixture.Pairs,
            fixture.Chokes, measureRedundancy: true, maximumExaminedFlowEdges: full.ExaminedFlowEdges - 1);
        AssertFailure(flowLimited, "RouteRedundancyFlowBudget", KsProcgenTacticalStatus.BudgetExceeded);
        Assert.That(flowLimited.ExaminedFlowEdges, Is.EqualTo(full.ExaminedFlowEdges - 1));
        var exact = KsProcgenGroupTacticalAnalyzer.Analyze(fixture.Request, fixture.Interior, fixture.Pairs, fixture.Chokes,
            measureRedundancy: true, maximumExpandedCells: full.ExpandedCells, maximumSearches: full.Searches,
            maximumExaminedFlowEdges: full.ExaminedFlowEdges);
        Assert.That(exact.MeasurementHash, Is.EqualTo(full.MeasurementHash));
    }

    [Test]
    public void SharedSideDetailCapKeepsEveryExactChokeAndBridge()
    {
        var fixture = Fixture();
        var truncated = KsProcgenGroupTacticalAnalyzer.Analyze(fixture.Request, fixture.Interior, fixture.Pairs,
            fixture.Chokes, maximumDetailedChokes: 1);
        Assert.That(truncated.Status, Is.EqualTo(KsProcgenTacticalStatus.DetailsTruncated));
        Assert.That(truncated.Networks.Sum(network => network.Movement.ChokeDetails.Count), Is.EqualTo(1));
        Assert.That(truncated.Networks.Sum(network => network.Movement.ArticulationCells.Count), Is.EqualTo(6));
        Assert.That(truncated.Networks.Sum(network => network.Movement.BridgeEdges.Count), Is.EqualTo(8));
        Assert.That(truncated.MeasurementHash, Is.Not.Zero);
        Assert.That(truncated.MeasurementHash, Is.Not.EqualTo(Analyze(fixture).MeasurementHash));
    }

    [Test]
    public void QueryOrderingAndSufficientBudgetsDoNotChangeMeasurementIdentity()
    {
        var fixture = Fixture();
        var full = Analyze(fixture);
        fixture.Pairs.Reverse();
        fixture.Chokes = fixture.Chokes.Reverse().ToHashSet();
        var replay = Analyze(fixture);
        Assert.That(replay.MeasurementHash, Is.EqualTo(full.MeasurementHash));
        fixture.Pairs.Clear();
        fixture.Chokes.Clear();
        fixture.Request.Shape.Cells.Clear();
        Assert.That(full.Networks[0].AlternateRoutes.Pairs.Count, Is.EqualTo(1));
        Assert.That(full.Networks[0].Movement.ArticulationCells.Count, Is.EqualTo(3));
    }

    [Test]
    public void RevalidationRejectsStaleSceneAndIgnoresForgedComponentLabels()
    {
        var fixture = Fixture();
        var original = fixture.Interior;
        var fake = new KsProcgenGroupInteriorResult
        {
            Status = original.Status, Scene = original.Scene, AccessHash = original.AccessHash,
            Analysis = new() { Status = KsProcgenEntranceSceneStatus.Candidate, ConfigurationId = original.Analysis!.ConfigurationId,
                SceneHash = original.Analysis.SceneHash, Components = new[] { new KsProcgenEntranceComponentWitness(0,
                    original.Scene!.CleanDomainCells.ToArray(), new[] { "A", "B" }) } },
        };
        var safe = KsProcgenGroupTacticalAnalyzer.Analyze(fixture.Request, fake, fixture.Pairs, fixture.Chokes);
        Assert.That(safe.Networks.Count, Is.EqualTo(2));
        var stale = new KsProcgenGroupInteriorResult
        {
            Status = original.Status, AccessHash = original.AccessHash, Analysis = original.Analysis,
            Scene = original.Scene! with { CleanHostCells = new HashSet<Vector2i> { new(-1, 0), new(-2, 0), new(-1, 2), new(-2, 2) } },
        };
        AssertFailure(KsProcgenGroupTacticalAnalyzer.Analyze(fixture.Request, stale, fixture.Pairs, fixture.Chokes),
            "StaleGroupTacticalSource", KsProcgenTacticalStatus.InvalidInput);
    }

    [Test]
    public void HostCellsDuplicateIdsAndBlockedCellsCannotBeNominated()
    {
        var fixture = Fixture();
        fixture.Chokes.Add(new(-2, 1));
        AssertFailure(Analyze(fixture), "InvalidOwnedTacticalQuery", KsProcgenTacticalStatus.InvalidInput);
        fixture = Fixture();
        fixture.Pairs[1] = fixture.Pairs[0];
        AssertFailure(Analyze(fixture), "InvalidOwnedTacticalQuery", KsProcgenTacticalStatus.InvalidInput);
        fixture = Fixture();
        fixture.Chokes.Add(new(2, 1));
        AssertFailure(Analyze(fixture), "InvalidOwnedTacticalQuery", KsProcgenTacticalStatus.InvalidInput);
    }

    [Test]
    public void OneByThreeNetworksAndRemovedEndpointsHaveTruthfulFallbacks()
    {
        var fixture = Fixture(width: 3, single: true);
        fixture.Chokes = new HashSet<Vector2i> { new(0, 0), new(1, 0) };
        var result = Analyze(fixture);
        Assert.That(result.Status, Is.EqualTo(KsProcgenTacticalStatus.Complete), result.Issue?.Code);
        Assert.That(result.Networks.Single().Movement.ArticulationCells, Is.EquivalentTo(new[] { new Vector2i(1, 0) }));
        Assert.That(result.Networks.Single().AlternateRoutes.Pairs.Single().Chokes.First().EndpointRemoved, Is.True);
        var exact = KsProcgenGroupTacticalAnalyzer.Analyze(fixture.Request, fixture.Interior, fixture.Pairs, fixture.Chokes,
            measureRedundancy: true, maximumExpandedCells: result.ExpandedCells, maximumSearches: result.Searches,
            maximumExaminedFlowEdges: result.ExaminedFlowEdges);
        Assert.That(exact.MeasurementHash, Is.EqualTo(result.MeasurementHash));
        var zero = KsProcgenGroupTacticalAnalyzer.Analyze(fixture.Request, fixture.Interior, fixture.Pairs, fixture.Chokes,
            maximumExpandedCells: 0);
        AssertFailure(zero, "EntranceSceneExpansionBudget", KsProcgenTacticalStatus.BudgetExceeded);
        var noQueries = KsProcgenGroupTacticalAnalyzer.Analyze(fixture.Request, fixture.Interior,
            new KsProcgenRoutePair[0], new HashSet<Vector2i>(), measureRedundancy: true, maximumSearches: 0,
            maximumExaminedFlowEdges: 0);
        Assert.That(noQueries.Status, Is.EqualTo(KsProcgenTacticalStatus.Complete));
        Assert.That(noQueries.Searches, Is.Zero);
        Assert.That(noQueries.ExaminedFlowEdges, Is.Zero);
    }

    private sealed class TacticalFixture
    {
        public KsProcgenRequest Request = default!;
        public KsProcgenGroupInteriorResult Interior = default!;
        public List<KsProcgenRoutePair> Pairs = new();
        public HashSet<Vector2i> Chokes = new();
    }

    private static TacticalFixture Fixture(int width = 5, bool single = false,
        KsProcgenEntranceConnectionMode mode = KsProcgenEntranceConnectionMode.ExactComponents)
    {
        var request = new KsProcgenRequest
        {
            RequestId = "TacticalAudit", Seed = 29, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
            Shape = new() { Cells = Enumerable.Range(0, width).SelectMany(x => Enumerable.Range(0, single ? 1 : 3)
                .Select(y => new Vector2i(x, y))).ToList() },
            EntranceDomain = new()
            {
                Entrances = new List<KsProcgenEntranceSpec>
                    { new() { PortId = "A1", ThresholdCells = new List<Vector2i> { new(-1, 0) }, InwardNormal = new(1, 0) } },
                Configurations = new List<KsProcgenEntranceConfigurationSpec> { new() { Id = "Networks", Mode = mode,
                    Groups = new List<KsProcgenEntranceGroupSpec> { new() { Id = "A", Ports = new List<string> { "A1" },
                        RootCells = new List<Vector2i> { new(width - 1, 0) } } } } },
            },
        };
        if (!single)
        {
            request.EntranceDomain.Entrances.Add(new() { PortId = "B1", ThresholdCells = new List<Vector2i> { new(-1, 2) }, InwardNormal = new(1, 0) });
            request.EntranceDomain.Configurations[0].Groups.Add(new() { Id = "B", Ports = new List<string> { "B1" },
                RootCells = new List<Vector2i> { new(width - 1, 2) } });
        }
        var host = single ? new HashSet<Vector2i> { new(-1, 0), new(-2, 0) }
            : new HashSet<Vector2i> { new(-1, 0), new(-2, 0), new(-2, 1), new(-2, 2), new(-1, 2) };
        var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(), host, new HashSet<Vector2i>(), true, true);
        var floor = KsProcgenGroupFloorPlanner.Plan(request, "Networks", context);
        Assert.That(floor.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), floor.Issue?.Code);
        var partition = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64);
        Assert.That(partition.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), partition.Issue?.Code);
        var selection = new KsProcgenThemeSelection("Theme", "Tiles", "Palette", "Walls", "Family", null, null, null, new string[0]);
        var regions = partition.Regions.SelectMany(network => network.Fill.Zones.Select(zone => new KsProcgenThemedRegion(zone.Id,
            KsProcgenZoneKind.RoomProposal, new[] { zone.Id }, zone.Cells.Where(network.Partition.FloorCells.Contains).ToArray(),
            network.Partition.WallCells, selection, new KsProcgenPackMinimum[0])
        {
            EntranceGroupIds = network.GroupIds, ProtectedPassageCells = zone.Cells.Where(network.ProtectedCells.Contains).ToArray(),
        })).ToArray();
        var themes = new KsProcgenThemeAssignmentResult { Status = KsProcgenThemeAssignmentStatus.Selected, Regions = regions };
        var furnishings = regions.Select(region => new KsProcgenFurnishedRegion(region.Id,
            new() { Status = KsProcgenFurnishingStatus.Proposed, ProtectedPassageCells = region.ProtectedPassageCells })).ToArray();
        var lighting = regions.Select(region => new KsProcgenLitRegion(region.Id,
            new() { Status = KsProcgenLightingPlanStatus.NotRequested })).ToArray();
        var interior = KsProcgenGroupInteriorAnalyzer.Analyze(request, partition, themes, furnishings, lighting);
        Assert.That(interior.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), interior.Issue?.Code);
        var fixture = new TacticalFixture { Request = request, Interior = interior };
        fixture.Pairs.Add(new("A", new(0, 0), new(width - 1, 0)));
        fixture.Chokes.Add(new(1, 0));
        if (!single)
        {
            fixture.Pairs.Add(new("B", new(0, 2), new(width - 1, 2)));
            fixture.Chokes.Add(new(1, 2));
        }
        return fixture;
    }

    private static KsProcgenGroupTacticalResult Analyze(TacticalFixture fixture) =>
        KsProcgenGroupTacticalAnalyzer.Analyze(fixture.Request, fixture.Interior, fixture.Pairs, fixture.Chokes, measureRedundancy: true);
    private static void AssertFailure(KsProcgenGroupTacticalResult result, string code, KsProcgenTacticalStatus status)
    {
        Assert.That(result.Status, Is.EqualTo(status), result.Issue?.Code);
        Assert.That(result.Issue!.Code, Is.EqualTo(code));
        Assert.That(result.Networks, Is.Empty);
        Assert.That(result.MeasurementHash, Is.Zero);
    }
}
