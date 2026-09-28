using System.Collections.Generic;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenPortNetworkAnalyzerTests
{
    [Test]
    public void OppositeConstantPortsJoinRoomsWithoutProceduralFloor()
    {
        var request = Request(withEastPort: true);
        var (shape, packing) = Plan(request);

        var network = KsProcgenPortNetworkAnalyzer.Analyze(shape, packing, new HashSet<Vector2i>(), []);

        Assert.Multiple(() =>
        {
            Assert.That(network.Status, Is.EqualTo(KsProcgenPortNetworkStatus.Connected));
            Assert.That(network.Groups, Has.Count.EqualTo(1));
            Assert.That(network.Groups[0].RoomIds, Is.EquivalentTo(new[] { "constant:East", "constant:West" }));
            Assert.That(network.Groups[0].PassageCells, Is.Zero);
            Assert.That(network.EngineAccessVerified, Is.False);
        });
    }

    [Test]
    public void UnconnectedConstantIsReportedEvenWhenPackingHasCompleteCover()
    {
        var request = Request(withEastPort: true);
        request.Shape.Cells.Add(new Vector2i(8, 0));
        request.ConstantRegions.Add(new KsProcgenConstantRegionSpec
        {
            Id = "Isolated",
            SourceId = "IsolatedMap",
            ContentFingerprint = "fixture",
            Origin = new Vector2i(8, 0),
            LocalCells = [new Vector2i(0, 0)],
        });
        var (shape, packing) = Plan(request);

        var network = KsProcgenPortNetworkAnalyzer.Analyze(shape, packing, new HashSet<Vector2i>(), []);

        Assert.That(network.Status, Is.EqualTo(KsProcgenPortNetworkStatus.Disconnected));
        Assert.That(network.Groups, Has.Count.EqualTo(2));
        Assert.That(network.RoomsWithoutDeclaredPorts, Is.EqualTo(new[] { "constant:Isolated" }));
    }

    [Test]
    public void ConstantPortJoinsProceduralPassageAndRoot()
    {
        var request = Request(withEastPort: false);
        request.ConstantRegions.RemoveAt(1);
        request.RootCells = [new Vector2i(2, 0)];
        var (shape, packing) = Plan(request);

        var network = KsProcgenPortNetworkAnalyzer.Analyze(shape, packing, new HashSet<Vector2i>(), request.RootCells);

        Assert.That(network.Status, Is.EqualTo(KsProcgenPortNetworkStatus.Connected));
        Assert.That(network.Groups, Has.Count.EqualTo(1));
        Assert.That(network.Groups[0].PassageCells, Is.EqualTo(2));
        Assert.That(network.Groups[0].RootCells, Is.EqualTo(1));
    }

    [Test]
    public void NodeBudgetReportsExhaustion()
    {
        var request = Request(withEastPort: true);
        var (shape, packing) = Plan(request);

        var network = KsProcgenPortNetworkAnalyzer.Analyze(shape, packing, new HashSet<Vector2i>(), [], maxNodes: 1);

        Assert.That(network.Status, Is.EqualTo(KsProcgenPortNetworkStatus.BudgetExceeded));
        Assert.That(network.Issue?.Code, Is.EqualTo("PortNetworkNodeBudget"));
    }

    [Test]
    public void InspectedRootInsideConstantJoinsItsRoom()
    {
        var cell = new Vector2i(5, 5);
        var request = new KsProcgenRequest
        {
            RequestId = "ConstantRoot",
            Mode = KsProcgenMode.Prefabs,
            Shape = new KsProcgenShapeSpec { Cells = [cell] },
            RootCells = [cell],
            ConstantRegions =
            [
                new KsProcgenConstantRegionSpec
                {
                    Id = "Arrival",
                    SourceId = "ArrivalMap",
                    ContentFingerprint = "fixture",
                    Origin = cell,
                    LocalCells = [new Vector2i(0, 0)],
                },
            ],
        };
        var inspected = new HashSet<Vector2i> { cell };
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True,
            issue?.Message);
        var packing = KsProcgenPackingPlanner.Plan(request, [], inspectedExistingPassages: inspected);
        Assert.That(packing.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady), packing.Issue?.Message);

        var network = KsProcgenPortNetworkAnalyzer.Analyze(shape!, packing, inspected, request.RootCells);

        Assert.That(network.Status, Is.EqualTo(KsProcgenPortNetworkStatus.Connected));
        Assert.That(network.Groups, Has.Count.EqualTo(1));
        Assert.That(network.Groups[0].RoomIds, Is.EqualTo(new[] { "constant:Arrival" }));
        Assert.That(network.Groups[0].RootCells, Is.EqualTo(1));
    }

    [Test]
    public void PartitionWallsCannotServeAsAbstractPassages()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "PartitionedNetwork",
            Shape = new KsProcgenShapeSpec
            {
                Cells = [new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0)],
            },
        };
        var (shape, packing) = Plan(request);
        var divided = new KsProcgenPartitionResult
        {
            Status = KsProcgenPartitionStatus.Proposed,
            FloorCells = [new Vector2i(0, 0), new Vector2i(2, 0)],
            WallCells = [new Vector2i(1, 0)],
        };
        var disconnected = KsProcgenPortNetworkAnalyzer.AnalyzePartitioned(shape, packing,
            divided, new HashSet<Vector2i>(), []);
        Assert.That(disconnected.Status, Is.EqualTo(KsProcgenPortNetworkStatus.Disconnected));
        Assert.That(disconnected.Groups, Has.Count.EqualTo(2));

        var door = new KsProcgenPartitionResult
        {
            Status = KsProcgenPartitionStatus.Proposed,
            FloorCells = request.Shape.Cells,
            DoorOpenings =
            [
                new KsProcgenPartitionDoor("left", "right", new Vector2i(1, 0),
                    new Vector2i(0, 0), new Vector2i(2, 0)),
            ],
        };
        var connected = KsProcgenPortNetworkAnalyzer.AnalyzePartitioned(shape, packing,
            door, new HashSet<Vector2i>(), []);
        Assert.That(connected.Status, Is.EqualTo(KsProcgenPortNetworkStatus.Connected));
        Assert.That(connected.Groups, Has.Count.EqualTo(1));

        var malformed = new KsProcgenPartitionResult
        {
            Status = KsProcgenPartitionStatus.Proposed,
            FloorCells = request.Shape.Cells,
            DoorOpenings =
            [
                new KsProcgenPartitionDoor("left", "right", new Vector2i(1, 0),
                    new Vector2i(0, 0), new Vector2i(0, 0)),
            ],
        };
        var invalid = KsProcgenPortNetworkAnalyzer.AnalyzePartitioned(shape, packing,
            malformed, new HashSet<Vector2i>(), []);
        Assert.That(invalid.Status, Is.EqualTo(KsProcgenPortNetworkStatus.InvalidInput));
    }

    private static (KsProcgenNormalizedShape Shape, KsProcgenPackingResult Packing) Plan(KsProcgenRequest request)
    {
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True,
            issue?.Message);
        var packing = KsProcgenPackingPlanner.Plan(request, []);
        Assert.That(packing.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady), packing.Issue?.Message);
        return (shape!, packing);
    }

    private static KsProcgenRequest Request(bool withEastPort) => new()
    {
        RequestId = "PortNetwork",
        Mode = KsProcgenMode.Hybrid,
        Shape = new KsProcgenShapeSpec
        {
            Cells = [new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0), new Vector2i(3, 0)],
        },
        ConstantRegions =
        [
            new KsProcgenConstantRegionSpec
            {
                Id = "West",
                SourceId = "WestMap",
                ContentFingerprint = "fixture",
                LocalCells = [new Vector2i(0, 0), new Vector2i(1, 0)],
                Ports =
                [
                    new KsProcgenConstantPortSpec
                    {
                        Id = "EastDoor",
                        Threshold = new Vector2i(1, 0),
                        OutwardNormal = new Vector2i(1, 0),
                    },
                ],
            },
            new KsProcgenConstantRegionSpec
            {
                Id = "East",
                SourceId = "EastMap",
                ContentFingerprint = "fixture",
                Origin = new Vector2i(2, 0),
                LocalCells = [new Vector2i(0, 0), new Vector2i(1, 0)],
                Ports = withEastPort
                    ?
                    [
                        new KsProcgenConstantPortSpec
                        {
                            Id = "WestDoor",
                            Threshold = new Vector2i(0, 0),
                            OutwardNormal = new Vector2i(-1, 0),
                        },
                    ]
                    : [],
            },
        ],
    };
}
