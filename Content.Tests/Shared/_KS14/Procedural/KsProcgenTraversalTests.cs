using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenTraversalTests
{
    [Test]
    public void RoomDoorsAndChairNeedAnUninterruptedCleanRoute()
    {
        var snapshot = CreateHallRoom();
        snapshot.InteractionApproaches.Add(new KsProcgenInteractionApproach("Chair", "Hall", new Vector2i(2, 0)));
        Assert.That(KsProcgenTraversal.Validate(snapshot).Valid, Is.True);

        // A table/desk or any equivalent vault-only obstacle has this movement classification.
        snapshot.VaultOnlyCells.Add(new Vector2i(2, 0));
        var blockedReport = KsProcgenTraversal.Validate(snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(blockedReport.Valid, Is.False);
            Assert.That(blockedReport.Issues.Select(issue => issue.Code), Does.Contain("RoomPortsDisconnected"));
            Assert.That(blockedReport.Issues.Select(issue => issue.Code), Does.Contain("RoomInteractionDisconnected"));
            Assert.That(KsProcgenTraversal.FindCleanPath(new Vector2i(1, 0), new Vector2i(3, 0),
                snapshot.WalkableCells, snapshot.VaultOnlyCells), Is.Empty);
        });
    }

    [Test]
    public void OutsideDetourCannotSatisfyRoomLocalConnection()
    {
        var snapshot = CreateHallRoom();
        snapshot.VaultOnlyCells.Add(new Vector2i(2, 0));
        foreach (var cell in new[]
                 {
                     new Vector2i(0, 1), new Vector2i(1, 1), new Vector2i(2, 1),
                     new Vector2i(3, 1), new Vector2i(4, 1),
                 })
            snapshot.WalkableCells.Add(cell);

        var report = KsProcgenTraversal.Validate(snapshot);
        Assert.That(report.Issues.Select(issue => issue.Code), Does.Contain("RoomPortsDisconnected"));
        Assert.That(KsProcgenTraversal.FindCleanPath(new Vector2i(-1, 0), new Vector2i(5, 0),
            snapshot.WalkableCells, snapshot.VaultOnlyCells), Is.Not.Empty);
    }

    [Test]
    public void DiagonalTouchAndBlockedPortApproachFail()
    {
        var diagonal = new HashSet<Vector2i> { new(0, 0), new(1, 1) };
        Assert.That(KsProcgenTraversal.FindCleanPath(new Vector2i(0, 0), new Vector2i(1, 1),
            diagonal, new HashSet<Vector2i>()), Is.Empty);

        var snapshot = CreateHallRoom();
        snapshot.VaultOnlyCells.Add(new Vector2i(-1, 0));
        var report = KsProcgenTraversal.Validate(snapshot);
        Assert.That(report.Issues.Select(issue => issue.Code), Does.Contain("PortApproachBlocked"));
    }

    private static KsProcgenTraversalSnapshot CreateHallRoom()
    {
        var snapshot = new KsProcgenTraversalSnapshot();
        for (var x = -1; x <= 5; x++)
            snapshot.WalkableCells.Add(new Vector2i(x, 0));

        snapshot.RoomCells.Add("Hall", new HashSet<Vector2i>
        {
            new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(4, 0),
        });
        snapshot.Ports.Add(new KsProcgenPortGeometry("West", "Hall",
            new Vector2i(0, 0), new Vector2i(-1, 0), new Vector2i(1, 0), new Vector2i(-1, 0)));
        snapshot.Ports.Add(new KsProcgenPortGeometry("East", "Hall",
            new Vector2i(4, 0), new Vector2i(1, 0), new Vector2i(3, 0), new Vector2i(5, 0)));
        snapshot.Roots.Add(new Vector2i(-1, 0));
        return snapshot;
    }
}
