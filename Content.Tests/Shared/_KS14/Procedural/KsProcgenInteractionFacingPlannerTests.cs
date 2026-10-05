using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenInteractionFacingPlannerTests
{
    [Test]
    public void RightWallTurnsComputerLeftTowardCleanFloor()
    {
        var floor = Rect(0, 0, 2, 3);
        var walls = new HashSet<Vector2i> { new(2, 1) };
        var result = KsProcgenInteractionFacingPlanner.ChooseMachineFacing(new Vector2i(1, 1),
            floor, walls, new HashSet<Vector2i>(), new HashSet<Vector2i>(),
            new HashSet<Vector2i> { new(0, 0) }, [0, 1, 2, 3], 7, "ComputerA");

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenInteractionStatus.Ready));
            Assert.That(result.Facing?.QuarterTurns, Is.EqualTo(1));
            Assert.That(result.Facing?.Facing, Is.EqualTo(new Vector2i(-1, 0)));
            Assert.That(result.Facing?.Approach, Is.EqualTo(new Vector2i(0, 1)));
            Assert.That(result.Facing?.BackedByWall, Is.True);
            Assert.That(result.Facing?.CleanPath.Count, Is.GreaterThan(0));
        });
    }

    [Test]
    public void CornerPrefersReachableAssociatedChairThenEmptyFloor()
    {
        var floor = Rect(0, 0, 2, 2);
        var walls = new HashSet<Vector2i> { new(2, 1), new(1, 2) };
        var chairs = new HashSet<Vector2i> { new(0, 1) };
        var roots = new HashSet<Vector2i> { new(0, 0) };
        var seated = KsProcgenInteractionFacingPlanner.ChooseMachineFacing(new Vector2i(1, 1),
            floor, walls, new HashSet<Vector2i>(), chairs, roots,
            [0, 1, 2, 3], 7, "CornerComputer", new Vector2i(0, 1));
        var standing = KsProcgenInteractionFacingPlanner.ChooseMachineFacing(new Vector2i(1, 1),
            floor, walls, new HashSet<Vector2i>(), chairs, roots,
            [0, 1, 2, 3], 7, "CornerComputer");

        Assert.Multiple(() =>
        {
            Assert.That(seated.Facing?.QuarterTurns, Is.EqualTo(1));
            Assert.That(standing.Facing?.QuarterTurns, Is.EqualTo(0));
            Assert.That(seated.Facing?.Approach, Is.EqualTo(new Vector2i(0, 1)));
            Assert.That(standing.Facing?.Approach, Is.EqualTo(new Vector2i(1, 0)));
        });
    }

    [Test]
    public void VaultRequiredTableCannotProvideChairPassage()
    {
        var floor = new HashSet<Vector2i> { new(0, 0), new(0, 1), new(0, 2) };
        var root = new HashSet<Vector2i> { new(0, 0) };
        var clear = KsProcgenInteractionFacingPlanner.FindChairApproach(new Vector2i(0, 2),
            floor, new HashSet<Vector2i>(), root);
        var blocked = KsProcgenInteractionFacingPlanner.FindChairApproach(new Vector2i(0, 2),
            floor, new HashSet<Vector2i> { new(0, 1) }, root);

        Assert.Multiple(() =>
        {
            Assert.That(clear.Status, Is.EqualTo(KsProcgenInteractionStatus.Ready));
            Assert.That(clear.Approach, Is.EqualTo(new Vector2i(0, 1)));
            Assert.That(blocked.Status, Is.EqualTo(KsProcgenInteractionStatus.NoCleanApproach));
            Assert.That(blocked.CleanPath, Is.Empty);
        });
    }

    [Test]
    public void ManyNetworkEntriesUseNearestCleanApproach()
    {
        var floor = Enumerable.Range(0, 6).Select(x => new Vector2i(x, 0)).ToHashSet();
        var roots = new HashSet<Vector2i> { new(0, 0), new(3, 0) };

        var result = KsProcgenInteractionFacingPlanner.FindChairApproach(new Vector2i(5, 0),
            floor, new HashSet<Vector2i>(), roots);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenInteractionStatus.Ready));
            Assert.That(result.CleanPath, Is.EqualTo(new[] { new Vector2i(3, 0), new Vector2i(4, 0) }));
        });
    }

    [Test]
    public void ComputerCannotFaceThroughVaultRequiredObstacle()
    {
        var floor = Rect(0, 0, 3, 2);
        var walls = new HashSet<Vector2i> { new(3, 1) };
        var roots = new HashSet<Vector2i> { new(0, 1) };
        var clear = KsProcgenInteractionFacingPlanner.ChooseMachineFacing(new Vector2i(2, 1),
            floor, walls, new HashSet<Vector2i>(), new HashSet<Vector2i>(),
            roots, [1], 11, "DeskComputer");
        var table = KsProcgenInteractionFacingPlanner.ChooseMachineFacing(new Vector2i(2, 1),
            floor, walls, new HashSet<Vector2i> { new(1, 1) }, new HashSet<Vector2i>(),
            roots, [1], 11, "DeskComputer");

        Assert.Multiple(() =>
        {
            Assert.That(clear.Status, Is.EqualTo(KsProcgenInteractionStatus.Ready));
            Assert.That(table.Status, Is.EqualTo(KsProcgenInteractionStatus.NoCleanApproach));
        });
    }

    [Test]
    public void MultiCellMachineUsesReachableFrontEdgeAndAssociatedChair()
    {
        var floor = Rect(0, 0, 4, 3);
        var footprint = new[] { new Vector2i(0, 0), new Vector2i(1, 0) };
        var chair = new Vector2i(2, 0);
        var result = KsProcgenInteractionFacingPlanner.ChooseMachineFacing(new Vector2i(1, 1),
            floor, new HashSet<Vector2i>(), new HashSet<Vector2i>(),
            new HashSet<Vector2i> { chair }, new HashSet<Vector2i> { new(0, 0) },
            [0], 7, "WideConsole", chair, footprint);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenInteractionStatus.Ready));
            Assert.That(result.Facing?.Approach, Is.EqualTo(chair));
            Assert.That(result.Facing?.Facing, Is.EqualTo(new Vector2i(0, -1)));
            Assert.That(result.Facing?.CleanPath[^1], Is.EqualTo(chair));
        });
    }

    [Test]
    public void MultiCellMachineRotatesWhenOnlyOneFootprintOrientationFits()
    {
        var floor = Rect(0, 0, 2, 3);
        var footprint = new[] { new Vector2i(0, 0), new Vector2i(1, 0) };
        var result = KsProcgenInteractionFacingPlanner.ChooseMachineFacing(new Vector2i(1, 1),
            floor, new HashSet<Vector2i>(), new HashSet<Vector2i>(),
            new HashSet<Vector2i>(), new HashSet<Vector2i> { new(0, 0) },
            [0, 1, 2, 3], 7, "RotatedWideConsole", localFootprint: footprint);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenInteractionStatus.Ready));
            Assert.That(result.Facing?.QuarterTurns, Is.EqualTo(1));
            Assert.That(result.Facing?.Facing, Is.EqualTo(new Vector2i(-1, 0)));
            Assert.That(result.Facing?.Approach.X, Is.EqualTo(0));
            Assert.That(result.Facing?.CleanPath, Is.Not.Empty);
        });
    }

    private static HashSet<Vector2i> Rect(int minX, int minY, int maxX, int maxY) =>
        Enumerable.Range(minX, maxX - minX).SelectMany(x => Enumerable.Range(minY, maxY - minY)
            .Select(y => new Vector2i(x, y))).ToHashSet();
}
