#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenUtilityRoomTests
{
    [TestCase(3, 6, 1, 2)] [TestCase(6, 3, 2, 1)] [TestCase(6, 6, 2, 2)]
    public void ActualInteriorAndSingleEntranceSurviveOrdinarySeamSolving(int width, int height, int roomWidth, int roomHeight)
    {
        var request = Request(width, height); var floor = Floor(request);
        var result = Plan(request, floor, roomWidth, roomHeight);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        var network = result.Regions.Single(); var zone = network.Fill.Zones.Single(zone => zone.UtilityRoom != null);
        var utility = zone.UtilityRoom!;
        Assert.That(utility.Size, Is.EqualTo(new Vector2i(roomWidth, roomHeight)));
        Assert.That(utility.InteriorCells.Count, Is.EqualTo(roomWidth * roomHeight));
        Assert.That(network.Partition.FloorCells, Is.SupersetOf(utility.InteriorCells.Append(utility.Threshold)));
        Assert.That(network.Partition.WallCells, Is.SupersetOf(utility.EnclosureCells.Where(cell => cell != utility.Threshold)));
        Assert.That(network.Partition.DoorOpenings.Count(door => door.FirstZoneId == zone.Id || door.SecondZoneId == zone.Id), Is.EqualTo(1));
        var door = network.Partition.DoorOpenings.Single(door => door.Threshold == utility.Threshold);
        Assert.That(door.InsideApproach, Is.EqualTo(utility.InsideApproach));
        Assert.That(door.OutsideApproach, Is.EqualTo(utility.OutsideApproach));
        Assert.That(network.Partition.MergedZoneGroups.Any(group => group.Contains(zone.Id)), Is.False);
        Assert.That(result.FloorCells.Intersect(result.WallCells), Is.Empty);
        Assert.That(result.FloorCells.Concat(result.WallCells), Is.EquivalentTo(request.Shape.Cells));
        Assert.That(result.Analysis!.Components.Count, Is.EqualTo(1));
        Assert.That(result.ProtectedCells, Is.SupersetOf(floor.ProtectedCells));
        Assert.That(result.EngineAccessVerified, Is.False);
    }

    [Test]
    public void SeparateIslandsReceiveSeparateClosetsWithoutChangingEntranceGroups()
    {
        var request = Request(6, 6);
        request.Shape.Cells.AddRange(request.Shape.Cells.Select(cell => cell + new Vector2i(10, 0)).ToArray());
        request.EntranceDomain = new();
        request = Capture(request); var floor = Floor(request);
        var result = Plan(request, floor, 1, 2);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Regions.Count, Is.EqualTo(2));
        Assert.That(result.Analysis!.Components.Count, Is.EqualTo(2));
        Assert.That(result.Regions.All(network => network.Fill.Zones.Count(zone => zone.UtilityRoom != null) == 1), Is.True);
        Assert.That(result.Regions.SelectMany(network => network.GroupIds), Is.EquivalentTo(floor.Regions.SelectMany(network => network.GroupIds)));
        Assert.That(result.Regions.All(network => network.Fill.Zones.SelectMany(zone => zone.Cells)
            .All(cell => floor.Regions.Single(source => source.GroupIds.SequenceEqual(network.GroupIds)).FloorCells.Contains(cell))), Is.True);
    }

    [Test]
    public void CanonicalReplayAndExactSharedAllowancesIncludeAllocationWork()
    {
        var request = Request(6, 6); var floor = Floor(request); var first = Plan(request, floor, 1, 2);
        Assert.That(first.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), first.Issue?.Code);
        var exact = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64,
            maximumExpandedCells: first.ExpandedCells, maximumSeamProbes: first.SeamProbes, electricalDistribution: Distribution(1, 2));
        Assert.That(exact.PartitionHash, Is.EqualTo(first.PartitionHash));
        Assert.That(exact.ExpandedCells, Is.EqualTo(first.ExpandedCells));
        Assert.That(exact.SeamProbes, Is.EqualTo(first.SeamProbes));
        request.Shape.Cells.Reverse();
        Assert.That(Plan(request, Floor(request), 1, 2).PartitionHash, Is.EqualTo(first.PartitionHash));
        var utility = first.Regions.Single().Fill.Zones.Single(zone => zone.UtilityRoom != null).UtilityRoom!;
        Assert.Throws<NotSupportedException>(() => ((IList<Vector2i>) utility.InteriorCells).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Vector2i>) utility.EnclosureCells).Clear());
    }

    [TestCase(false)] [TestCase(true)]
    public void ExhaustedAllocationOrFinalAuditCannotLeakPartialRooms(bool seam)
    {
        var request = Request(6, 6); var floor = Floor(request); var complete = Plan(request, floor, 1, 2);
        Assert.That(complete.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), complete.Issue?.Code);
        var limit = seam ? complete.SeamProbes - 1 : complete.ExpandedCells - 1;
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64,
            maximumExpandedCells: seam ? 262_144 : limit, maximumSeamProbes: seam ? limit : 262_144,
            electricalDistribution: Distribution(1, 2));
        Failure(result, KsProcgenEntranceSceneStatus.BudgetExceeded);
        Assert.That(seam ? result.SeamProbes : result.ExpandedCells, Is.LessThanOrEqualTo(limit));
    }

    [TestCase(1)] [TestCase(3)]
    public void TinyPureDomainsRemainRepresentableButCannotInventMandatoryClosets(int length)
    {
        var request = Request(length, 1); var floor = Floor(request);
        Assert.That(KsProcgenGroupPartitionPlanner.Plan(request, floor).Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        var result = Plan(request, floor, 1, 2);
        Failure(result, KsProcgenEntranceSceneStatus.Rejected);
        Assert.That(result.Issue?.Code, Is.EqualTo("UtilityRoomNoPlacement"));
    }

    [Test]
    public void ProtectedInteriorAndReadonlyGeometryCannotBeTakenForAnEnclosure()
    {
        var request = Request(6, 6); var floor = Floor(request);
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Code);
        var source = new[] { new KsProcgenZone("Ordinary", KsProcgenZoneKind.RoomProposal, floor.FloorCells) };
        var work = new KsProcgenPartitionWorkBudget();
        Assert.That(KsProcgenUtilityRoomAllocator.TryAllocate("Network", source, shape!, floor.FloorCells.ToHashSet(),
            Distribution(1, 2), work, out var zones, out issue), Is.False);
        Assert.That(zones, Is.Empty); Assert.That(issue?.Code, Is.EqualTo("UtilityRoomNoPlacement"));
        request.Shape.PreservedCells = request.Shape.Cells.ToList();
        Assert.That(KsProcgenGeometry.TryNormalize(request, out shape, out issue), Is.True, issue?.Code);
        work = new();
        Assert.That(KsProcgenUtilityRoomAllocator.TryAllocate("Network", source, shape!, new HashSet<Vector2i>(),
            Distribution(1, 2), work, out zones, out issue), Is.False);
        Assert.That(zones, Is.Empty); Assert.That(issue?.Code, Is.EqualTo("UtilityRoomNoPlacement"));
    }

    [TestCase(false)] [TestCase(true)]
    public void RequiredEnclosuresCannotUseMergeOrOpenFallbackWhenTheirWallIsProtected(bool mergeBudget)
    {
        var request = Request(6, 6); var complete = Plan(request, Floor(request), 1, 2);
        var network = complete.Regions.Single(); var utility = network.Fill.Zones.Single(zone => zone.UtilityRoom != null).UtilityRoom!;
        var wall = utility.EnclosureCells.First(cell => cell != utility.Threshold);
        var result = KsProcgenPartitionPlanner.Plan(network.Fill, new HashSet<Vector2i>(),
            maxMergeAttempts: mergeBudget ? 0 : 256, protectedFloorCells: new HashSet<Vector2i> { wall },
            workBudget: new());
        Assert.That(result.Status, Is.EqualTo(KsProcgenPartitionStatus.RequiredRoomRejected));
        Assert.That(result.FloorCells.Concat(result.WallCells), Is.Empty);
        Assert.That(result.DoorOpenings, Is.Empty); Assert.That(result.MergedZoneGroups, Is.Empty);
    }

    [Test]
    public void MalformedReservationCoverageIsRejectedBeforeAnyReusablePartition()
    {
        var request = Request(6, 6); var result = Plan(request, Floor(request), 1, 2); var source = result.Regions.Single().Fill;
        var zones = source.Zones.Select(zone => zone.UtilityRoom == null ? zone : zone with { Cells = zone.Cells.Skip(1).ToArray() }).ToArray();
        var rejected = KsProcgenPartitionPlanner.Plan(new() { Status = KsProcgenPureFillStatus.Proposed, Zones = zones,
            ProceduralCells = zones.Sum(zone => zone.Cells.Count) }, new HashSet<Vector2i>(), workBudget: new());
        Assert.That(rejected.Status, Is.EqualTo(KsProcgenPartitionStatus.InvalidInput));
        Assert.That(rejected.FloorCells.Concat(rejected.WallCells), Is.Empty);
    }

    [TestCase(0)] [TestCase(256)]
    public void OrdinaryRepairBudgetCannotOpenTheMandatoryCloset(int mergeLimit)
    {
        var request = Request(6, 6); var complete = Plan(request, Floor(request), 1, 2);
        var original = complete.Regions.Single().Fill;
        var utilityZone = original.Zones.Single(zone => zone.UtilityRoom != null);
        var outside = original.Zones.Where(zone => zone.UtilityRoom == null).SelectMany(zone => zone.Cells).ToHashSet();
        var zones = new[]
        {
            new KsProcgenZone("OutsideLeft", KsProcgenZoneKind.RoomProposal, outside.Where(cell => cell.X < 3).OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray()),
            new KsProcgenZone("OutsideRight", KsProcgenZoneKind.RoomProposal, outside.Where(cell => cell.X >= 3).OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray()),
            utilityZone,
        };
        var fill = new KsProcgenPureFillResult { Status = KsProcgenPureFillStatus.Proposed, Zones = zones, ProceduralCells = original.ProceduralCells };
        var result = KsProcgenPartitionPlanner.Plan(fill, new HashSet<Vector2i>(), maxMergeAttempts: mergeLimit,
            protectedFloorCells: outside, workBudget: new());
        if (mergeLimit == 0)
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenPartitionStatus.RequiredRoomRejected));
            Assert.That(result.Issue?.Code, Is.EqualTo("UtilityRoomOpenFallbackForbidden"));
            Assert.That(result.FloorCells.Concat(result.WallCells), Is.Empty);
            Assert.That(result.DoorOpenings, Is.Empty);
        }
        else
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenPartitionStatus.MergedFallback), result.Issue?.Code);
            Assert.That(result.MergedZoneGroups.Single(), Is.EquivalentTo(new[] { "OutsideLeft", "OutsideRight" }));
            Assert.That(result.WallCells, Is.EquivalentTo(utilityZone.UtilityRoom!.EnclosureCells.Where(cell => cell != utilityZone.UtilityRoom.Threshold)));
            Assert.That(result.FloorCells, Is.SupersetOf(outside));
            Assert.That(result.DoorOpenings.Single().Threshold, Is.EqualTo(utilityZone.UtilityRoom.Threshold));
        }
    }

    [Test]
    public void FrozenClosetThemeChangesPartitionIdentityEvenWhenGeometryIsIdentical()
    {
        var request = Request(6, 6); var floor = Floor(request);
        var first = Plan(request, floor, 1, 2);
        var changed = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64,
            electricalDistribution: Distribution(1, 2, theme: "OtherElectricalCloset"));
        Assert.That(first.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), first.Issue?.Code);
        Assert.That(changed.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), changed.Issue?.Code);
        Assert.That(changed.FloorCells, Is.EqualTo(first.FloorCells));
        Assert.That(changed.WallCells, Is.EqualTo(first.WallCells));
        Assert.That(changed.PartitionHash, Is.Not.EqualTo(first.PartitionHash));
    }

    [Test]
    public void ReservationOrdinalsSelectDistinctCanonicalCompleteCandidates()
    {
        var request = Request(6, 6); var first = Allocate(request, Distribution(1, 2), 0, 0, out _);
        var next = Allocate(request, Distribution(1, 2), 1, 0, out _);
        Assert.That(next.Threshold, Is.Not.EqualTo(first.Threshold));
        request.Shape.Cells.Reverse();
        var replay = Allocate(request, Distribution(1, 2), 1, 0, out _);
        Assert.That(replay.Minimum, Is.EqualTo(next.Minimum)); Assert.That(replay.Threshold, Is.EqualTo(next.Threshold));
        Assert.That(replay.InteriorCells, Is.EqualTo(next.InteriorCells));
    }

    [TestCase(6, 6, 4)] [TestCase(3, 6, 2)]
    public void ComfortableInteriorPreferenceStillFitsTheAvailableShape(int width, int height, int expectedArea)
    {
        Assert.That(KsProcgenElectricalProfiles.TrySnapshot(new() { SchemaVersion = 2, Distribution = new()
            { ClosetInteriorSizes = new() { new(1, 2), new(2, 2) } } }, out var profile, out var issue), Is.True, issue?.Code);
        var room = Allocate(Request(width, height), profile!.Distribution!, 0, 3, out _);
        Assert.That(room.InteriorCells.Count, Is.EqualTo(expectedArea));
    }

    [Test]
    public void SkippedReservationsConsumeExactSharedWorkAndCannotLeakOnShortfall()
    {
        var request = Request(6, 6); var room = Allocate(request, Distribution(1, 2), 1, 0, out var complete);
        var exact = Allocate(request, Distribution(1, 2), 1, 0, out var replay,
            work: new(maximumExpandedCells: complete.ExpandedCells, maximumSeamProbes: complete.SeamProbes));
        Assert.That(exact.Threshold, Is.EqualTo(room.Threshold)); Assert.That(replay.SeamProbes, Is.EqualTo(complete.SeamProbes));
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Code);
        var source = new[] { new KsProcgenZone("Ordinary", KsProcgenZoneKind.RoomProposal, Floor(request).FloorCells) };
        var limited = new KsProcgenPartitionWorkBudget(maximumSeamProbes: complete.SeamProbes - 1);
        Assert.That(KsProcgenUtilityRoomAllocator.TryAllocate("Network", source, shape!, Floor(request).ProtectedCells,
            Distribution(1, 2), limited, out var zones, out issue, candidateOrdinal: 1), Is.False);
        Assert.That(zones, Is.Empty); Assert.That(issue!.Code, Is.EqualTo("UtilityRoomAllocationBudget"));
        Assert.That(limited.SeamProbes, Is.EqualTo(limited.MaximumSeamProbes));
    }

    [TestCase(-1)] [TestCase(65536)]
    public void InvalidReservationOrdinalCannotProduceGeometry(int ordinal)
    {
        var request = Request(6, 6); var floor = Floor(request);
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Code);
        Assert.That(KsProcgenUtilityRoomAllocator.TryAllocate("Network", new[] { new KsProcgenZone("Ordinary", KsProcgenZoneKind.RoomProposal, floor.FloorCells) },
            shape!, floor.ProtectedCells, Distribution(1, 2), new(), out var zones, out issue, candidateOrdinal: ordinal), Is.False);
        Assert.That(zones, Is.Empty); Assert.That(issue!.Code, Is.EqualTo("InvalidUtilityRoomAllocation"));
    }

    [Test]
    public void IndependentNetworksCanChooseDifferentReservationOrdinals()
    {
        var request = Request(6, 6); request.Shape.Cells.AddRange(request.Shape.Cells.Select(cell => cell + new Vector2i(10, 0)).ToArray());
        request.EntranceDomain = new(); request = Capture(request); var floor = Floor(request); var first = Plan(request, floor, 1, 2);
        var networks = first.Regions.OrderBy(region => region.Id, StringComparer.Ordinal).ToArray();
        var choices = new Dictionary<string, int> { [networks[1].Id] = 1 };
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64,
            electricalDistribution: Distribution(1, 2), utilityRoomOrdinals: choices);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        var unchanged = result.Regions.Single(region => region.Id == networks[0].Id).Fill.Zones.Single(zone => zone.UtilityRoom != null).UtilityRoom!;
        var changed = result.Regions.Single(region => region.Id == networks[1].Id).Fill.Zones.Single(zone => zone.UtilityRoom != null).UtilityRoom!;
        Assert.That(unchanged.Threshold, Is.EqualTo(networks[0].Fill.Zones.Single(zone => zone.UtilityRoom != null).UtilityRoom!.Threshold));
        Assert.That(changed.Threshold, Is.Not.EqualTo(networks[1].Fill.Zones.Single(zone => zone.UtilityRoom != null).UtilityRoom!.Threshold));
        Assert.That(result.UtilityRoomChoices.Single(choice => choice.NetworkId == networks[0].Id).Ordinal, Is.Zero);
        Assert.That(result.UtilityRoomChoices.Single(choice => choice.NetworkId == networks[1].Id).Ordinal, Is.EqualTo(1));
        choices.Clear(); Assert.That(result.UtilityRoomChoices.Count, Is.EqualTo(2));
    }

    [Test]
    public void UnknownNetworkChoiceAndUnavailableOrdinalExposeNoReusableRooms()
    {
        var request = Request(6, 6); var floor = Floor(request);
        var unknown = KsProcgenGroupPartitionPlanner.Plan(request, floor, electricalDistribution: Distribution(1, 2),
            utilityRoomOrdinals: new Dictionary<string, int> { ["Unknown"] = 1 });
        Failure(unknown, KsProcgenEntranceSceneStatus.InvalidInput);
        var exhausted = KsProcgenGroupPartitionPlanner.Plan(request, floor, electricalDistribution: Distribution(1, 2),
            utilityRoomOrdinals: new Dictionary<string, int> { [request.RequestId + "/EntranceNetwork/0"] = 65535 });
        Failure(exhausted, KsProcgenEntranceSceneStatus.Rejected);
        Assert.That(exhausted.Issue!.Code, Is.EqualTo("UtilityRoomNoPlacement"));
        Assert.That(exhausted.UtilityRoomChoices.Single().Allocated, Is.False);
    }

    [TestCase(1, 4, 1, 2)] [TestCase(4, 1, 2, 1)] [TestCase(4, 4, 2, 2)]
    public void GeneratedHullReuseKeepsARealRoomAndItsDoorInsideTheTarget(int width, int height, int roomWidth, int roomHeight)
    {
        var request = WithEnvelope(width, height); var floor = Floor(request);
        var result = Plan(request, floor, roomWidth, roomHeight);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        var room = result.Regions.Single().Fill.Zones.Single(zone => zone.UtilityRoom != null).UtilityRoom!;
        Assert.That(room.ReusedHullCells, Is.Not.Empty);
        Assert.That(room.InteriorCells.Count, Is.EqualTo(roomWidth * roomHeight));
        Assert.That(result.FloorCells, Is.SupersetOf(room.InteriorCells.Append(room.Threshold).Append(room.OutsideApproach)));
        Assert.That(result.WallCells, Is.SupersetOf(room.PartitionEnclosureCells.Where(cell => cell != room.Threshold)));
        Assert.That(room.ReusedHullCells.Intersect(result.FloorCells.Concat(result.WallCells)), Is.Empty);
        Assert.That(result.FloorCells.Concat(result.WallCells), Is.EquivalentTo(request.Shape.Cells));
        Assert.That(room.ReusedHullCells, Is.SubsetOf(request.Shape.EnvelopeCells));
        Assert.That(room.ReusedHullCells, Does.Not.Contain(room.Threshold));
        Assert.Throws<NotSupportedException>(() => ((IList<Vector2i>) room.ReusedHullCells).Clear());
        var boundary = KsProcgenGroupHullBoundaryAnalyzer.Analyze(request, result);
        Assert.That(boundary.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), boundary.Issue?.Code);
        Assert.That(boundary.RequiredEnvelopeCells, Is.SupersetOf(room.ReusedHullCells));
        var corners = room.ReusedHullCells.Where(cell => !boundary.Edges.Any(edge => edge.BoundaryCell == cell)).ToArray();
        Assert.That(corners, Is.Not.Empty, "The complete enclosure must retain corners without cardinal exposure.");
        Assert.That(corners, Is.SubsetOf(boundary.RequiredEnvelopeCells));
        var exact = KsProcgenGroupHullBoundaryAnalyzer.Analyze(request, result, maximumExpandedCells: boundary.ExpandedCells);
        Assert.That(exact.BoundaryHash, Is.EqualTo(boundary.BoundaryHash));
        var shortfall = KsProcgenGroupHullBoundaryAnalyzer.Analyze(request, result, maximumExpandedCells: boundary.ExpandedCells - 1);
        Assert.That(shortfall.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(shortfall.RequiredEnvelopeCells, Is.Empty); Assert.That(shortfall.BoundaryHash, Is.Zero);
        request.Shape.Cells.Reverse(); request.Shape.EnvelopeCells.Reverse();
        Assert.That(Plan(request, Floor(request), roomWidth, roomHeight).PartitionHash, Is.EqualTo(result.PartitionHash));
    }

    [TestCase("Missing")] [TestCase("Preserved")] [TestCase("OtherTarget")] [TestCase("Void")]
    public void ReuseCannotInventMissingCornersOrBorrowNonEnvelopeGeometry(string obstruction)
    {
        var request = WithEnvelope(1, 4); var originalFloor = Floor(request); var corner = new Vector2i(-1, 4);
        request.Shape.EnvelopeCells.Remove(corner);
        if (obstruction is "OtherTarget" or "Preserved") request.Shape.Cells.Add(corner);
        if (obstruction == "Preserved") request.Shape.PreservedCells.Add(corner);
        if (obstruction == "Void") request.Shape.VoidCells.Add(corner);
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Code);
        var source = new[] { new KsProcgenZone("Ordinary", KsProcgenZoneKind.RoomProposal, originalFloor.FloorCells) };
        Assert.That(KsProcgenUtilityRoomAllocator.TryAllocate("Network", source, shape!, originalFloor.ProtectedCells,
            Distribution(1, 2), new(), out var zones, out issue), Is.False);
        Assert.That(issue!.Code, Is.EqualTo("UtilityRoomNoPlacement")); Assert.That(zones, Is.Empty);
    }

    [Test]
    public void EnvelopeDoesNotMakeAThreeTileStripFitAMandatoryTwoTileCloset()
    {
        var request = WithEnvelope(1, 3); var result = Plan(request, Floor(request), 1, 2);
        Failure(result, KsProcgenEntranceSceneStatus.Rejected);
        Assert.That(result.Issue!.Code, Is.EqualTo("UtilityRoomNoPlacement"));
    }

    [Test]
    public void ReusedHullCannotAlsoBeOwnedByAnOrdinaryPartitionZone()
    {
        var request = WithEnvelope(4, 4); var complete = Plan(request, Floor(request), 2, 2);
        Assert.That(complete.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), complete.Issue?.Code);
        var fill = complete.Regions.Single().Fill;
        var ordinary = fill.Zones.First(zone => zone.UtilityRoom == null);
        var directions = new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) };
        var owned = fill.Zones.SelectMany(zone => zone.Cells).ToHashSet();
        var bridge = fill.Zones.Single(zone => zone.UtilityRoom != null).UtilityRoom!.ReusedHullCells
            .SelectMany(cell => directions.Select(step => (Cell: cell, Neighbor: cell + step)))
            .First(pair => !owned.Contains(pair.Neighbor) && directions.Any(step => ordinary.Cells.Contains(pair.Neighbor + step)));
        var zones = fill.Zones.Select(zone => zone.Id == ordinary.Id ?
            zone with { Cells = zone.Cells.Append(bridge.Neighbor).Append(bridge.Cell).ToArray() } : zone).ToArray();
        var rejected = KsProcgenPartitionPlanner.Plan(new() { Status = KsProcgenPureFillStatus.Proposed,
            Zones = zones, ProceduralCells = zones.Sum(zone => zone.Cells.Count) }, new HashSet<Vector2i>(), workBudget: new());
        Assert.That(rejected.Status, Is.EqualTo(KsProcgenPartitionStatus.InvalidInput));
        Assert.That(rejected.Issue!.Code, Is.EqualTo("UtilityRoomHullOverlapsPartition"));
        Assert.That(rejected.FloorCells.Concat(rejected.WallCells), Is.Empty);
    }

    [Test]
    public void PreservedEnvelopeDeclarationsAreRejectedByTheExistingShapeContract()
    {
        var request = WithEnvelope(1, 4); request.Shape.PreservedCells.Add(new(-1, 4));
        Assert.That(KsProcgenGeometry.TryNormalize(request, out _, out var issue), Is.False);
        Assert.That(issue!.Code, Is.EqualTo("PreservedOutsideTarget"));
    }

    private static KsProcgenRequest WithEnvelope(int width, int height)
    {
        var request = Request(width, height);
        request.Shape.EnvelopeCells = Enumerable.Range(-1, width + 2).SelectMany(x => Enumerable.Range(-1, height + 2)
            .Select(y => new Vector2i(x, y))).Where(cell => !request.Shape.Cells.Contains(cell)).ToList();
        return Capture(request);
    }

    private static KsProcgenUtilityRoomReservation Allocate(KsProcgenRequest request, KsProcgenElectricalDistribution distribution,
        int ordinal, int preferred, out KsProcgenPartitionWorkBudget used, KsProcgenPartitionWorkBudget? work = null)
    {
        var floor = Floor(request); used = work ?? new();
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Code);
        Assert.That(KsProcgenUtilityRoomAllocator.TryAllocate("Network", new[] { new KsProcgenZone("Ordinary", KsProcgenZoneKind.RoomProposal, floor.FloorCells) },
            shape!, floor.ProtectedCells, distribution, used, out var zones, out issue,
            candidateOrdinal: ordinal, preferredInteriorCells: preferred), Is.True, issue?.Code);
        return zones.Single(zone => zone.UtilityRoom != null).UtilityRoom!;
    }

    private static KsProcgenElectricalDistribution Distribution(int width, int height, string theme = "KsProcgenElectricalCloset")
    {
        Assert.That(KsProcgenElectricalProfiles.TrySnapshot(new() { SchemaVersion = 2, Distribution = new()
            { ClosetInteriorSizes = new() { new(width, height) }, ClosetTheme = theme } }, out var profile, out var issue), Is.True, issue?.Code);
        return profile!.Distribution!;
    }
    private static KsProcgenRequest Capture(KsProcgenRequest source)
    {
        Assert.That(KsProcgenRequestSnapshot.TryCapture(source, out var request, out var issue), Is.True, issue?.Code);
        return request!.CopyRequest();
    }
    private static KsProcgenRequest Request(int width, int height) => Capture(new()
    {
        RequestId = "UtilityGeometry", ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
        EntranceDomain = new(),
        Shape = new() { Cells = Enumerable.Range(0, width).SelectMany(x => Enumerable.Range(0, height).Select(y => new Vector2i(x, y))).ToList() },
    });
    private static KsProcgenGroupFloorResult Floor(KsProcgenRequest request)
    {
        var result = KsProcgenGroupFloorPlanner.Plan(request, request.EntranceDomain!.Configurations.Single().Id,
            new(new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true));
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        return result;
    }
    private static KsProcgenGroupPartitionResult Plan(KsProcgenRequest request, KsProcgenGroupFloorResult floor, int width, int height) =>
        KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64, electricalDistribution: Distribution(width, height));
    private static void Failure(KsProcgenGroupPartitionResult result, KsProcgenEntranceSceneStatus status)
    {
        Assert.That(result.Status, Is.EqualTo(status), result.Issue?.Code);
        Assert.That(result.Regions, Is.Empty); Assert.That(result.FloorCells.Concat(result.WallCells), Is.Empty);
        Assert.That(result.Scene, Is.Null); Assert.That(result.Analysis, Is.Null); Assert.That(result.PartitionHash, Is.Zero);
    }
}
