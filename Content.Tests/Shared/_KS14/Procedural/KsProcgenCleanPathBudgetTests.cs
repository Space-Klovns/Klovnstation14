using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenCleanPathBudgetTests
{
    [TestCase(0, false)]
    [TestCase(2, false)]
    [TestCase(3, true)]
    public void GoalDequeueConsumesTheAllowanceAndNeverReturnsAPartialPath(int allowance, bool succeeds)
    {
        var floor = Line(3);
        var budget = Budget(allowance);
        var path = KsProcgenTraversal.FindCleanPath(new(0, 0), new(2, 0), floor,
            new HashSet<Vector2i>(), pathBudget: budget);
        Assert.That(path, succeeds ? Is.EqualTo(new[] { new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0) }) : Is.Empty);
        Assert.That(budget.ExpandedCells, Is.EqualTo(allowance));
        Assert.That(budget.CleanPathExpandedCells, Is.EqualTo(allowance));
        Assert.That(budget.RelationExpandedCells, Is.Zero);
        Assert.That(budget.CleanPathSearchTruncated, Is.EqualTo(!succeeds));
        Assert.That(budget.RelationPathSearchTruncated, Is.False);
    }

    [Test]
    public void RepeatedQueriesAndRelationSearchesShareOneAllowance()
    {
        var budget = Budget(4);
        var floor = Line(3);
        Assert.That(KsProcgenTraversal.FindCleanPath(new(0, 0), new(2, 0), floor,
            new HashSet<Vector2i>(), pathBudget: budget), Is.Not.Empty);
        Assert.That(budget.Expand(), Is.True);
        Assert.That(KsProcgenTraversal.FindCleanPath(new(0, 0), new(0, 0), floor,
            new HashSet<Vector2i>(), pathBudget: budget), Is.Empty);
        Assert.That(budget.ExpandedCells, Is.EqualTo(4));
        Assert.That(budget.CleanPathExpandedCells, Is.EqualTo(3));
        Assert.That(budget.RelationExpandedCells, Is.EqualTo(1));
        Assert.That(budget.CleanPathSearchTruncated, Is.True);
        Assert.That(budget.RelationPathSearchTruncated, Is.False);
        Assert.That(budget.Expand(), Is.False);
        Assert.That(budget.RelationPathSearchTruncated, Is.True);
        Assert.That(budget.ExpandedCells, Is.EqualTo(4));
    }

    [Test]
    public void DisconnectedQueriesChargeOnlyExpandedReachableCells()
    {
        var budget = Budget(1);
        var floor = new HashSet<Vector2i> { new(0, 0), new(1, 1) };
        Assert.That(KsProcgenTraversal.FindCleanPath(new(0, 0), new(1, 1), floor,
            new HashSet<Vector2i>(), pathBudget: budget), Is.Empty);
        Assert.That(budget.ExpandedCells, Is.EqualTo(1));
        Assert.That(budget.Truncated, Is.False, "An exhausted queue is a proven miss, even at the exact allowance.");
    }

    [Test]
    public void VaultBlockersAndInvalidEndpointsDoNotBecomeBudgetFailures()
    {
        var budget = Budget(1);
        var floor = Line(3);
        var vault = new HashSet<Vector2i> { new(1, 0) };
        Assert.That(KsProcgenTraversal.FindCleanPath(new(0, 0), new(2, 0), floor, vault,
            pathBudget: budget), Is.Empty);
        Assert.That(budget.ExpandedCells, Is.EqualTo(1));
        Assert.That(KsProcgenTraversal.FindCleanPath(new(0, 0), new(1, 0), floor, vault,
            pathBudget: budget), Is.Empty);
        Assert.That(KsProcgenTraversal.FindCleanPath(new(-1, 0), new(0, 0), floor, vault,
            pathBudget: budget), Is.Empty);
        Assert.That(budget.ExpandedCells, Is.EqualTo(1));
        Assert.That(budget.Truncated, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ChairAndMachineApproachesDistinguishBudgetFailureFromBlockedAccess(bool machine)
    {
        var budget = Budget(1);
        var floor = Line(3);
        var roots = new HashSet<Vector2i> { new(0, 0) };
        if (machine)
        {
            var result = KsProcgenInteractionFacingPlanner.ChooseMachineFacing(new(2, 0), floor,
                new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), roots,
                [1], 7, "Computer", pathBudget: budget);
            Assert.That(result.Status, Is.EqualTo(KsProcgenInteractionStatus.BudgetExceeded));
            Assert.That(result.Options, Is.Empty);
            Assert.That(result.Facing, Is.Null);
            Assert.That(result.Issue?.Code, Is.EqualTo("MachineApproachPathBudget"));
        }
        else
        {
            var result = KsProcgenInteractionFacingPlanner.FindChairApproach(new(2, 0), floor,
                new HashSet<Vector2i>(), roots, pathBudget: budget);
            Assert.That(result.Status, Is.EqualTo(KsProcgenInteractionStatus.BudgetExceeded));
            Assert.That(result.Approaches, Is.Empty);
            Assert.That(result.CleanPath, Is.Empty);
            Assert.That(result.Approach, Is.Null);
            Assert.That(result.Issue?.Code, Is.EqualTo("ChairApproachPathBudget"));
        }
        Assert.That(budget.ExpandedCells, Is.EqualTo(1));
        Assert.That(budget.CleanPathSearchTruncated, Is.True);
    }

    [Test]
    public void FacingSearchDropsEarlierLegalOptionsWhenLaterWorkExhausts()
    {
        var floor = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 3)
            .Select(y => new Vector2i(x, y))).ToHashSet();
        var roots = new HashSet<Vector2i> { new(1, 0) };
        var budget = Budget(1);
        var result = KsProcgenInteractionFacingPlanner.ChooseMachineFacing(new(1, 1), floor,
            new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), roots,
            [0, 1], 7, "Computer", pathBudget: budget);
        Assert.That(result.Status, Is.EqualTo(KsProcgenInteractionStatus.BudgetExceeded));
        Assert.That(result.Options, Is.Empty);
        Assert.That(result.Facing, Is.Null);
        Assert.That(budget.CleanPathExpandedCells, Is.EqualTo(1));
    }

    [Test]
    public void MultipleRootsUseCanonicalOrderAndChargeEveryDequeuedRoot()
    {
        var floor = Line(6);
        var roots = new HashSet<Vector2i> { new(3, 0), new(0, 0) };
        var budget = Budget(5);
        var result = KsProcgenInteractionFacingPlanner.FindChairApproach(new(5, 0), floor,
            new HashSet<Vector2i>(), roots, pathBudget: budget);
        Assert.That(result.Status, Is.EqualTo(KsProcgenInteractionStatus.Ready));
        Assert.That(result.CleanPath, Is.EqualTo(new[] { new Vector2i(3, 0), new Vector2i(4, 0) }));
        Assert.That(budget.CleanPathExpandedCells, Is.EqualTo(5));
        Assert.That(budget.Truncated, Is.False);
    }

    [Test]
    public void LegacyRelationOnlyBudgetDoesNotChargeAccessQueries()
    {
        var budget = new KsProcgenRelationPathBudget(maximumExpandedCells: 0);
        var floor = Line(3);
        Assert.That(KsProcgenTraversal.FindCleanPath(new(0, 0), new(2, 0), floor,
            new HashSet<Vector2i>(), pathBudget: budget), Is.Not.Empty);
        Assert.That(budget.ExpandedCells, Is.Zero);
        Assert.That(budget.Truncated, Is.False);
    }

    private static HashSet<Vector2i> Line(int length) =>
        Enumerable.Range(0, length).Select(x => new Vector2i(x, 0)).ToHashSet();

    private static KsProcgenRelationPathBudget Budget(int allowance) =>
        new(maximumExpandedCells: allowance, meterCleanPaths: true);
}
