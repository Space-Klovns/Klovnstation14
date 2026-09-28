using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenPartitionPlannerTests
{
    [Test]
    public void TileThickSeamLeavesOneDoorAndProtectedCorridor()
    {
        var passage = Row(0);
        var room = Row(1).Concat(Row(2)).Concat(Row(3)).ToArray();
        var fill = Fill(new KsProcgenZone("passage", KsProcgenZoneKind.Passage, passage),
            new KsProcgenZone("room", KsProcgenZoneKind.RoomProposal, room));

        var result = KsProcgenPartitionPlanner.Plan(fill, passage.ToHashSet());

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenPartitionStatus.Proposed));
            Assert.That(result.WallCells, Is.EquivalentTo(new[] { new Vector2i(1, 1), new Vector2i(2, 1) }));
            Assert.That(result.DoorOpenings.Count, Is.EqualTo(1));
            Assert.That(result.DoorOpenings[0].Threshold, Is.EqualTo(new Vector2i(0, 1)));
            Assert.That(result.DoorOpenings[0].InsideApproach, Is.EqualTo(new Vector2i(0, 2)));
            Assert.That(result.DoorOpenings[0].OutsideApproach, Is.EqualTo(new Vector2i(0, 0)));
            Assert.That(result.FloorCells.Intersect(passage), Is.EquivalentTo(passage));
            Assert.That(result.FloorCells.Count + result.WallCells.Count, Is.EqualTo(12));
            Assert.That(KsProcgenGeometry.ConnectedComponents(result.FloorCells.ToHashSet()).Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void ThinRoomWithoutInteriorLandingFallsBackOpen()
    {
        var passage = Row(0);
        var room = Row(1);
        var fill = Fill(new KsProcgenZone("passage", KsProcgenZoneKind.Passage, passage),
            new KsProcgenZone("room", KsProcgenZoneKind.RoomProposal, room));

        var result = KsProcgenPartitionPlanner.Plan(fill, passage.ToHashSet());

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenPartitionStatus.OpenFallback));
            Assert.That(result.Issue?.Code, Is.EqualTo("PartitionOpenFallback"));
            Assert.That(result.FloorCells.Count, Is.EqualTo(6));
            Assert.That(result.WallCells, Is.Empty);
            Assert.That(result.DoorOpenings, Is.Empty);
        });
    }

    [Test]
    public void TinyPassageHasNoSeamAndInvalidCoverageFails()
    {
        var tiny = new[] { new Vector2i(0, 0), new Vector2i(0, 1), new Vector2i(0, 2) };
        var fill = Fill(new KsProcgenZone("tiny", KsProcgenZoneKind.Passage, tiny));
        var good = KsProcgenPartitionPlanner.Plan(fill, tiny.ToHashSet());
        var bad = KsProcgenPartitionPlanner.Plan(fill, new HashSet<Vector2i> { new(7, 7) });

        Assert.Multiple(() =>
        {
            Assert.That(good.Status, Is.EqualTo(KsProcgenPartitionStatus.Proposed));
            Assert.That(good.FloorCells, Is.EquivalentTo(tiny));
            Assert.That(good.WallCells, Is.Empty);
            Assert.That(bad.Status, Is.EqualTo(KsProcgenPartitionStatus.InvalidInput));
            Assert.That(bad.FloorCells, Is.Empty);
        });
    }

    [Test]
    public void TwoInterfacesCannotClaimTheSamePhysicalDoorCell()
    {
        var west = new[] { new Vector2i(-1, 0) };
        var south = new[] { new Vector2i(0, -1) };
        var room = new[] { new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(0, 1) };
        var fill = Fill(new KsProcgenZone("west", KsProcgenZoneKind.Passage, west),
            new KsProcgenZone("south", KsProcgenZoneKind.Passage, south),
            new KsProcgenZone("room", KsProcgenZoneKind.RoomProposal, room));

        var result = KsProcgenPartitionPlanner.Plan(fill, west.Concat(south).ToHashSet());

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenPartitionStatus.OpenFallback));
            Assert.That(result.DoorOpenings, Is.Empty);
            Assert.That(result.WallCells, Is.Empty);
        });
    }

    [Test]
    public void BadSplitMergesLocallyAndKeepsAnotherIslandPartition()
    {
        var goodPassage = Row(0);
        var goodRoom = Row(1).Concat(Row(2)).Concat(Row(3)).ToArray();
        var thinPassage = Row(0).Select(cell => cell + new Vector2i(10, 0)).ToArray();
        var thinRoom = Row(1).Select(cell => cell + new Vector2i(10, 0)).ToArray();
        var fill = Fill(new KsProcgenZone("a-passage", KsProcgenZoneKind.Passage, goodPassage),
            new KsProcgenZone("b-room", KsProcgenZoneKind.RoomProposal, goodRoom),
            new KsProcgenZone("c-passage", KsProcgenZoneKind.Passage, thinPassage),
            new KsProcgenZone("d-room", KsProcgenZoneKind.RoomProposal, thinRoom));
        var reserved = goodPassage.Concat(thinPassage).ToHashSet();

        var result = KsProcgenPartitionPlanner.Plan(fill, reserved);
        var replay = KsProcgenPartitionPlanner.Plan(fill, reserved);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenPartitionStatus.MergedFallback));
            Assert.That(result.RejectedSplits, Is.EqualTo(1));
            Assert.That(result.MergedZoneGroups.Single(), Is.EquivalentTo(new[] { "c-passage", "d-room" }));
            Assert.That(result.WallCells, Is.EquivalentTo(new[] { new Vector2i(1, 1), new Vector2i(2, 1) }));
            Assert.That(result.DoorOpenings.Count, Is.EqualTo(1));
            Assert.That(result.FloorCells, Is.EqualTo(replay.FloorCells));
            Assert.That(result.WallCells, Is.EqualTo(replay.WallCells));
            Assert.That(result.DoorOpenings, Is.EqualTo(replay.DoorOpenings));
        });
    }

    [Test]
    public void MergeBudgetFallsBackWithoutPartialWalls()
    {
        var passage = Row(0);
        var room = Row(1);
        var fill = Fill(new KsProcgenZone("passage", KsProcgenZoneKind.Passage, passage),
            new KsProcgenZone("room", KsProcgenZoneKind.RoomProposal, room));

        var result = KsProcgenPartitionPlanner.Plan(fill, passage.ToHashSet(), maxMergeAttempts: 0);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenPartitionStatus.OpenFallback));
            Assert.That(result.Issue?.Code, Is.EqualTo("PartitionMergeBudget"));
            Assert.That(result.WallCells, Is.Empty);
            Assert.That(result.DoorOpenings, Is.Empty);
            Assert.That(result.FloorCells.Count, Is.EqualTo(6));
        });
    }

    private static Vector2i[] Row(int y) => [new(0, y), new(1, y), new(2, y)];

    private static KsProcgenPureFillResult Fill(params KsProcgenZone[] zones) => new()
    {
        Status = KsProcgenPureFillStatus.Proposed,
        Zones = zones,
        ProceduralCells = zones.Sum(zone => zone.Cells.Count),
    };
}
