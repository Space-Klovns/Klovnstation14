using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenWindowPlannerTests
{
    [Test]
    public void FixedEligibleWindowsStayInDenominatorAndSelectionReplays()
    {
        var boundary = Enumerable.Range(0, 8)
            .Select(x => new KsProcgenWindowBoundaryCell(new Vector2i(x, 0), true, true, true,
                Fixed: x < 2, IsWindow: x == 0)).ToArray();
        var first = KsProcgenWindowPlanner.Plan(boundary, 31, 0.5f);
        var replay = KsProcgenWindowPlanner.Plan(boundary, 31, 0.5f);
        var fromRequestGoal = KsProcgenWindowPlanner.Plan(boundary, 31,
            new KsProcgenWindowGoal { ExteriorWindowFraction = 0.5f });

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(KsProcgenWindowPlanStatus.Planned));
            Assert.That(first.EligibleCells, Is.EqualTo(8));
            Assert.That(first.FixedWindowCells, Is.EqualTo(1));
            Assert.That(first.FixedEligibleWallCells, Is.EqualTo(1));
            Assert.That(first.EditableEligibleCells, Is.EqualTo(6));
            Assert.That(first.ExcludedCells, Is.Empty);
            Assert.That(first.RequestedWindowCells, Is.EqualTo(4));
            Assert.That(first.ChosenWindowCells.Count, Is.EqualTo(3));
            Assert.That(first.ChosenWindowCells, Does.Not.Contain(new Vector2i(0, 0)));
            Assert.That(first.ChosenWindowCells, Does.Not.Contain(new Vector2i(1, 0)));
            Assert.That(first.ChosenWindowCells, Is.EqualTo(replay.ChosenWindowCells));
            Assert.That(first.ChosenWindowCells, Is.EqualTo(fromRequestGoal.ChosenWindowCells));
            Assert.That(first.AchievedFraction, Is.EqualTo(0.5f));
            Assert.That(first.AirtightnessVerified, Is.False);
        });
    }

    [Test]
    public void ExclusionsDoNotInflateDenominatorAndHardMissIsReported()
    {
        KsProcgenWindowBoundaryCell[] boundary =
        [
            new(new Vector2i(0, 0), true, true, true, Fixed: true, IsWindow: true),
            new(new Vector2i(1, 0), true, true, true, Fixed: true),
            new(new Vector2i(2, 0), true, true, true, IsCorner: true),
            new(new Vector2i(3, 0), true, true, true, IsDoorway: true),
            new(new Vector2i(4, 0), true, true, false),
        ];
        var soft = KsProcgenWindowPlanner.Plan(boundary, 1, 0f);
        var hard = KsProcgenWindowPlanner.Plan(boundary, 1, 0f,
            hardFraction: true, windowToleranceCells: 0);

        Assert.Multiple(() =>
        {
            Assert.That(soft.Status, Is.EqualTo(KsProcgenWindowPlanStatus.Planned));
            Assert.That(soft.EligibleCells, Is.EqualTo(2));
            Assert.That(soft.ExcludedCells.Select(item => item.Reason),
                Is.EquivalentTo(new[]
                {
                    KsProcgenWindowExclusionReason.Corner,
                    KsProcgenWindowExclusionReason.Doorway,
                    KsProcgenWindowExclusionReason.UnsupportedAirtightWindow,
                }));
            Assert.That(soft.AchievedWindowCells, Is.EqualTo(1));
            Assert.That(soft.ChosenWindowCells, Is.Empty);
            Assert.That(hard.Status, Is.EqualTo(KsProcgenWindowPlanStatus.HardTargetUnmet));
            Assert.That(hard.Issue?.Code, Is.EqualTo("WindowFractionInfeasible"));
        });
    }

    [Test]
    public void ZeroEligibleAndImpossibleCountHaveDistinctResults()
    {
        var boundary = new[]
        {
            new KsProcgenWindowBoundaryCell(new Vector2i(0, 0), true, true, true,
                IsDoorway: true),
        };
        var none = KsProcgenWindowPlanner.Plan(boundary, 4);
        var required = KsProcgenWindowPlanner.Plan(boundary, 4, minimumWindowCells: 1);
        var duplicate = KsProcgenWindowPlanner.Plan([boundary[0], boundary[0]], 4);

        Assert.Multiple(() =>
        {
            Assert.That(none.Status, Is.EqualTo(KsProcgenWindowPlanStatus.NotApplicable));
            Assert.That(none.EligibleCells, Is.Zero);
            Assert.That(none.ExcludedCells.Single().Reason,
                Is.EqualTo(KsProcgenWindowExclusionReason.Doorway));
            Assert.That(none.AchievedFraction, Is.Zero);
            Assert.That(required.Status, Is.EqualTo(KsProcgenWindowPlanStatus.HardTargetUnmet));
            Assert.That(required.Issue?.Code, Is.EqualTo("WindowCountInfeasible"));
            Assert.That(duplicate.Status, Is.EqualTo(KsProcgenWindowPlanStatus.InvalidInput));
        });
    }

    [Test]
    public void InspectedBoundaryMustBelongToRequestedShape()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "WindowBoundaryContract",
            Shape = new KsProcgenShapeSpec
            {
                Cells = [new Vector2i(0, 0)],
                EnvelopeCells = [new Vector2i(1, 0)],
            },
        };
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True,
            issue?.Message);

        var eligible = new KsProcgenWindowBoundaryCell(new Vector2i(1, 0), true, true, true);
        var planned = KsProcgenWindowPlanner.PlanForShape(shape!, [eligible], 7, request.WindowGoal);
        var outside = KsProcgenWindowPlanner.PlanForShape(shape!,
            [eligible with { Cell = new Vector2i(2, 0) }], 7, request.WindowGoal);

        Assert.That(planned.Status, Is.EqualTo(KsProcgenWindowPlanStatus.Planned));
        Assert.That(outside.Status, Is.EqualTo(KsProcgenWindowPlanStatus.InvalidInput));
        Assert.That(outside.Issue?.Code, Is.EqualTo("WindowBoundaryOutsideRequest"));
    }
}
