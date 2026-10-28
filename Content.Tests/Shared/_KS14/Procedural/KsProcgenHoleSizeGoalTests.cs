using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenHoleSizeGoalTests
{
    [TestCase(false)] [TestCase(true)]
    public void CourtyardUsesConnectedRoomsAndDisjointBoundarySpans(bool transpose)
    {
        var request = KsProcgenHoleFootprintFixtures.Create(transpose: transpose);
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.True);
        Assert.That(Sizes(result), Is.EqualTo(new[] { 28, 33 }));
        Assert.That(result.WallCells.Count, Is.EqualTo(3));
        Assert.That(result.FloorCells.Concat(result.WallCells), Is.EquivalentTo(request.Shape.Cells));
        Assert.That(result.FloorCells.Concat(result.WallCells).Intersect(request.Shape.VoidCells), Is.Empty);
        var door = result.Regions.Single().Partition.DoorOpenings.Single();
        Assert.That(new[] { door.Threshold, door.InsideApproach, door.OutsideApproach }.All(result.FloorCells.Contains), Is.True);
        Assert.That((door.InsideApproach - door.OutsideApproach).LengthSquared, Is.EqualTo(4));
        Assert.That(result.SizeGoalAttempts.Last().AcrossX, Is.EqualTo(!transpose));
    }

    [TestCase(false)] [TestCase(true)]
    public void MultipleCourtyardsFitLargeMediumAndSmallRoomsTogether(bool transpose)
    {
        var request = KsProcgenHoleFootprintFixtures.Create(transpose: transpose, threeBands: true);
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.True);
        Assert.That(Sizes(result), Is.EqualTo(new[] { 16, 23, 35 }));
        Assert.That(result.WallCells.Count, Is.EqualTo(12));
        Assert.That(result.Regions.Single().Partition.DoorOpenings.Count, Is.EqualTo(2));
        Assert.That(result.FloorCells.Concat(result.WallCells), Is.EquivalentTo(request.Shape.Cells));
        request.Shape.Cells.Reverse(); request.Shape.VoidCells.Reverse(); request.SizeMix.Reverse();
        Assert.That(Plan(request).PartitionHash, Is.EqualTo(result.PartitionHash));
    }

    [Test]
    public void OneFootprintFitsALargeRoomAMediumRoomAndSeveralSmallRooms()
    {
        var request = KsProcgenHoleFootprintFixtures.Create(threeBands: true, smallRoomCount: 3);
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.True);
        Assert.That(Sizes(result), Is.EqualTo(new[] { 16, 16, 16, 23, 35 }));
        Assert.That(result.WallCells.Count, Is.EqualTo(24));
        Assert.That(result.Regions.Single().Partition.DoorOpenings.Count, Is.EqualTo(4));
        Assert.That(result.FloorCells.Concat(result.WallCells), Is.EquivalentTo(request.Shape.Cells));
    }

    [Test]
    public void SeparateCourtyardsShareRoomQuotasWithoutDuplicatingThem()
    {
        var request = KsProcgenHoleFootprintFixtures.Create(separateNetworks: true);
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.True);
        Assert.That(result.SizeGoalAllocationAttempts, Is.GreaterThan(0));
        Assert.That(Sizes(result), Is.EqualTo(new[] { 28, 28, 33, 33 }));
        Assert.That(result.Regions.Count, Is.EqualTo(2));
        Assert.That(result.FloorCells.Concat(result.WallCells), Is.EquivalentTo(request.Shape.Cells));
    }

    [TestCase(false)] [TestCase(true)]
    public void ExactWorkReplaysAndShortWorkDiscardsAllLayouts(bool separateNetworks)
    {
        var request = KsProcgenHoleFootprintFixtures.Create(separateNetworks: separateNetworks);
        var full = Plan(request);
        Assert.That(full.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), full.Issue?.Code);
        var exact = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), preferredMaxRoomCells: 64,
            maximumMergeAttempts: 0, useSizeGoals: true, maximumExpandedCells: full.ExpandedCells,
            maximumSeamProbes: full.SeamProbes);
        Assert.That(exact.PartitionHash, Is.EqualTo(full.PartitionHash));
        var shortWork = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), preferredMaxRoomCells: 64,
            maximumMergeAttempts: 0, useSizeGoals: true, maximumExpandedCells: full.ExpandedCells - 1,
            maximumSeamProbes: full.SeamProbes);
        Assert.That(shortWork.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(shortWork.ExpandedCells, Is.EqualTo(full.ExpandedCells - 1));
        Assert.That(shortWork.Regions, Is.Empty);
        Assert.That(shortWork.PartitionHash, Is.Zero);
    }

    [TestCase(false)] [TestCase(true)]
    public void ConnectivityFittingExhaustionCannotPublishEarlierLayouts(bool separateNetworks)
    {
        var request = KsProcgenHoleFootprintFixtures.Create(separateNetworks: separateNetworks);
        var floor = Floor(request);
        var limit = floor.Analysis!.ExpandedCells + floor.FloorCells.Count + 1;
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, useSizeGoals: true,
            maximumMergeAttempts: 0, maximumExpandedCells: limit);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(result.Issue!.Code, Is.EqualTo("PartitionExpansionBudget"));
        Assert.That(result.SizeGoalAttempts, Is.Empty, "fitting must exhaust before any seam attempt can be published");
        Assert.That(result.ExpandedCells, Is.EqualTo(limit));
        Assert.That(result.Regions, Is.Empty);
        Assert.That(result.PartitionHash, Is.Zero);
    }

    [Test]
    public void DisconnectedMiddleStripsRetainTheNormalScalarFallback()
    {
        var request = KsProcgenHoleFootprintFixtures.Create();
        request.Shape.Cells = Enumerable.Range(0, 10).SelectMany(x => Enumerable.Range(0, 7)
            .Where(y => !(x is >= 2 and <= 7 && y is >= 2 and <= 4)).Select(y => new Vector2i(x, y))).ToList();
        request.Shape.VoidCells.Clear();
        request.SizeMix = new List<KsProcgenRoomSizeGoal> { new() { Id = "Room", MinCells = 4, MaxCells = 32, TargetCount = 3 } };
        var result = Plan(request);
        var legacy = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), preferredMaxRoomCells: 64, maximumMergeAttempts: 0);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.False);
        Assert.That(result.PartitionHash, Is.EqualTo(legacy.PartitionHash));
        Assert.That(result.ExpandedCells, Is.GreaterThan(legacy.ExpandedCells));
    }

    private static int[] Sizes(KsProcgenGroupPartitionResult result) => result.Regions.SelectMany(region => region.Fill.Zones)
        .Select(zone => zone.Cells.Count(result.FloorCells.Contains)).Order().ToArray();
    private static KsProcgenGroupPartitionResult Plan(KsProcgenRequest request) =>
        KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), preferredMaxRoomCells: 64, maximumMergeAttempts: 0, useSizeGoals: true);
    private static KsProcgenGroupFloorResult Floor(KsProcgenRequest request)
    {
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Code);
        var result = KsProcgenGroupFloorPlanner.Plan(request, shape!.EntranceContract!.Configurations.Single().Id,
            new(new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true) { ExteriorIsEmptySpace = true });
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        return result;
    }
}
