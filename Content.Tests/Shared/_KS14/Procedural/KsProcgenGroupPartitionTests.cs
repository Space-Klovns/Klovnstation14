using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenGroupPartitionTests
{
    [Test]
    public void AutomaticZonesAndFallbacksRemainInsideEachOwnedNetwork()
    {
        var request = Request();
        var floor = Floor(request);
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 4);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Regions.Count, Is.EqualTo(2));
        Assert.That(result.Analysis!.Components.Count, Is.EqualTo(2));
        Assert.That(result.ProtectedCells, Is.SupersetOf(floor.ProtectedCells));
        Assert.That(result.WallCells, Is.SupersetOf(floor.WallCells));
        Assert.That(result.FloorCells.Concat(result.WallCells), Is.EquivalentTo(request.Shape.Cells));
        foreach (var region in result.Regions)
        {
            Assert.That(region.GroupIds.Count, Is.EqualTo(1));
            Assert.That(region.Fill.Zones.Count, Is.GreaterThan(1));
            Assert.That(region.Fill.Zones.All(zone => zone.Kind == KsProcgenZoneKind.RoomProposal), Is.True);
            Assert.That(region.Partition.FloorCells, Is.SupersetOf(region.ProtectedCells));
            Assert.That(region.Fill.Zones.SelectMany(zone => zone.Cells), Is.EquivalentTo(floor.Regions
                .Single(source => source.GroupIds.SequenceEqual(region.GroupIds)).FloorCells));
        }
        Assert.That(result.EngineAccessVerified, Is.False);
    }

    [Test]
    public void AuthoredRoomSeamKeepsProtectedRouteCellsWithoutReclassifyingTheRoomAsPassage()
    {
        var request = Request();
        var floor = Floor(request);
        var zones = Zones(floor);
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, zones: zones);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Regions.All(region => region.Partition.Status == KsProcgenPartitionStatus.Proposed), Is.True);
        Assert.That(result.Regions.Sum(region => region.Partition.DoorOpenings.Count), Is.EqualTo(2));
        Assert.That(result.Regions.All(region => region.Fill.Zones.All(zone => zone.Kind == KsProcgenZoneKind.RoomProposal)), Is.True);
        Assert.That(result.WallCells.Intersect(floor.ProtectedCells), Is.Empty);
        Assert.That(result.Regions.SelectMany(region => region.Partition.DoorOpenings)
            .All(door => result.FloorCells.Contains(door.Threshold) && result.FloorCells.Contains(door.InsideApproach) &&
                         result.FloorCells.Contains(door.OutsideApproach)), Is.True);
    }

    [Test]
    public void FullyProtectedSeamsFallbackLocallyAndCannotTurnSeparationWallsIntoFloor()
    {
        var request = Request();
        // Multi-entrance trees run along the outer rows and therefore protect both seam owners.
        request.EntranceDomain!.Entrances.Add(new() { PortId = "A2", ThresholdCells = new List<Vector2i> { new(6, 0) }, InwardNormal = new(-1, 0) });
        request.EntranceDomain.Entrances.Add(new() { PortId = "B2", ThresholdCells = new List<Vector2i> { new(6, 4) }, InwardNormal = new(-1, 0) });
        request.EntranceDomain.Configurations[0].Groups[0].Ports.Add("A2");
        request.EntranceDomain.Configurations[0].Groups[1].Ports.Add("B2");
        request.EntranceDomain.Configurations[0].Groups[0].RootCells = new List<Vector2i> { new(3, 0), new(3, 1) };
        request.EntranceDomain.Configurations[0].Groups[1].RootCells = new List<Vector2i> { new(3, 3), new(3, 4) };
        var host = Context().CleanHostCells.ToHashSet();
        host.UnionWith(new Vector2i[] { new(6, 0), new(7, 0), new(6, 4), new(7, 4) });
        var floor = KsProcgenGroupFloorPlanner.Plan(request, "Separate", Context() with { CleanHostCells = host });
        Assert.That(floor.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, maximumMergeAttempts: 0, zones: Zones(floor));
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Regions.All(region => region.Partition.Status == KsProcgenPartitionStatus.OpenFallback), Is.True);
        Assert.That(result.MergeAttempts, Is.Zero);
        Assert.That(result.FloorCells, Is.EquivalentTo(floor.FloorCells));
        Assert.That(result.WallCells, Is.EqualTo(floor.WallCells));
        Assert.That(result.Analysis!.Components.Count, Is.EqualTo(2));
    }

    [Test]
    public void MinimumConnectivityCarriesMergedMembershipThroughPartitioning()
    {
        var request = Request();
        request.EntranceDomain!.Configurations[0].Mode = KsProcgenEntranceConnectionMode.RequiredConnections;
        var result = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), preferredMaxRoomCells: 12);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Regions.Single().GroupIds, Is.EqualTo(new[] { "A", "B" }));
        Assert.That(result.Analysis!.Components.Single().GroupIds, Is.EqualTo(new[] { "A", "B" }));
    }

    [Test]
    public void PreservedFloorCannotBecomeAPartitionWall()
    {
        var request = Request();
        request.Shape.PreservedCells = new List<Vector2i> { new(2, 1) };
        var floor = KsProcgenGroupFloorPlanner.Plan(request, "Separate", Context() with
        {
            ExistingCleanDomainCells = new HashSet<Vector2i> { new(2, 1) },
        });
        // A clean preserved island must initially connect to its entrance tree.
        Assert.That(floor.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Rejected));
        request.Shape.PreservedCells = new List<Vector2i> { new(0, 0) };
        floor = KsProcgenGroupFloorPlanner.Plan(request, "Separate", Context() with
        {
            ExistingCleanDomainCells = new HashSet<Vector2i> { new(0, 0) },
        });
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, zones: Zones(floor));
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.FloorCells, Does.Contain(new Vector2i(0, 0)));
        Assert.That(result.ProtectedCells, Does.Contain(new Vector2i(0, 0)));
        Assert.That(result.WallCells, Does.Not.Contain(new Vector2i(0, 0)));
    }

    [Test]
    public void ThinNetworksRemainPassagesAndWorkBudgetIsAtomicAcrossAllRegions()
    {
        var request = Request();
        request.Shape.Cells = new List<Vector2i> { new(0, 0), new(1, 0), new(2, 0) };
        request.EntranceDomain!.Entrances.RemoveAt(1);
        request.EntranceDomain.Configurations[0].Groups.RemoveAt(1);
        var floor = Floor(request);
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        Assert.That(result.Regions.Single().Fill.Zones.Single().Kind, Is.EqualTo(KsProcgenZoneKind.Passage));
        Assert.That(result.FloorCells.Count, Is.EqualTo(3));
        var limited = KsProcgenGroupPartitionPlanner.Plan(request, floor, maximumExpandedCells: result.ExpandedCells - 1);
        AssertFailure(limited, KsProcgenEntranceSceneStatus.BudgetExceeded);
        Assert.That(limited.ExpandedCells, Is.EqualTo(result.ExpandedCells - 1));
        Assert.That(KsProcgenGroupPartitionPlanner.Plan(request, floor, maximumExpandedCells: result.ExpandedCells).PartitionHash,
            Is.EqualTo(result.PartitionHash));
    }

    [Test]
    public void CrossNetworkOrIncompleteOrDuplicateZonesRejectWithoutFallbackGeometry()
    {
        var request = Request();
        var floor = Floor(request);
        var crossing = new[] { new KsProcgenZone("Cross", KsProcgenZoneKind.RoomProposal, floor.FloorCells) };
        AssertFailure(KsProcgenGroupPartitionPlanner.Plan(request, floor, zones: crossing), KsProcgenEntranceSceneStatus.InvalidInput);
        var zones = Zones(floor);
        AssertFailure(KsProcgenGroupPartitionPlanner.Plan(request, floor, zones: zones.Take(1).ToArray()), KsProcgenEntranceSceneStatus.InvalidInput);
        AssertFailure(KsProcgenGroupPartitionPlanner.Plan(request, floor, zones: zones.Append(zones[0]).ToArray()), KsProcgenEntranceSceneStatus.InvalidInput);
        AssertFailure(KsProcgenGroupPartitionPlanner.Plan(request, floor, maximumMergeAttempts: 65), KsProcgenEntranceSceneStatus.InvalidInput);
        AssertFailure(KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 0), KsProcgenEntranceSceneStatus.InvalidInput);
    }

    [Test]
    public void InputPermutationReplaysAndStaleRequestsCannotReuseFloorArtifacts()
    {
        var request = Request();
        var floor = Floor(request);
        var zones = Zones(floor);
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, zones: zones);
        var reversed = zones.Reverse().Select(zone => zone with { Cells = zone.Cells.Reverse().ToArray() }).ToArray();
        Assert.That(KsProcgenGroupPartitionPlanner.Plan(request, floor, zones: reversed).PartitionHash, Is.EqualTo(result.PartitionHash));
        zones[0] = zones[0] with { Cells = Array.Empty<Vector2i>() };
        Assert.That(result.Regions.SelectMany(region => region.Fill.Zones).All(zone => zone.Cells.Count > 0), Is.True);
        Assert.Throws<NotSupportedException>(() => ((IList<Vector2i>) result.Regions[0].Partition.FloorCells)[0] = new(99, 99));
        request.Seed++;
        AssertFailure(KsProcgenGroupPartitionPlanner.Plan(request, floor), KsProcgenEntranceSceneStatus.InvalidInput);
    }

    [Test]
    public void SeamAndConnectivityWorkAreSharedAcrossNetworksAndReplayAtExactLimits()
    {
        var request = Request();
        var floor = Floor(request);
        var zones = Zones(floor);
        var full = KsProcgenGroupPartitionPlanner.Plan(request, floor, zones: zones);
        Assert.That(full.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        Assert.That(full.SeamProbes, Is.EqualTo(full.Regions.Sum(region => region.Partition.SeamProbes)));
        Assert.That(full.ExpandedCells, Is.EqualTo(floor.Analysis!.ExpandedCells +
            zones.Sum(zone => zone.Cells.Count) + full.Regions.Sum(region => region.Partition.ExpandedCells) +
            full.Analysis!.ExpandedCells));
        Assert.That(full.Regions.All(region => region.Partition.ExpandedCells > 0 && region.Partition.SeamProbes > 0), Is.True);
        var exact = KsProcgenGroupPartitionPlanner.Plan(request, floor, maximumExpandedCells: full.ExpandedCells,
            zones: zones.Reverse().ToArray(), maximumSeamProbes: full.SeamProbes);
        Assert.That(exact.PartitionHash, Is.EqualTo(full.PartitionHash));
        Assert.That(exact.ExpandedCells, Is.EqualTo(full.ExpandedCells));
        Assert.That(exact.SeamProbes, Is.EqualTo(full.SeamProbes));
        var firstAllowance = full.Regions[0].Partition.SeamProbes;
        var shortfall = KsProcgenGroupPartitionPlanner.Plan(request, floor, zones: zones,
            maximumSeamProbes: firstAllowance);
        AssertFailure(shortfall, KsProcgenEntranceSceneStatus.BudgetExceeded);
        Assert.That(shortfall.Issue?.Code, Is.EqualTo("PartitionSeamProbeBudget"));
        Assert.That(shortfall.SeamProbes, Is.EqualTo(firstAllowance));
        Assert.That(shortfall.ExpandedCells, Is.GreaterThan(full.Regions[0].Partition.ExpandedCells));
        var zero = KsProcgenGroupPartitionPlanner.Plan(request, floor, zones: zones, maximumSeamProbes: 0);
        AssertFailure(zero, KsProcgenEntranceSceneStatus.BudgetExceeded);
        Assert.That(zero.Issue?.Code, Is.EqualTo("PartitionSeamProbeBudget"));
        Assert.That(zero.SeamProbes, Is.Zero);
    }

    [Test]
    public void GraphExhaustionDuringSeamValidationCannotBecomeAnOpenFallback()
    {
        var request = Request();
        var floor = Floor(request);
        var zones = Zones(floor);
        var full = KsProcgenGroupPartitionPlanner.Plan(request, floor, zones: zones);
        var beforeSeams = floor.Analysis!.ExpandedCells + zones.Sum(zone => zone.Cells.Count);
        var limit = beforeSeams + 1;
        var shortfall = KsProcgenGroupPartitionPlanner.Plan(request, floor, maximumExpandedCells: limit, zones: zones);
        AssertFailure(shortfall, KsProcgenEntranceSceneStatus.BudgetExceeded);
        Assert.That(shortfall.Issue?.Code, Is.EqualTo("PartitionExpansionBudget"));
        Assert.That(shortfall.ExpandedCells, Is.EqualTo(limit));
        Assert.That(shortfall.SeamProbes, Is.Zero);
        Assert.That(full.PartitionHash, Is.Not.Zero);
    }

    [Test]
    public void InvalidSeamLimitsFailBeforeSceneWork()
    {
        var request = Request();
        var floor = Floor(request);
        foreach (var limit in new[] { -1, 1_000_001 })
        {
            var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, maximumSeamProbes: limit);
            AssertFailure(result, KsProcgenEntranceSceneStatus.InvalidInput);
            Assert.That(result.ExpandedCells + result.SeamProbes, Is.Zero);
        }
    }

    [Test]
    public void SizeGoalHeuristicCannotCombineSeparateEntranceNetworks()
    {
        var request = Request();
        request.SizeMix = new List<KsProcgenRoomSizeGoal>
            { new() { Id = "Room", MinCells = 4, MaxCells = 64, TargetCount = 2 } };
        var floor = Floor(request);
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64, useSizeGoals: true);
        var legacy = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.True);
        Assert.That(result.Regions.Count, Is.EqualTo(2));
        Assert.That(result.Regions.Sum(region => region.Fill.Zones.Count), Is.EqualTo(2));
        Assert.That(result.FloorCells, Is.EqualTo(legacy.FloorCells));
        Assert.That(result.WallCells, Is.EqualTo(legacy.WallCells));
        Assert.That(result.Analysis!.Components.Count, Is.EqualTo(2));
    }

    private static KsProcgenZone[] Zones(KsProcgenGroupFloorResult floor) => floor.Regions.SelectMany(region => new[]
    {
        new KsProcgenZone(region.Id + "/Left", KsProcgenZoneKind.RoomProposal, region.FloorCells.Where(cell => cell.X < 3).ToArray()),
        new KsProcgenZone(region.Id + "/Right", KsProcgenZoneKind.RoomProposal, region.FloorCells.Where(cell => cell.X >= 3).ToArray()),
    }).ToArray();

    private static KsProcgenRequest Request() => new()
    {
        RequestId = "GroupPartitions", Seed = 23, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
        Shape = new() { Cells = Enumerable.Range(0, 6).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToList() },
        EntranceDomain = new()
        {
            Entrances = new List<KsProcgenEntranceSpec>
            {
                new() { PortId = "A1", ThresholdCells = new List<Vector2i> { new(-1, 0) }, InwardNormal = new(1, 0) },
                new() { PortId = "B1", ThresholdCells = new List<Vector2i> { new(-1, 4) }, InwardNormal = new(1, 0) },
            },
            Configurations = new List<KsProcgenEntranceConfigurationSpec> { new() { Id = "Separate", Groups = new List<KsProcgenEntranceGroupSpec>
                { new() { Id = "A", Ports = new List<string> { "A1" } }, new() { Id = "B", Ports = new List<string> { "B1" } } } } },
        },
    };
    private static KsProcgenGroupRouteContext Context() => new(new HashSet<Vector2i>(),
        new HashSet<Vector2i> { new(-1, 0), new(-2, 0), new(-1, 4), new(-2, 4) }, new HashSet<Vector2i>(), true, true);
    private static KsProcgenGroupFloorResult Floor(KsProcgenRequest request) => KsProcgenGroupFloorPlanner.Plan(request, "Separate", Context());
    private static void AssertFailure(KsProcgenGroupPartitionResult result, KsProcgenEntranceSceneStatus status)
    {
        Assert.That(result.Status, Is.EqualTo(status), result.Issue?.Code);
        Assert.That(result.Regions, Is.Empty);
        Assert.That(result.FloorCells, Is.Empty);
        Assert.That(result.WallCells, Is.Empty);
        Assert.That(result.ProtectedCells, Is.Empty);
        Assert.That(result.Scene, Is.Null);
        Assert.That(result.Analysis, Is.Null);
        Assert.That(result.PartitionHash, Is.Zero);
    }
}
