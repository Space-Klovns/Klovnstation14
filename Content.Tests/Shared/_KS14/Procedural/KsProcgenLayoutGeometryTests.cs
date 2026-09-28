using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenLayoutGeometryTests
{
    [Test]
    public void AlternativeReservesItsGeneratedGapAgainstSiblings()
    {
        var cells = new[] { new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(0, 1), new Vector2i(1, 1) };
        var left = new KsProcgenLayoutOption("SmallL", cells,
        [
            new KsProcgenRoomShape("A1", [cells[0]]),
            new KsProcgenRoomShape("A2", [cells[1]]),
            new KsProcgenRoomShape("A3", [cells[2]]),
        ], [cells[3]]);
        var four = new KsProcgenLayoutOption("SmallFour", cells,
        [
            new KsProcgenRoomShape("B1", [cells[0]]),
            new KsProcgenRoomShape("B2", [cells[1]]),
            new KsProcgenRoomShape("B3", [cells[2]]),
            new KsProcgenRoomShape("B4", [cells[3]]),
        ]);
        var family = new KsProcgenLayoutFamily("Block", [left, four]);
        Assert.That(family.TryValidate(out var issue), Is.True, issue?.Message);

        var request = new KsProcgenRequest
        {
            RequestId = "Block",
            Shape = new KsProcgenShapeSpec { Cells = cells.ToList() },
        };
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out issue), Is.True, issue?.Message);
        var candidates = KsProcgenLayoutGeometry.FindCandidatesCovering(family, shape, cells[0], 100, out var complete);
        Assert.That(complete, Is.True);
        Assert.That(candidates.Count, Is.EqualTo(2));

        var plan = new KsProcgenPlan(shape);
        var selected = candidates.Single(candidate => candidate.OptionId == "SmallL");
        var rejected = candidates.Single(candidate => candidate.OptionId == "SmallFour");
        Assert.That(KsProcgenLayoutGeometry.TryClaimCandidate(plan, selected), Is.True);
        Assert.That(plan.IsTargetComplete(), Is.True);
        Assert.That(plan.TryGetClaim(cells[3], out var gapClaim), Is.True);
        Assert.That(gapClaim.OwnerId, Does.EndWith("/Generate"));
        var selectedHash = plan.SemanticHash(request.RequestId, request.Seed, generatorVersion: 1);
        Assert.That(KsProcgenLayoutGeometry.TryClaimCandidate(plan, rejected), Is.False);
        Assert.That(plan.SemanticHash(request.RequestId, request.Seed, generatorVersion: 1), Is.EqualTo(selectedHash));
        Assert.That(plan.TryGetClaim(cells[3], out gapClaim), Is.True);
        Assert.That(gapClaim.OwnerId, Does.EndWith("/Generate"));
    }

    [Test]
    public void PlacementFindsOneTileShiftAndRanksExteriorMatch()
    {
        var localCells = new[] { new Vector2i(0, 0), new Vector2i(1, 0) };
        var facade = new KsProcgenBoundaryEdge(localCells[0], new Vector2i(-1, 0));
        var matched = new KsProcgenLayoutOption("Matched", localCells,
            [new KsProcgenRoomShape("Room", localCells, [facade])]);
        var unmatched = new KsProcgenLayoutOption("Unmatched", localCells,
            [new KsProcgenRoomShape("Room", localCells)]);
        var family = new KsProcgenLayoutFamily("Pair", [matched, unmatched]);
        var request = new KsProcgenRequest
        {
            RequestId = "Shifted",
            Shape = new KsProcgenShapeSpec { Cells = [new Vector2i(11, 5), new Vector2i(12, 5)] },
        };
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Message);

        var candidates = KsProcgenLayoutGeometry.FindCandidatesCovering(family, shape,
            new Vector2i(12, 5), 100, out var complete);

        Assert.Multiple(() =>
        {
            Assert.That(complete, Is.True);
            Assert.That(candidates.Count, Is.EqualTo(2));
            Assert.That(candidates[0].OptionId, Is.EqualTo("Matched"));
            Assert.That(candidates[0].Origin, Is.EqualTo(new Vector2i(11, 5)));
            Assert.That(candidates[0].MatchedExteriorEdges, Is.EqualTo(1));
            Assert.That(candidates[1].MatchedExteriorEdges, Is.Zero);
        });

        KsProcgenLayoutGeometry.FindCandidatesCovering(family, shape, new Vector2i(12, 5),
            maximumProbes: 1, out complete);
        Assert.That(complete, Is.False);
    }

    [Test]
    public void LayoutWithUnassignedOrOverlappingCellsIsInvalid()
    {
        var a = new Vector2i(0, 0);
        var b = new Vector2i(1, 0);
        var gap = new KsProcgenLayoutFamily("Gap",
            [new KsProcgenLayoutOption("A", [a, b], [new KsProcgenRoomShape("Room", [a])])]);
        Assert.That(gap.TryValidate(out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("LayoutGap"));

        var overlap = new KsProcgenLayoutFamily("Overlap",
            [new KsProcgenLayoutOption("A", [a, b],
            [
                new KsProcgenRoomShape("One", [a]),
                new KsProcgenRoomShape("Two", [a, b]),
            ])]);
        Assert.That(overlap.TryValidate(out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("LayoutOverlap"));
    }

    [Test]
    public void PortRotatesWithRoomAndNeedsTargetSideLanding()
    {
        var room = new KsProcgenRoomShape("Room", [new Vector2i(0, 0), new Vector2i(1, 0)],
            ports: [new KsProcgenRoomPort("Door", new Vector2i(1, 0), new Vector2i(1, 0))]);
        var family = new KsProcgenLayoutFamily("Pair",
            [new KsProcgenLayoutOption("Doorway", room.Cells, [room])], [1]);
        Assert.That(family.TryValidate(out var issue), Is.True, issue?.Message);

        var request = new KsProcgenRequest
        {
            RequestId = "RotatedPort",
            Shape = new KsProcgenShapeSpec
            {
                Cells = [new Vector2i(5, 5), new Vector2i(5, 4), new Vector2i(5, 3)],
            },
        };
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out issue), Is.True, issue?.Message);
        var candidates = KsProcgenLayoutGeometry.FindCandidatesCovering(family, shape!,
            new Vector2i(5, 4), 20, out var complete);
        Assert.That(complete, Is.True);
        var candidate = candidates.Single(c => c.Origin == new Vector2i(5, 5));
        var port = candidate.Ports.Single();
        Assert.Multiple(() =>
        {
            Assert.That(port.Threshold, Is.EqualTo(new Vector2i(5, 4)));
            Assert.That(port.OutwardNormal, Is.EqualTo(new Vector2i(0, -1)));
            Assert.That(port.InsideApproach, Is.EqualTo(new Vector2i(5, 5)));
            Assert.That(port.OutsideApproach, Is.EqualTo(new Vector2i(5, 3)));
        });

        request.Shape.Cells.Remove(new Vector2i(5, 3));
        Assert.That(KsProcgenGeometry.TryNormalize(request, out shape, out issue), Is.True, issue?.Message);
        candidates = KsProcgenLayoutGeometry.FindCandidatesCovering(family, shape!,
            new Vector2i(5, 4), 20, out complete);
        Assert.That(candidates, Is.Empty);
    }

    [Test]
    public void PortRequiresInteriorLandingAndBoundaryThreshold()
    {
        var room = new KsProcgenRoomShape("Room", [new Vector2i(0, 0), new Vector2i(1, 0)],
            ports: [new KsProcgenRoomPort("Door", new Vector2i(0, 0), new Vector2i(1, 0))]);
        var family = new KsProcgenLayoutFamily("Invalid",
            [new KsProcgenLayoutOption("A", room.Cells, [room])]);
        Assert.That(family.TryValidate(out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidRoomPort"));
    }
}
