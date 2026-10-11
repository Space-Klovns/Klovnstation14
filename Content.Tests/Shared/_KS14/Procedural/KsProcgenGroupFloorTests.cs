using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenGroupFloorTests
{
    [Test]
    public void GrowsTwoUsableRegionsWithAContinuousSeparatingWallAndProtectedSeeds()
    {
        var request = Request();
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.FloorCells.Count, Is.EqualTo(20));
        Assert.That(result.WallCells, Is.EquivalentTo(Enumerable.Range(0, 5).Select(x => new Vector2i(x, 2))));
        Assert.That(result.Regions.Count, Is.EqualTo(2));
        Assert.That(result.Regions.Select(region => region.FloorCells.Count), Is.EqualTo(new[] { 10, 10 }));
        Assert.That(result.ProtectedCells, Is.EquivalentTo(new Vector2i[] { new(0, 0), new(0, 4) }));
        Assert.That(result.Regions.All(region => region.ProtectedCells.Count == 1 && region.GroupIds.Count == 1), Is.True);
        Assert.That(result.Regions.SelectMany(region => region.FloorCells), Is.EquivalentTo(result.FloorCells));
        Assert.That(result.Scene!.CleanDomainCells, Is.EquivalentTo(result.FloorCells));
        Assert.That(result.Analysis!.Components.Count, Is.EqualTo(2));
        Assert.That(result.EngineAccessVerified, Is.False);
        Assert.That(result.Routing!.FloorCells.Count, Is.EqualTo(2));
    }

    [Test]
    public void MultiEntranceTreesAndDeclaredRootsRemainProtectedAcrossGrowth()
    {
        var request = Request();
        request.EntranceDomain!.Entrances.Add(new()
        {
            PortId = "A2", ThresholdCells = new List<Vector2i> { new(5, 0) }, InwardNormal = new(-1, 0),
        });
        request.EntranceDomain.Configurations[0].Groups[0].Ports.Add("A2");
        request.EntranceDomain.Configurations[0].Groups[0].RootCells = new List<Vector2i> { new(2, 1) };
        var host = Context().CleanHostCells.ToHashSet();
        host.UnionWith(new Vector2i[] { new(5, 0), new(6, 0) });
        var result = KsProcgenGroupFloorPlanner.Plan(request, "Separate", Context() with { CleanHostCells = host });
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.ProtectedCells, Is.EquivalentTo(result.Routing!.FloorCells));
        Assert.That(result.ProtectedCells.Count, Is.GreaterThan(3));
        Assert.That(result.ProtectedCells, Does.Contain(new Vector2i(2, 1)));
        Assert.That(result.FloorCells, Is.SupersetOf(result.ProtectedCells));
        Assert.That(result.Regions.SelectMany(region => region.ProtectedCells), Is.EquivalentTo(result.ProtectedCells));
        var service = result.Regions.Single(region => region.GroupIds.Contains("A"));
        Assert.That(service.ProtectedCells, Does.Contain(new Vector2i(0, 0)));
        Assert.That(service.ProtectedCells, Does.Contain(new Vector2i(4, 0)));
        Assert.That(service.ProtectedCells, Does.Contain(new Vector2i(2, 1)));
    }

    [Test]
    public void MinimumConnectivityCanGrowAMergedRegionWithBothGroupMemberships()
    {
        var request = Request();
        request.EntranceDomain!.Configurations[0].Mode = KsProcgenEntranceConnectionMode.RequiredConnections;
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.FloorCells.Count, Is.EqualTo(25));
        Assert.That(result.WallCells, Is.Empty);
        Assert.That(result.Regions.Single().GroupIds, Is.EqualTo(new[] { "A", "B" }));
        Assert.That(result.Regions.Single().ProtectedCells.Count, Is.EqualTo(2));
    }

    [Test]
    public void ThinDomainsWorkAndUnreachableWritableIslandsRemainWalls()
    {
        var request = Request();
        request.Shape.Cells = new List<Vector2i> { new(0, 0), new(0, 1), new(0, 2), new(20, 0) };
        request.EntranceDomain!.Entrances.RemoveAt(1);
        request.EntranceDomain.Configurations[0].Groups.RemoveAt(1);
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.FloorCells, Is.EqualTo(new Vector2i[] { new(0, 0), new(0, 1), new(0, 2) }));
        Assert.That(result.WallCells, Is.EqualTo(new Vector2i[] { new(20, 0) }));
        Assert.That(result.Regions.Count, Is.EqualTo(1));
    }

    [Test]
    public void PreservedAndVoidMasksAndEnvelopeNeverGainFloorWrites()
    {
        var request = Request();
        request.EntranceDomain!.Configurations[0].Mode = KsProcgenEntranceConnectionMode.RequiredConnections;
        request.Shape.PreservedCells = new List<Vector2i> { new(0, 0), new(2, 1) };
        request.Shape.VoidCells = new List<Vector2i> { new(2, 3) };
        request.Shape.Cells.Remove(new Vector2i(2, 3));
        request.Shape.EnvelopeCells = new List<Vector2i> { new(8, 8) };
        var context = Context() with { ExistingCleanDomainCells = new HashSet<Vector2i> { new(0, 0) } };
        var result = KsProcgenGroupFloorPlanner.Plan(request, "Separate", context);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.FloorCells.Count, Is.EqualTo(23));
        Assert.That(result.FloorCells, Does.Contain(new Vector2i(0, 0)));
        foreach (var cell in new Vector2i[] { new(2, 1), new(2, 3), new(8, 8) })
        {
            Assert.That(result.FloorCells, Does.Not.Contain(cell));
            Assert.That(result.WallCells, Does.Not.Contain(cell));
        }
        Assert.That(result.ProtectedCells, Does.Contain(new Vector2i(0, 0)));
    }

    [Test]
    public void SealedWritableThresholdsStayBlockedDuringGrowth()
    {
        var request = Request();
        request.Shape.Cells = Enumerable.Range(0, 3).Select(x => new Vector2i(x, 0)).ToList();
        var domain = request.EntranceDomain!;
        domain.Entrances[1].ThresholdCells = new List<Vector2i> { new(2, 0) };
        domain.Entrances[1].InwardNormal = new(-1, 0);
        domain.Entrances[1].OptionalSealable = true;
        domain.Configurations[0].Groups.RemoveAt(1);
        domain.Configurations[0].SealedPorts = new List<string> { "B1" };
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.FloorCells, Is.EqualTo(new Vector2i[] { new(0, 0), new(1, 0) }));
        Assert.That(result.WallCells, Is.EqualTo(new Vector2i[] { new(2, 0) }));
        Assert.That(result.Scene!.BlockedThresholdCells, Does.Contain(new Vector2i(2, 0)));
    }

    [Test]
    public void RequestNetworkGrowthDoesNotAttachToAForeignOwnedHostPassage()
    {
        var request = Request();
        request.EntranceDomain!.Configurations[0].IsolationScope = KsProcgenEntranceIsolationScope.RequestNetwork;
        var host = Context().CleanHostCells.ToHashSet();
        host.Add(new(-1, 5));
        host.UnionWith(Enumerable.Range(0, 6).Select(x => new Vector2i(x, 5)));
        host.UnionWith(Enumerable.Range(0, 6).Select(y => new Vector2i(5, y)));
        var context = Context() with { CleanHostCells = host };
        var separated = KsProcgenGroupFloorPlanner.Plan(request, "Separate", context);
        Assert.That(separated.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), separated.Issue?.Code);
        Assert.That(separated.WallCells, Does.Contain(new Vector2i(4, 0)));
        Assert.That(separated.WallCells, Does.Contain(new Vector2i(4, 1)));
        Assert.That(separated.Analysis!.Ports.Select(port => port.GlobalComponent).Distinct().Count(), Is.EqualTo(2));
        request.EntranceDomain.Configurations[0].IsolationScope = KsProcgenEntranceIsolationScope.DomainLocal;
        var local = KsProcgenGroupFloorPlanner.Plan(request, "Separate", context);
        Assert.That(local.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        Assert.That(local.FloorCells, Does.Contain(new Vector2i(4, 0)));
        Assert.That(local.Analysis!.Ports.All(port => port.GlobalComponent == null), Is.True);
    }

    [Test]
    public void UnattachedHostComponentsCanBeClaimedByOnlyOneExactGroup()
    {
        var request = Request();
        request.EntranceDomain!.Configurations[0].IsolationScope = KsProcgenEntranceIsolationScope.RequestNetwork;
        var host = Context().CleanHostCells.ToHashSet();
        host.UnionWith(Enumerable.Range(1, 3).Select(y => new Vector2i(5, y)));
        var result = KsProcgenGroupFloorPlanner.Plan(request, "Separate", Context() with { CleanHostCells = host });
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(new Vector2i[] { new(4, 1), new(4, 3) }.Count(result.Scene!.CleanDomainCells.Contains), Is.EqualTo(1));
        Assert.That(result.Analysis!.Ports.Select(port => port.GlobalComponent).Distinct().Count(), Is.EqualTo(2));
    }

    [Test]
    public void RoutingGrowthAndFinalAnalysisShareOneAtomicAllowance()
    {
        var request = Request();
        var full = Plan(request);
        var limited = KsProcgenGroupFloorPlanner.Plan(request, "Separate", Context(), maximumExpandedCells: full.ExpandedCells - 1);
        AssertFailure(limited, KsProcgenEntranceSceneStatus.BudgetExceeded);
        Assert.That(limited.ExpandedCells, Is.EqualTo(full.ExpandedCells - 1));
        var exact = KsProcgenGroupFloorPlanner.Plan(request, "Separate", Context(), maximumExpandedCells: full.ExpandedCells);
        Assert.That(exact.FloorHash, Is.EqualTo(full.FloorHash));
        var routing = KsProcgenGroupRoutePlanner.Search(request, "Separate", Context());
        var noGrowth = KsProcgenGroupFloorPlanner.Plan(request, "Separate", Context(), maximumExpandedCells: routing.ExpandedCells);
        AssertFailure(noGrowth, KsProcgenEntranceSceneStatus.BudgetExceeded);
        Assert.That(noGrowth.GrowthProbes, Is.Zero);
    }

    [Test]
    public void WholeConfigurationRetryDoesNotReuseRejectedGeometryOrWorkAllowance()
    {
        var request = Request();
        var first = request.EntranceDomain!.Configurations[0];
        first.IsolationScope = KsProcgenEntranceIsolationScope.RequestNetwork;
        first.Weight = float.MaxValue;
        request.EntranceDomain.Configurations.Add(new() { Id = "Fallback", Weight = float.Epsilon, Groups = first.Groups });
        var host = Context().CleanHostCells.ToHashSet();
        host.UnionWith(Enumerable.Range(0, 5).Select(y => new Vector2i(-2, y)));
        var context = Context() with { CleanHostCells = host };
        var rejected = KsProcgenGroupFloorPlanner.Plan(request, "Separate", context);
        var fallback = KsProcgenGroupFloorPlanner.Plan(request, "Fallback", context);
        AssertFailure(rejected, KsProcgenEntranceSceneStatus.Rejected);
        var selected = KsProcgenGroupFloorPlanner.Select(request, context);
        Assert.That(selected.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        Assert.That(selected.CandidateProbes, Is.EqualTo(2));
        Assert.That(selected.Accepted!.Analysis!.ConfigurationId, Is.EqualTo("Fallback"));
        Assert.That(selected.Accepted.FloorHash, Is.EqualTo(fallback.FloorHash));
        Assert.That(selected.ExpandedCells, Is.EqualTo(rejected.ExpandedCells + fallback.ExpandedCells));
        var limited = KsProcgenGroupFloorPlanner.Select(request, context, maximumExpandedCells: selected.ExpandedCells - 1);
        Assert.That(limited.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(limited.Accepted, Is.Null);
        Assert.That(limited.SelectionHash, Is.Zero);
        Assert.That(limited.ExpandedCells, Is.EqualTo(selected.ExpandedCells - 1));
        Assert.That(KsProcgenGroupFloorPlanner.Select(request, context, maximumCandidateProbes: 1).Issue?.Code,
            Is.EqualTo("GroupFloorCandidateBudget"));
        Assert.That(KsProcgenGroupFloorPlanner.Select(request, context, maximumOrderProbes: 1).Issue?.Code,
            Is.EqualTo("GroupRouteOrderProbeBudget"));
    }

    [Test]
    public void CanonicalDetachedReplayPreservesGroupAndCellOwnership()
    {
        var request = Request();
        var context = Context();
        var original = KsProcgenGroupFloorPlanner.Select(request, context);
        request.Shape.Cells.Reverse();
        request.EntranceDomain!.Entrances.Reverse();
        request.EntranceDomain.Configurations[0].Groups.Reverse();
        var replay = KsProcgenGroupFloorPlanner.Select(request, context, maximumExpandedCells: original.ExpandedCells);
        Assert.That(replay.SelectionHash, Is.EqualTo(original.SelectionHash));
        Assert.That(replay.Accepted!.Regions.Select(region => region.Id), Is.EqualTo(original.Accepted!.Regions.Select(region => region.Id)));
        ((HashSet<Vector2i>) context.CleanHostCells).Clear();
        request.Shape.Cells.Clear();
        Assert.That(original.Accepted.Scene!.CleanHostCells.Count, Is.EqualTo(4));
        Assert.Throws<NotSupportedException>(() => ((IList<Vector2i>) original.Accepted.Regions[0].FloorCells)[0] = new(99, 99));
        Assert.That(original.Accepted.FloorCells.Count, Is.EqualTo(20));
    }

    [Test]
    public void InvalidContextsAndBoundsNeverExposePartialRegions()
    {
        var request = Request();
        AssertFailure(KsProcgenGroupFloorPlanner.Plan(request, "Unknown", Context()), KsProcgenEntranceSceneStatus.InvalidInput);
        AssertFailure(KsProcgenGroupFloorPlanner.Plan(request, "Separate", Context(), maximumExpandedCells: -1), KsProcgenEntranceSceneStatus.InvalidInput);
        request.Mode = KsProcgenMode.Hybrid;
        AssertFailure(Plan(request), KsProcgenEntranceSceneStatus.InvalidInput);
        Assert.That(KsProcgenGroupFloorPlanner.Select(Request(), Context(), maximumCandidateProbes: 65).Status,
            Is.EqualTo(KsProcgenEntranceSceneStatus.InvalidInput));
    }

    private static KsProcgenRequest Request() => new()
    {
        RequestId = "GroupFloor", Seed = 23, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
        Shape = new() { Cells = Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToList() },
        EntranceDomain = new()
        {
            Entrances = new List<KsProcgenEntranceSpec> { new() { PortId = "A1", ThresholdCells = new List<Vector2i> { new(-1, 0) }, InwardNormal = new(1, 0) },
                new() { PortId = "B1", ThresholdCells = new List<Vector2i> { new(-1, 4) }, InwardNormal = new(1, 0) } },
            Configurations = new List<KsProcgenEntranceConfigurationSpec> { new() { Id = "Separate", Groups = new List<KsProcgenEntranceGroupSpec>
                { new() { Id = "A", Ports = new List<string> { "A1" } }, new() { Id = "B", Ports = new List<string> { "B1" } } } } },
        },
    };
    private static KsProcgenGroupRouteContext Context() => new(new HashSet<Vector2i>(),
        new HashSet<Vector2i> { new(-1, 0), new(-2, 0), new(-1, 4), new(-2, 4) }, new HashSet<Vector2i>(), true, true);
    private static KsProcgenGroupFloorResult Plan(KsProcgenRequest request) => KsProcgenGroupFloorPlanner.Plan(request, "Separate", Context());
    private static void AssertFailure(KsProcgenGroupFloorResult result, KsProcgenEntranceSceneStatus status)
    {
        Assert.That(result.Status, Is.EqualTo(status), result.Issue?.Code);
        Assert.That(result.Regions, Is.Empty);
        Assert.That(result.FloorCells, Is.Empty);
        Assert.That(result.WallCells, Is.Empty);
        Assert.That(result.ProtectedCells, Is.Empty);
        Assert.That(result.Routing, Is.Null);
        Assert.That(result.Scene, Is.Null);
        Assert.That(result.Analysis, Is.Null);
        Assert.That(result.FloorHash, Is.Zero);
    }
}
