using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenSizeGoalSubdivisionTests
{
    [TestCase(0)]
    [TestCase(1)]
    public void BlockedFirstOrientationRetriesOtherAxisWithoutRefundingWork(int merges)
    {
        var request = SquareRequest();
        var floor = SquareFloor(request);
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, useSizeGoals: true, maximumMergeAttempts: merges);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(Sizes(result), Is.EqualTo(new[] { 13, 18 }));
        Assert.That(result.SizeGoalAttempts.Select(attempt => attempt.AcrossX), Is.EqualTo(new[] { true, false }));
        Assert.That(result.SizeGoalAttempts[0].Status, Is.EqualTo(KsProcgenPartitionStatus.OpenFallback));
        Assert.That(result.SizeGoalAttempts[1].Status, Is.EqualTo(KsProcgenPartitionStatus.Proposed));
        Assert.That(result.MergeAttempts, Is.EqualTo(merges));
        Assert.That(result.SeamProbes, Is.EqualTo(result.SizeGoalAttempts.Sum(attempt => attempt.SeamProbes)));
        Assert.That(result.ExpandedCells, Is.EqualTo(floor.Analysis!.ExpandedCells + floor.FloorCells.Count +
            result.SizeGoalAttempts.Sum(attempt => attempt.ExpandedCells) + result.Analysis!.ExpandedCells));
        Assert.That(result.WallCells.All(cell => cell.Y == 3 && cell.X != 3), Is.True);
        Assert.That(request.Shape.PreservedCells.All(result.FloorCells.Contains), Is.True);
        Assert.That(result.Regions.Single().Partition.DoorOpenings.Single().Threshold, Is.EqualTo(new Vector2i(3, 3)));
        var exact = KsProcgenGroupPartitionPlanner.Plan(request, floor, useSizeGoals: true, maximumMergeAttempts: merges,
            maximumSeamProbes: result.SeamProbes, maximumExpandedCells: result.ExpandedCells);
        Assert.That(exact.PartitionHash, Is.EqualTo(result.PartitionHash));
        Assert.That(exact.SizeGoalAttempts, Is.EqualTo(result.SizeGoalAttempts));
        Assert.Throws<System.NotSupportedException>(() =>
            ((IList<KsProcgenSizeGoalPartitionAttempt>) result.SizeGoalAttempts).Clear());
        request.Shape.Cells.Reverse();
        request.SizeMix.Reverse();
        Assert.That(KsProcgenGroupPartitionPlanner.Plan(request, SquareFloor(request), useSizeGoals: true,
            maximumMergeAttempts: merges).PartitionHash, Is.EqualTo(result.PartitionHash));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SecondOrientationBudgetFailureCannotReuseFirstFallback(bool seamBudget)
    {
        var request = SquareRequest();
        var floor = SquareFloor(request);
        var full = KsProcgenGroupPartitionPlanner.Plan(request, floor, useSizeGoals: true, maximumMergeAttempts: 0);
        var first = full.SizeGoalAttempts[0];
        var limit = seamBudget ? first.SeamProbes + 1
            : floor.Analysis!.ExpandedCells + floor.FloorCells.Count + first.ExpandedCells + 1;
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, useSizeGoals: true, maximumMergeAttempts: 0,
            maximumSeamProbes: seamBudget ? limit : 262_144, maximumExpandedCells: seamBudget ? 262_144 : limit);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(result.SizeGoalAttempts.Count, Is.EqualTo(2));
        Assert.That(result.SizeGoalAttempts[1].Status, Is.EqualTo(KsProcgenPartitionStatus.BudgetExceeded));
        Assert.That(seamBudget ? result.SeamProbes : result.ExpandedCells, Is.EqualTo(limit));
        Assert.That(result.Regions, Is.Empty);
        Assert.That(result.FloorCells, Is.Empty);
        Assert.That(result.PartitionHash, Is.Zero);
    }

    [Test]
    public void TwoBlockedOrientationsRetainFirstFallbackAndAllWork()
    {
        var request = SquareRequest();
        request.Shape.PreservedCells = request.Shape.Cells.ToList();
        var result = KsProcgenGroupPartitionPlanner.Plan(request, SquareFloor(request), useSizeGoals: true, maximumMergeAttempts: 0);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalAttempts.Count, Is.EqualTo(2));
        Assert.That(result.SizeGoalAttempts.All(attempt => attempt.Status == KsProcgenPartitionStatus.OpenFallback), Is.True);
        Assert.That(result.SeamProbes, Is.EqualTo(result.SizeGoalAttempts.Sum(attempt => attempt.SeamProbes)));
        Assert.That(result.SeamProbes, Is.GreaterThan(result.Regions.Single().Partition.SeamProbes));
        Assert.That(result.Regions.Single().Fill.Zones[0].Cells.All(cell => cell.X < 3), Is.True);
        Assert.That(result.WallCells, Is.Empty);
        Assert.That(result.FloorCells.Count, Is.EqualTo(36));
    }

    [Test]
    public void IdenticalSingleRoomOrientationsAreAttemptedOnce()
    {
        var request = SquareRequest();
        request.SizeMix = new List<KsProcgenRoomSizeGoal> { new() { Id = "Whole", MinCells = 36, MaxCells = 36 } };
        var result = KsProcgenGroupPartitionPlanner.Plan(request, SquareFloor(request), useSizeGoals: true);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalAttempts.Count, Is.EqualTo(1));
        Assert.That(Sizes(result), Is.EqualTo(new[] { 36 }));
    }

    private static KsProcgenRequest SquareRequest()
    {
        var request = Request();
        request.Shape.Cells = Enumerable.Range(0, 6).SelectMany(x => Enumerable.Range(0, 6)
            .Select(y => new Vector2i(x, y))).ToList();
        request.Shape.PreservedCells = request.Shape.Cells.Where(cell => cell.X == 3 || cell.Y == 0).ToList();
        request.SizeMix = new List<KsProcgenRoomSizeGoal>
        {
            new() { Id = "Large", MinCells = 18, MaxCells = 18 },
            new() { Id = "Small", MinCells = 13, MaxCells = 13 },
        };
        return request;
    }

    private static KsProcgenGroupFloorResult SquareFloor(KsProcgenRequest request) =>
        KsProcgenGroupFloorPlanner.Plan(request, "Connected", new(request.Shape.PreservedCells.ToHashSet(),
            new HashSet<Vector2i> { new(-1, 0), new(-2, 0) }, new HashSet<Vector2i>(), true, true));

    [TestCase(false)]
    [TestCase(true)]
    public void MixedSizesShareOneRectangleWithUsableCardinalDoors(bool transpose)
    {
        var request = Request(transpose: transpose);
        var floor = Floor(request);
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, useSizeGoals: true, maximumMergeAttempts: 0);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.True);
        var region = result.Regions.Single();
        Assert.That(region.Partition.Status, Is.EqualTo(KsProcgenPartitionStatus.Proposed));
        Assert.That(region.Partition.MergedZoneGroups.All(group => group.Count == 1), Is.True);
        Assert.That(Sizes(result), Is.EqualTo(new[] { 5, 9, 16 }));
        Assert.That(region.Partition.WallCells.Count, Is.EqualTo(6));
        Assert.That(region.Partition.DoorOpenings.Count, Is.EqualTo(2));
        foreach (var door in region.Partition.DoorOpenings)
        {
            Assert.That(result.FloorCells, Does.Contain(door.Threshold));
            Assert.That(result.FloorCells, Does.Contain(door.InsideApproach));
            Assert.That(result.FloorCells, Does.Contain(door.OutsideApproach));
            Assert.That((door.InsideApproach - door.OutsideApproach).LengthSquared, Is.EqualTo(4));
        }
        request.Shape.Cells.Reverse();
        request.SizeMix.Reverse();
        var replay = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), useSizeGoals: true, maximumMergeAttempts: 0);
        Assert.That(replay.PartitionHash, Is.EqualTo(result.PartitionHash));
        var exact = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), useSizeGoals: true,
            maximumMergeAttempts: 0, maximumExpandedCells: result.ExpandedCells, maximumSeamProbes: result.SeamProbes);
        Assert.That(exact.PartitionHash, Is.EqualTo(result.PartitionHash));
        var shortfall = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), useSizeGoals: true,
            maximumMergeAttempts: 0, maximumExpandedCells: result.ExpandedCells - 1);
        Assert.That(shortfall.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(shortfall.ExpandedCells, Is.EqualTo(result.ExpandedCells - 1));
        Assert.That(shortfall.Regions, Is.Empty);
        Assert.That(shortfall.PartitionHash, Is.Zero);
    }

    [Test]
    public void MoreThanTenRoomsKeepSpatialWallOwnershipOrder()
    {
        var request = Request();
        request.Shape.Cells = Enumerable.Range(0, 37).SelectMany(x => Enumerable.Range(0, 4)
            .Select(y => new Vector2i(x, y))).ToList();
        request.SizeMix.RemoveAll(goal => goal.Id == "Small");
        request.SizeMix.Single(goal => goal.Id == "Medium").TargetCount = 11;
        var result = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), useSizeGoals: true, maximumMergeAttempts: 0);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.True);
        Assert.That(Sizes(result), Is.EqualTo(Enumerable.Repeat(9, 11).Append(16)));
        Assert.That(result.Regions.Single().Partition.DoorOpenings.Count, Is.EqualTo(11));
    }

    [Test]
    public void InclusiveBandsAllocateSpareColumnsWithoutOverlappingRoomCategories()
    {
        var request = Request();
        request.Shape.Cells.AddRange(Enumerable.Range(0, 4).Select(y => new Vector2i(9, y)));
        request.SizeMix.Single(goal => goal.Id == "Large").MaxCells = 20;
        var result = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), useSizeGoals: true, maximumMergeAttempts: 0);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.True);
        Assert.That(Sizes(result), Is.EqualTo(new[] { 5, 9, 20 }));
        Assert.That(result.WallCells.Count, Is.EqualTo(6));
    }

    [TestCase(257)]
    [TestCase(4096)]
    public void QuotasBeyondThePartitionZoneCapUseBoundedScalarFallback(int count)
    {
        var request = Request();
        request.SizeMix = new List<KsProcgenRoomSizeGoal> { new() { Id = "Many", MinCells = 4, MaxCells = 8, TargetCount = count } };
        var result = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), preferredMaxRoomCells: 64, useSizeGoals: true);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.False);
        Assert.That(result.Regions.Single().Fill.Zones.Count, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnsupportedFootprintsOrImpossibleBandsRetainScalarFallback(bool irregular)
    {
        var request = Request();
        if (irregular) request.Shape.Cells.Remove(new(8, 3));
        else request.SizeMix.Single(goal => goal.Id == "Small").MaxCells = 6;
        if (!irregular) request.SizeMix.Single(goal => goal.Id == "Small").MinCells = 6;
        var floor = Floor(request);
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64, useSizeGoals: true);
        var legacy = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.False);
        Assert.That(result.PartitionHash, Is.EqualTo(legacy.PartitionHash));
        Assert.That(result.ExpandedCells, Is.EqualTo(legacy.ExpandedCells + floor.FloorCells.Count));
    }

    [Test]
    public void ExplicitZonesBypassAutomaticGoalsAndExhaustedInspectionIsAtomic()
    {
        var request = Request();
        var floor = Floor(request);
        var explicitZones = new[] { new KsProcgenZone("Authored", KsProcgenZoneKind.RoomProposal, floor.FloorCells) };
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, zones: explicitZones, useSizeGoals: true);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.False);
        Assert.That(result.PartitionHash, Is.EqualTo(KsProcgenGroupPartitionPlanner.Plan(request, floor,
            zones: explicitZones).PartitionHash));
        var limit = floor.Analysis!.ExpandedCells + 3;
        var limited = KsProcgenGroupPartitionPlanner.Plan(request, floor, useSizeGoals: true, maximumExpandedCells: limit);
        Assert.That(limited.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(limited.ExpandedCells, Is.EqualTo(limit));
        Assert.That(limited.Issue?.Code, Is.EqualTo("GroupPartitionExpansionBudget"));
        Assert.That(limited.Regions, Is.Empty);
        Assert.That(limited.SizeGoalSubdivisionUsed, Is.False);
    }

    private static int[] Sizes(KsProcgenGroupPartitionResult result) => result.Regions.Single().Fill.Zones
        .Select(zone => zone.Cells.Count(result.FloorCells.Contains)).Order().ToArray();

    private static KsProcgenRequest Request(bool transpose = false) => new()
    {
        RequestId = "MixedRectangle", Seed = 23,
        Shape = new() { Cells = Enumerable.Range(0, transpose ? 4 : 9)
            .SelectMany(x => Enumerable.Range(0, transpose ? 9 : 4).Select(y => new Vector2i(x, y))).ToList() },
        SizeMix = new List<KsProcgenRoomSizeGoal>
        {
            new() { Id = "Small", MinCells = 5, MaxCells = 5 },
            new() { Id = "Large", MinCells = 16, MaxCells = 16 },
            new() { Id = "Medium", MinCells = 9, MaxCells = 9 },
        },
        EntranceDomain = new()
        {
            Entrances = new List<KsProcgenEntranceSpec>
                { new() { PortId = "Entry", ThresholdCells = new List<Vector2i> { new(-1, 0) }, InwardNormal = new(1, 0) } },
            Configurations = new List<KsProcgenEntranceConfigurationSpec> { new() { Id = "Connected",
                Groups = new List<KsProcgenEntranceGroupSpec> { new() { Id = "Room", Ports = new List<string> { "Entry" } } } } },
        },
    };

    private static KsProcgenGroupFloorResult Floor(KsProcgenRequest request) =>
        KsProcgenGroupFloorPlanner.Plan(request, "Connected", new(new HashSet<Vector2i>(),
            new HashSet<Vector2i> { new(-1, 0), new(-2, 0) }, new HashSet<Vector2i>(), true, true));
}
