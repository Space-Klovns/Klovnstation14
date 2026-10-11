using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenPartitionWorkTests
{
    [TestCase(0, false)]
    [TestCase(11, false)]
    [TestCase(12, true)]
    public void TinyPassageChargesFourConnectivityPassesAndFailsAtomically(int allowance, bool succeeds)
    {
        var fill = Tiny();
        var budget = new KsProcgenPartitionWorkBudget(maximumExpandedCells: allowance, maximumSeamProbes: 18);
        var result = KsProcgenPartitionPlanner.Plan(fill, fill.Zones[0].Cells.ToHashSet(), workBudget: budget);
        Assert.That(result.ExpandedCells, Is.EqualTo(allowance));
        Assert.That(budget.ExpandedCells, Is.EqualTo(allowance));
        if (succeeds)
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenPartitionStatus.Proposed));
            Assert.That(result.FloorCells, Is.EqualTo(fill.Zones[0].Cells));
            Assert.That(result.SeamProbes, Is.EqualTo(18));
            Assert.That(budget.ExhaustedCode, Is.Null);
        }
        else
            AssertFailure(result, "PartitionExpansionBudget");
    }

    [TestCase(0, false)]
    [TestCase(17, false)]
    [TestCase(18, true)]
    public void OwnershipAndDirectedInterfaceQueriesConsumeAnIndependentSeamAllowance(int allowance, bool succeeds)
    {
        var fill = Tiny();
        var budget = new KsProcgenPartitionWorkBudget(maximumExpandedCells: 12, maximumSeamProbes: allowance);
        var result = KsProcgenPartitionPlanner.Plan(fill, fill.Zones[0].Cells.ToHashSet(), workBudget: budget);
        Assert.That(result.SeamProbes, Is.EqualTo(allowance));
        Assert.That(budget.SeamProbes, Is.EqualTo(allowance));
        if (succeeds)
            Assert.That(result.Status, Is.EqualTo(KsProcgenPartitionStatus.Proposed));
        else
            AssertFailure(result, "PartitionSeamProbeBudget");
    }

    [Test]
    public void ReusingOneBudgetCannotRefundAnEarlierNetwork()
    {
        var fill = Tiny();
        var budget = new KsProcgenPartitionWorkBudget(maximumExpandedCells: 24, maximumSeamProbes: 35);
        var first = KsProcgenPartitionPlanner.Plan(fill, fill.Zones[0].Cells.ToHashSet(), workBudget: budget);
        Assert.That(first.Status, Is.EqualTo(KsProcgenPartitionStatus.Proposed));
        var second = KsProcgenPartitionPlanner.Plan(fill, fill.Zones[0].Cells.ToHashSet(), workBudget: budget);
        AssertFailure(second, "PartitionSeamProbeBudget");
        Assert.That(second.SeamProbes, Is.EqualTo(17));
        Assert.That(second.ExpandedCells, Is.EqualTo(3));
        Assert.That(budget.SeamProbes, Is.EqualTo(first.SeamProbes + second.SeamProbes));
        Assert.That(budget.ExpandedCells, Is.EqualTo(first.ExpandedCells + second.ExpandedCells));
        Assert.That(first.FloorCells.Count, Is.EqualTo(3));
        var exhausted = KsProcgenPartitionPlanner.Plan(fill, fill.Zones[0].Cells.ToHashSet(), workBudget: budget);
        AssertFailure(exhausted, "PartitionSeamProbeBudget");
        Assert.That(exhausted.SeamProbes + exhausted.ExpandedCells, Is.Zero);
    }

    [Test]
    public void ARejectedSplitChargesItsRebuildAndReportsActualMerges()
    {
        var fill = ThinRoom();
        var budget = new KsProcgenPartitionWorkBudget();
        var full = KsProcgenPartitionPlanner.Plan(fill, fill.Zones[0].Cells.ToHashSet(), workBudget: budget);
        Assert.That(full.Status, Is.EqualTo(KsProcgenPartitionStatus.OpenFallback));
        Assert.That(full.ExpandedCells, Is.EqualTo(24));
        Assert.That(full.SeamProbes, Is.EqualTo(69));
        Assert.That(full.MergeAttempts, Is.EqualTo(1));
        Assert.That(full.RejectedSplits, Is.EqualTo(1));
        var shortfall = KsProcgenPartitionPlanner.Plan(fill, fill.Zones[0].Cells.ToHashSet(),
            workBudget: new(maximumExpandedCells: 24, maximumSeamProbes: 68));
        AssertFailure(shortfall, "PartitionSeamProbeBudget");
        Assert.That(shortfall.MergeAttempts, Is.EqualTo(1));
        Assert.That(shortfall.SeamProbes, Is.EqualTo(68));
        Assert.That(shortfall.ExpandedCells, Is.EqualTo(6));
        var noMerge = KsProcgenPartitionPlanner.Plan(fill, fill.Zones[0].Cells.ToHashSet(),
            maxMergeAttempts: 0, workBudget: new());
        Assert.That(noMerge.Status, Is.EqualTo(KsProcgenPartitionStatus.OpenFallback));
        Assert.That(noMerge.Issue?.Code, Is.EqualTo("PartitionMergeBudget"));
        Assert.That(noMerge.RejectedSplits, Is.EqualTo(1));
        Assert.That(noMerge.MergeAttempts, Is.Zero);
        Assert.That(noMerge.SeamProbes, Is.EqualTo(39));
    }

    [Test]
    public void MeteringPreservesDoorsWallsAndMergedMembershipAtExactLimits()
    {
        var passage = Enumerable.Range(0, 3).Select(x => new Vector2i(x, 0)).ToArray();
        var room = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(1, 3)
            .Select(y => new Vector2i(x, y))).ToArray();
        var fill = Fill(new("Passage", KsProcgenZoneKind.Passage, passage),
            new("Room", KsProcgenZoneKind.RoomProposal, room));
        var legacy = KsProcgenPartitionPlanner.Plan(fill, passage.ToHashSet());
        var full = KsProcgenPartitionPlanner.Plan(fill, passage.ToHashSet(), workBudget: new());
        Assert.That(full.Status, Is.EqualTo(legacy.Status));
        Assert.That(full.FloorCells, Is.EqualTo(legacy.FloorCells));
        Assert.That(full.WallCells, Is.EqualTo(legacy.WallCells));
        Assert.That(full.DoorOpenings, Is.EqualTo(legacy.DoorOpenings));
        Assert.That(full.MergedZoneGroups, Is.EqualTo(legacy.MergedZoneGroups));
        Assert.That(full.ExpandedCells, Is.EqualTo(44));
        Assert.That(full.SeamProbes, Is.EqualTo(77));
        var exact = KsProcgenPartitionPlanner.Plan(fill, passage.ToHashSet(), workBudget: new(
            maximumExpandedCells: full.ExpandedCells, maximumSeamProbes: full.SeamProbes));
        Assert.That(exact.DoorOpenings, Is.EqualTo(full.DoorOpenings));
        Assert.That(exact.WallCells, Is.EqualTo(full.WallCells));
        Assert.That(exact.ExpandedCells, Is.EqualTo(full.ExpandedCells));
        Assert.That(exact.SeamProbes, Is.EqualTo(full.SeamProbes));
        Assert.That(legacy.ExpandedCells + legacy.SeamProbes, Is.Zero);
    }

    [Test]
    public void InvalidDisconnectedAndOverlappingInputsRetainActualValidationWork()
    {
        var disconnected = Fill(new KsProcgenZone("Bad", KsProcgenZoneKind.RoomProposal, [new(0, 0), new(1, 1)]));
        var result = KsProcgenPartitionPlanner.Plan(disconnected, new HashSet<Vector2i>(), workBudget: new());
        Assert.That(result.Status, Is.EqualTo(KsProcgenPartitionStatus.InvalidInput));
        Assert.That(result.ExpandedCells, Is.EqualTo(2));
        Assert.That(result.SeamProbes, Is.Zero);
        var overlap = Fill(new("A", KsProcgenZoneKind.RoomProposal, [new(0, 0)]),
            new("B", KsProcgenZoneKind.RoomProposal, [new(0, 0)]));
        result = KsProcgenPartitionPlanner.Plan(overlap, new HashSet<Vector2i>(), workBudget: new());
        Assert.That(result.Issue?.Code, Is.EqualTo("OverlappingPartitionZones"));
        Assert.That(result.ExpandedCells, Is.EqualTo(2));
        Assert.That(result.SeamProbes, Is.EqualTo(2));
        Assert.That(result.FloorCells, Is.Empty);
    }

    private static KsProcgenPureFillResult Tiny() => Fill(new KsProcgenZone("Tiny", KsProcgenZoneKind.Passage,
        [new(0, 0), new(1, 0), new(2, 0)]));

    private static KsProcgenPureFillResult ThinRoom() => Fill(
        new("Passage", KsProcgenZoneKind.Passage, [new(0, 0), new(1, 0), new(2, 0)]),
        new("Room", KsProcgenZoneKind.RoomProposal, [new(0, 1), new(1, 1), new(2, 1)]));

    private static KsProcgenPureFillResult Fill(params KsProcgenZone[] zones) => new()
    {
        Status = KsProcgenPureFillStatus.Proposed, Zones = zones,
        ProceduralCells = zones.Sum(zone => zone.Cells.Count),
    };

    private static void AssertFailure(KsProcgenPartitionResult result, string code)
    {
        Assert.That(result.Status, Is.EqualTo(KsProcgenPartitionStatus.BudgetExceeded));
        Assert.That(result.Issue?.Code, Is.EqualTo(code));
        Assert.That(result.FloorCells, Is.Empty);
        Assert.That(result.WallCells, Is.Empty);
        Assert.That(result.DoorOpenings, Is.Empty);
        Assert.That(result.MergedZoneGroups, Is.Empty);
    }
}
