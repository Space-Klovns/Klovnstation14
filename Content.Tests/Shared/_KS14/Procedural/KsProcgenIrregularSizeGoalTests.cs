using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenIrregularSizeGoalTests
{
    [Test]
    public void ReflectedStepUsesOverlapForWallLoss()
    {
        var request = Request(false);
        request.Shape.Cells = Enumerable.Range(0, 8).SelectMany(x => Enumerable.Range(0, x < 2 ? 2 : 4)
            .Select(y => new Vector2i(x, y))).ToList();
        request.SizeMix = new List<KsProcgenRoomSizeGoal>
        {
            new() { Id = "Large", MinCells = 23, MaxCells = 23 },
            new() { Id = "Small", MinCells = 4, MaxCells = 4 },
        };
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.True);
        Assert.That(result.SizeGoalAttempts.Single().Reflected, Is.True);
        Assert.That(Sizes(result), Is.EqualTo(new[] { 4, 23 }));
        Assert.That(result.WallCells.Count, Is.EqualTo(1));
    }

    [Test]
    public void IrregularCutVectorsAreCappedAndWholeFallbackIsRetained()
    {
        var request = Request(false);
        request.Shape.Cells = Enumerable.Range(0, 30).SelectMany(x => Enumerable.Range(0, x == 29 ? 3 : 4)
            .Select(y => new Vector2i(x, y))).ToList();
        request.Shape.PreservedCells = request.Shape.Cells.ToList();
        request.SizeMix = new List<KsProcgenRoomSizeGoal>
            { new() { Id = "Rooms", MinCells = 4, MaxCells = 4096, TargetCount = 4 } };
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalAttempts.Count, Is.EqualTo(16));
        Assert.That(result.SizeGoalAttempts.Select(attempt => attempt.WidthVariant),
            Is.EqualTo(Enumerable.Range(0, 8).Concat(Enumerable.Range(0, 8))));
        Assert.That(result.SizeGoalAttempts.All(attempt => attempt.Status == KsProcgenPartitionStatus.OpenFallback), Is.True);
        Assert.That(result.Regions.Single().Fill.Zones.Select(zone => zone.Cells.Count), Is.EqualTo(new[] { 8, 8, 8, 95 }));
        Assert.That(result.WallCells, Is.Empty);
    }

    [TestCase(65)]
    [TestCase(256)]
    public void IrregularRoomCountCapRetainsScalarFallback(int count)
    {
        var request = Request(false);
        request.SizeMix = new List<KsProcgenRoomSizeGoal>
            { new() { Id = "Rooms", MinCells = 4, MaxCells = 64, TargetCount = count } };
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.False);
        Assert.That(result.PartitionHash, Is.EqualTo(KsProcgenGroupPartitionPlanner.Plan(request, Floor(request),
            maximumMergeAttempts: 0).PartitionHash));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LShapeMeetsExactFloorGoalsWithoutFillingItsMissingCorner(bool transpose)
    {
        var request = Request(transpose);
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.True);
        Assert.That(result.SizeGoalAttempts.Single().AcrossX, Is.EqualTo(!transpose));
        Assert.That(Sizes(result), Is.EqualTo(new[] { 9, 16 }));
        Assert.That(result.FloorCells.Concat(result.WallCells).ToHashSet().SetEquals(request.Shape.Cells), Is.True);
        Assert.That(result.WallCells.Count, Is.EqualTo(3));
        var door = result.Regions.Single().Partition.DoorOpenings.Single();
        Assert.That(result.FloorCells, Does.Contain(door.InsideApproach));
        Assert.That(result.FloorCells, Does.Contain(door.OutsideApproach));
        Assert.That((door.InsideApproach - door.OutsideApproach).LengthSquared, Is.EqualTo(4));
        var exact = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), useSizeGoals: true,
            maximumMergeAttempts: 0, maximumExpandedCells: result.ExpandedCells, maximumSeamProbes: result.SeamProbes);
        Assert.That(exact.PartitionHash, Is.EqualTo(result.PartitionHash));
        request.Shape.Cells.Reverse();
        request.SizeMix.Reverse();
        Assert.That(Plan(request).PartitionHash, Is.EqualTo(result.PartitionHash));
    }

    [Test]
    public void IrregularCutRetryAvoidsProtectedSeam()
    {
        var request = Request(false);
        request.SizeMix[0].MinCells = 16;
        request.SizeMix[0].MaxCells = 20;
        request.SizeMix[1].MinCells = 5;
        request.SizeMix[1].MaxCells = 9;
        request.Shape.PreservedCells = new List<Vector2i> { new(4, 0), new(4, 1) };
        request.EntranceDomain!.Configurations[0].Groups[0].RootCells.Add(new(4, 0));
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalAttempts.Select(attempt => attempt.WidthVariant), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(result.SizeGoalAttempts[0].Status, Is.EqualTo(KsProcgenPartitionStatus.OpenFallback));
        Assert.That(Sizes(result), Is.EqualTo(new[] { 5, 20 }));
        Assert.That(request.Shape.PreservedCells.All(result.FloorCells.Contains), Is.True);
        Assert.That(result.SeamProbes, Is.EqualTo(result.SizeGoalAttempts.Sum(attempt => attempt.SeamProbes)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void IrregularSolveExhaustionIsAtomic(bool graphBudget)
    {
        var request = Request(false);
        var floor = Floor(request);
        var full = Plan(request);
        var graphLimit = floor.Analysis!.ExpandedCells + floor.FloorCells.Count + 1;
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, useSizeGoals: true, maximumMergeAttempts: 0,
            maximumExpandedCells: graphBudget ? graphLimit : full.ExpandedCells,
            maximumSeamProbes: graphBudget ? full.SeamProbes : 1);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(result.Regions, Is.Empty);
        Assert.That(result.PartitionHash, Is.Zero);
    }

    [Test]
    public void InteriorHoleRetainsScalarFallback()
    {
        var request = Request(false);
        request.Shape.Cells.Remove(new(2, 2));
        var result = Plan(request);
        var legacy = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), maximumMergeAttempts: 0);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.False);
        Assert.That(result.PartitionHash, Is.EqualTo(legacy.PartitionHash));
    }

    private static int[] Sizes(KsProcgenGroupPartitionResult result) => result.Regions.Single().Fill.Zones
        .Select(zone => zone.Cells.Count(result.FloorCells.Contains)).Order().ToArray();
    private static KsProcgenGroupPartitionResult Plan(KsProcgenRequest request) =>
        KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), maximumMergeAttempts: 0, useSizeGoals: true);
    private static KsProcgenRequest Request(bool transpose) => new()
    {
        RequestId = "IrregularRooms", Seed = 23,
        Shape = new() { Cells = Enumerable.Range(0, 8).SelectMany(x => Enumerable.Range(0, x >= 6 ? 2 : 4)
            .Select(y => transpose ? new Vector2i(y, x) : new Vector2i(x, y))).ToList() },
        SizeMix = new List<KsProcgenRoomSizeGoal>
        {
            new() { Id = "Large", MinCells = 16, MaxCells = 16 },
            new() { Id = "Small", MinCells = 9, MaxCells = 9 },
        },
        EntranceDomain = new()
        {
            Entrances = new List<KsProcgenEntranceSpec> { new() { PortId = "Entry",
                ThresholdCells = new List<Vector2i> { transpose ? new(0, -1) : new(-1, 0) },
                InwardNormal = transpose ? new(0, 1) : new(1, 0) } },
            Configurations = new List<KsProcgenEntranceConfigurationSpec> { new() { Id = "Connected",
                Groups = new List<KsProcgenEntranceGroupSpec> { new() { Id = "Room", Ports = new List<string> { "Entry" } } } } },
        },
    };
    private static KsProcgenGroupFloorResult Floor(KsProcgenRequest request)
    {
        var entrance = request.EntranceDomain!.Entrances[0];
        var threshold = entrance.ThresholdCells[0];
        return KsProcgenGroupFloorPlanner.Plan(request, "Connected", new(request.Shape.PreservedCells.ToHashSet(),
            new HashSet<Vector2i> { threshold, threshold - entrance.InwardNormal }, new HashSet<Vector2i>(), true, true));
    }
}
