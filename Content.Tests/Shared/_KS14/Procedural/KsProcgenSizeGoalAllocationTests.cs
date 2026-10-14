using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenSizeGoalAllocationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void BlockedSeamsRetryWholeQuotasAndRetainFirstWholeFallback(bool blockBoth)
    {
        var request = RetryRequest(blockBoth);
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalAllocationAttempts, Is.EqualTo(2));
        Assert.That(result.SizeGoalFitProbes, Is.InRange(1, 64));
        Assert.That(result.SizeGoalQuotaProbes, Is.InRange(1, 4096));
        Assert.That(result.SizeGoalFitProbes, Is.EqualTo(blockBoth ? 10 : 6));
        Assert.That(result.SizeGoalQuotaProbes, Is.EqualTo(blockBoth ? 14 : 8));
        Assert.That(result.SizeGoalAttempts.Select(attempt => attempt.AllocationOrdinal).Distinct(), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(result.Analysis!.Components.Count, Is.EqualTo(2));
        Assert.That(result.SeamProbes, Is.EqualTo(result.SizeGoalAttempts.Sum(attempt => attempt.SeamProbes)));
        Assert.That(result.Regions.Select(region => region.Fill.Zones.Count), Is.EqualTo(blockBoth ? new[] { 1, 2 } : new[] { 2, 1 }));
        Assert.That(Sizes(result), Is.EqualTo(blockBoth ? new[] { 8, 24, 32 } : new[] { 5, 24, 32 }));
        Assert.That(result.Regions.All(region => region.Partition.Status == KsProcgenPartitionStatus.Proposed), Is.EqualTo(!blockBoth));
        Assert.That(request.Shape.PreservedCells.All(result.FloorCells.Contains), Is.True);
        var exact = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), useSizeGoals: true,
            maximumMergeAttempts: 0, maximumExpandedCells: result.ExpandedCells, maximumSeamProbes: result.SeamProbes);
        Assert.That(exact.PartitionHash, Is.EqualTo(result.PartitionHash));
        Assert.That(exact.SizeGoalAttempts, Is.EqualTo(result.SizeGoalAttempts));
        request.Shape.Cells.Reverse();
        Assert.That(Plan(request).PartitionHash, Is.EqualTo(result.PartitionHash));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AllocationRetryCannotReuseExhaustedWork(bool graphBudget)
    {
        var request = RetryRequest(false);
        var floor = Floor(request);
        var full = Plan(request);
        var first = full.SizeGoalAttempts.Where(attempt => attempt.AllocationOrdinal == 0).ToArray();
        var graphLimit = floor.Analysis!.ExpandedCells + floor.FloorCells.Count + first.Sum(attempt => attempt.ExpandedCells) + 1;
        var seamLimit = first.Sum(attempt => attempt.SeamProbes) + 1;
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, useSizeGoals: true, maximumMergeAttempts: 0,
            maximumExpandedCells: graphBudget ? graphLimit : full.ExpandedCells,
            maximumSeamProbes: graphBudget ? full.SeamProbes : seamLimit);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(result.SizeGoalAllocationAttempts, Is.EqualTo(2));
        Assert.That(result.SizeGoalAttempts.Last().AllocationOrdinal, Is.EqualTo(1));
        Assert.That(result.SizeGoalAttempts.Last().Status, Is.EqualTo(KsProcgenPartitionStatus.BudgetExceeded));
        Assert.That(graphBudget ? result.ExpandedCells : result.SeamProbes,
            Is.EqualTo(graphBudget ? graphLimit : seamLimit));
        Assert.That(result.Regions, Is.Empty);
        Assert.That(result.PartitionHash, Is.Zero);
    }

    private static KsProcgenRequest RetryRequest(bool blockBoth)
    {
        var request = Request(8, 8);
        request.Shape.PreservedCells = request.Shape.Cells.Where(cell => blockBoth || cell.X >= 20).ToList();
        request.SizeMix = new List<KsProcgenRoomSizeGoal>
            { new() { Id = "Room", MinCells = 4, MaxCells = 64, TargetCount = 3 } };
        return request;
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void GlobalCountsAreDistributedOnceAcrossSeparateNetworks(bool reverse, bool requireBacktracking)
    {
        var request = Request(9, 4);
        request.SizeMix = new List<KsProcgenRoomSizeGoal>
        {
            new() { Id = "Large", MinCells = 16, MaxCells = 16, TargetCount = 2 },
            new() { Id = "Medium", MinCells = 9, MaxCells = 9 },
            new() { Id = "Small", MinCells = 5, MaxCells = 5 },
        };
        if (requireBacktracking)
        {
            request.SizeMix[0].MaxCells = 20;
            request.SizeMix[1].MaxCells = 13;
            request.SizeMix[2].MaxCells = 8;
        }
        var baseline = Plan(request);
        if (reverse)
        {
            request.Shape.Cells.Reverse();
            request.SizeMix.Reverse();
            request.EntranceDomain!.Entrances.Reverse();
            request.EntranceDomain.Configurations[0].Groups.Reverse();
        }
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.True);
        Assert.That(result.Regions.Count, Is.EqualTo(2));
        Assert.That(result.Analysis!.Components.Count, Is.EqualTo(2));
        Assert.That(Sizes(result), Is.EqualTo(new[] { 5, 9, 16, 16 }));
        Assert.That(result.SizeGoalAttempts.Select(attempt => attempt.NetworkId).Distinct().Count(), Is.EqualTo(2));
        Assert.That(result.SizeGoalQuotaProbes, Is.InRange(1, 4096));
        Assert.That(result.SizeGoalFitProbes, Is.InRange(1, 64));
        Assert.That(result.PartitionHash, Is.EqualTo(baseline.PartitionHash));
        Assert.That(result.SizeGoalAttempts, Is.EqualTo(baseline.SizeGoalAttempts));
        var exact = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), useSizeGoals: true,
            maximumMergeAttempts: 0, maximumExpandedCells: result.ExpandedCells,
            maximumSeamProbes: result.SeamProbes);
        Assert.That(exact.PartitionHash, Is.EqualTo(result.PartitionHash));
    }

    [Test]
    public void AllocationFailureIsAtomicAndRetainsScalarGeometry()
    {
        var request = Request(9, 4);
        request.SizeMix = new List<KsProcgenRoomSizeGoal>
            { new() { Id = "OnlyOne", MinCells = 4, MaxCells = 64 } };
        var result = Plan(request);
        var floor = Floor(request);
        var legacy = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64,
            maximumMergeAttempts: 0);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.False);
        Assert.That(result.SizeGoalAttempts, Is.Empty);
        Assert.That(result.PartitionHash, Is.EqualTo(legacy.PartitionHash));
        Assert.That(result.ExpandedCells, Is.EqualTo(legacy.ExpandedCells + floor.FloorCells.Count));
    }

    [Test]
    public void LaterUnsupportedNetworkDiscardsEarlierFeasibleQuota()
    {
        var request = Request(4, 9);
        request.Shape.Cells.Remove(new(28, 3));
        request.SizeMix = new List<KsProcgenRoomSizeGoal>
        {
            new() { Id = "Large", MinCells = 16, MaxCells = 16, TargetCount = 2 },
            new() { Id = "Medium", MinCells = 9, MaxCells = 9 },
            new() { Id = "Small", MinCells = 5, MaxCells = 5 },
        };
        var result = Plan(request);
        var legacy = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), preferredMaxRoomCells: 64,
            maximumMergeAttempts: 0);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalFitProbes, Is.GreaterThan(1));
        Assert.That(result.SizeGoalSubdivisionUsed, Is.False);
        Assert.That(result.SizeGoalAttempts, Is.Empty);
        Assert.That(result.PartitionHash, Is.EqualTo(legacy.PartitionHash));
    }

    [Test]
    public void InfeasibleQuotaSearchHasAHardFitCap()
    {
        var request = Request(10, 10, height: 10);
        request.SizeMix = new List<KsProcgenRoomSizeGoal>
            { new() { Id = "Tiny", MinCells = 4, MaxCells = 4, TargetCount = 100 } };
        var result = KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), preferredMaxRoomCells: 256,
            maximumMergeAttempts: 0, useSizeGoals: true);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.False);
        Assert.That(result.SizeGoalFitProbes, Is.EqualTo(64));
        Assert.That(result.SizeGoalQuotaProbes, Is.EqualTo(65));
        Assert.That(result.SizeGoalAttempts, Is.Empty);
        Assert.That(result.Regions.All(region => region.Fill.Zones.Count == 1), Is.True);
    }

    [Test]
    public void InspectionBudgetFailurePublishesNoPartialAllocation()
    {
        var request = Request(9, 4);
        request.SizeMix = new List<KsProcgenRoomSizeGoal>
            { new() { Id = "Room", MinCells = 4, MaxCells = 64, TargetCount = 2 } };
        var floor = Floor(request);
        var limit = floor.Analysis!.ExpandedCells + 1;
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, useSizeGoals: true,
            maximumExpandedCells: limit);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(result.ExpandedCells, Is.EqualTo(limit));
        Assert.That(result.SizeGoalFitProbes, Is.Zero);
        Assert.That(result.Regions, Is.Empty);
        Assert.That(result.PartitionHash, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PartitionBudgetsAreSharedAfterTheFirstNetwork(bool graphBudget)
    {
        var request = Request(9, 4);
        request.SizeMix = new List<KsProcgenRoomSizeGoal>
        {
            new() { Id = "Large", MinCells = 16, MaxCells = 16, TargetCount = 2 },
            new() { Id = "Medium", MinCells = 9, MaxCells = 9 },
            new() { Id = "Small", MinCells = 5, MaxCells = 5 },
        };
        var floor = Floor(request);
        var full = Plan(request);
        var first = full.SizeGoalAttempts[0];
        var graphLimit = floor.Analysis!.ExpandedCells + floor.FloorCells.Count + first.ExpandedCells + 1;
        var seamLimit = first.SeamProbes + 1;
        var result = KsProcgenGroupPartitionPlanner.Plan(request, floor, useSizeGoals: true,
            maximumMergeAttempts: 0, maximumExpandedCells: graphBudget ? graphLimit : full.ExpandedCells,
            maximumSeamProbes: graphBudget ? full.SeamProbes : seamLimit);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(result.SizeGoalAttempts.Count, Is.EqualTo(2));
        Assert.That(result.SizeGoalAttempts[0], Is.EqualTo(first));
        Assert.That(result.SizeGoalAttempts[1].Status, Is.EqualTo(KsProcgenPartitionStatus.BudgetExceeded));
        Assert.That(result.SizeGoalAttempts[1].NetworkId, Is.Not.EqualTo(first.NetworkId));
        Assert.That(graphBudget ? result.ExpandedCells : result.SeamProbes,
            Is.EqualTo(graphBudget ? graphLimit : seamLimit));
        Assert.That(result.SizeGoalFitProbes, Is.EqualTo(full.SizeGoalFitProbes));
        Assert.That(result.Regions, Is.Empty);
        Assert.That(result.PartitionHash, Is.Zero);
    }

    [TestCase(257)]
    [TestCase(4096)]
    public void RoomCapAppliesAcrossTheWholeRequest(int count)
    {
        var request = Request(9, 4);
        request.SizeMix = new List<KsProcgenRoomSizeGoal>
            { new() { Id = "Room", MinCells = 4, MaxCells = 64, TargetCount = count } };
        var result = Plan(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SizeGoalSubdivisionUsed, Is.False);
        Assert.That(result.SizeGoalFitProbes, Is.Zero);
        Assert.That(result.SizeGoalQuotaProbes, Is.Zero);
        Assert.That(result.SizeGoalAttempts, Is.Empty);
    }

    private static int[] Sizes(KsProcgenGroupPartitionResult result) => result.Regions.SelectMany(region => region.Fill.Zones)
        .Select(zone => zone.Cells.Count(result.FloorCells.Contains)).Order().ToArray();

    private static KsProcgenGroupPartitionResult Plan(KsProcgenRequest request) =>
        KsProcgenGroupPartitionPlanner.Plan(request, Floor(request), preferredMaxRoomCells: 64,
            maximumMergeAttempts: 0, useSizeGoals: true);

    private static KsProcgenRequest Request(int firstWidth, int secondWidth, int height = 4) => new()
    {
        RequestId = "SeparateRectangles", Seed = 23, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
        Shape = new() { Cells = Enumerable.Range(0, firstWidth).Concat(Enumerable.Range(20, secondWidth))
            .SelectMany(x => Enumerable.Range(0, height).Select(y => new Vector2i(x, y))).ToList() },
        EntranceDomain = new()
        {
            Entrances = new List<KsProcgenEntranceSpec>
            {
                new() { PortId = "A1", ThresholdCells = new List<Vector2i> { new(-1, 0) }, InwardNormal = new(1, 0) },
                new() { PortId = "B1", ThresholdCells = new List<Vector2i> { new(19, 0) }, InwardNormal = new(1, 0) },
            },
            Configurations = new List<KsProcgenEntranceConfigurationSpec> { new() { Id = "Separate",
                Groups = new List<KsProcgenEntranceGroupSpec>
                {
                    new() { Id = "A", Ports = new List<string> { "A1" } },
                    new() { Id = "B", Ports = new List<string> { "B1" } },
                } } },
        },
    };

    private static KsProcgenGroupFloorResult Floor(KsProcgenRequest request) =>
        KsProcgenGroupFloorPlanner.Plan(request, "Separate", new(request.Shape.PreservedCells.ToHashSet(),
            new HashSet<Vector2i> { new(-1, 0), new(-2, 0), new(19, 0), new(18, 0) },
            new HashSet<Vector2i>(), true, true));
}
