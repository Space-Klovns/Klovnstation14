using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenResidualConnectorTests
{
    [Test]
    public void SelectedPortRoutesAcrossProceduralGapToRoot()
    {
        var request = CorridorRequest();
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Message);
        var packing = KsProcgenPackingPlanner.Plan(request, [LeftFamily()],
            new KsProcgenPackingBudgets { PreferredPrefabCoveragePercent = 40 });
        Assert.That(packing.Placements.Count, Is.EqualTo(1));
        Assert.That(packing.Placements[0].Origin, Is.EqualTo(new Vector2i(0, 0)));

        var result = KsProcgenResidualConnector.Connect(shape!, packing,
            [new Vector2i(4, 0)], new HashSet<Vector2i>());
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenResidualStatus.PreliminaryReady));
            Assert.That(result.ReservedPassageCells,
                Is.EquivalentTo(new[] { new Vector2i(2, 0), new Vector2i(3, 0), new Vector2i(4, 0) }));
            Assert.That(result.DirectPortPairs, Is.Empty);
        });
    }

    [Test]
    public void PreservedBarrierReportsTheUnreachablePort()
    {
        var request = CorridorRequest();
        request.Shape.PreservedCells.Add(new Vector2i(3, 0));
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Message);
        var packing = KsProcgenPackingPlanner.Plan(request, [LeftFamily()],
            new KsProcgenPackingBudgets { PreferredPrefabCoveragePercent = 50 });
        Assert.That(packing.Placements.Single().Origin, Is.EqualTo(new Vector2i(0, 0)));

        var result = KsProcgenResidualConnector.Connect(shape!, packing,
            [new Vector2i(4, 0)], new HashSet<Vector2i>());
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenResidualStatus.NoRoute));
            Assert.That(result.ReservedPassageCells, Is.Empty);
            Assert.That(result.UnreachablePortIds.Single(), Does.EndWith("/Door"));
        });
    }

    [Test]
    public void OppositeAdjacentPortsFormOneDirectPair()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "Paired",
            Mode = KsProcgenMode.Prefabs,
            Shape = new KsProcgenShapeSpec
            {
                Cells =
                [
                    new Vector2i(0, 0), new Vector2i(1, 0),
                    new Vector2i(2, 0), new Vector2i(3, 0),
                ],
            },
        };
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Message);
        var packing = KsProcgenPackingPlanner.Plan(request, [LeftFamily(), RightFamily()]);
        Assert.That(packing.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady));
        Assert.That(packing.Placements.Count, Is.EqualTo(2));

        var result = KsProcgenResidualConnector.Connect(shape!, packing, [], new HashSet<Vector2i>());
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenResidualStatus.PreliminaryReady));
            Assert.That(result.DirectPortPairs.Count, Is.EqualTo(1));
            Assert.That(result.ReservedPassageCells, Is.Empty);
        });

        var inspectedThresholds = packing.Placements.SelectMany(placement => placement.Ports)
            .Select(port => port.Threshold).ToHashSet();
        var withInspectedThresholds = KsProcgenResidualConnector.Connect(shape!, packing, [], inspectedThresholds);
        Assert.That(withInspectedThresholds.DirectPortPairs.Count, Is.EqualTo(1));
        Assert.That(withInspectedThresholds.ReservedPassageCells, Is.Empty);
    }

    private static KsProcgenRequest CorridorRequest() => new()
    {
        RequestId = "ResidualCorridor",
        Mode = KsProcgenMode.Hybrid,
        Shape = new KsProcgenShapeSpec
        {
            Cells =
            [
                new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0),
                new Vector2i(3, 0), new Vector2i(4, 0),
            ],
        },
    };

    private static KsProcgenLayoutFamily LeftFamily()
    {
        var cells = new[] { new Vector2i(0, 0), new Vector2i(1, 0) };
        var room = new KsProcgenRoomShape("Left", cells,
            [new KsProcgenBoundaryEdge(cells[0], new Vector2i(-1, 0))],
            [new KsProcgenRoomPort("Door", cells[1], new Vector2i(1, 0))]);
        return new KsProcgenLayoutFamily("LeftFamily",
            [new KsProcgenLayoutOption("LeftOption", cells, [room])]);
    }

    private static KsProcgenLayoutFamily RightFamily()
    {
        var cells = new[] { new Vector2i(0, 0), new Vector2i(1, 0) };
        var room = new KsProcgenRoomShape("Right", cells,
            [new KsProcgenBoundaryEdge(cells[1], new Vector2i(1, 0))],
            [new KsProcgenRoomPort("Door", cells[0], new Vector2i(-1, 0))]);
        return new KsProcgenLayoutFamily("RightFamily",
            [new KsProcgenLayoutOption("RightOption", cells, [room])]);
    }
}
