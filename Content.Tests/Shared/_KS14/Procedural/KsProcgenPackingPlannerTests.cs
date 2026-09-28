using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenPackingPlannerTests
{
    [Test]
    public void AutomaticallyChoosesOneCompleteSmallRoomPattern()
    {
        var area = new[] { new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(0, 1), new Vector2i(1, 1) };
        var left = new KsProcgenLayoutOption("L", area,
        [
            new KsProcgenRoomShape("A1", [area[0]]),
            new KsProcgenRoomShape("A2", [area[1]]),
            new KsProcgenRoomShape("A3", [area[2]]),
        ], [area[3]]);
        var four = new KsProcgenLayoutOption("Four", area,
        [
            new KsProcgenRoomShape("B1", [area[0]]),
            new KsProcgenRoomShape("B2", [area[1]]),
            new KsProcgenRoomShape("B3", [area[2]]),
            new KsProcgenRoomShape("B4", [area[3]]),
        ]);
        var family = new KsProcgenLayoutFamily("Block", [left, four]);
        var request = new KsProcgenRequest
        {
            RequestId = "ChoosePattern",
            Mode = KsProcgenMode.Hybrid,
            Shape = new KsProcgenShapeSpec { Cells = area.ToList() },
        };

        var threeRoomPlan = KsProcgenPackingPlanner.Plan(request, [family],
            new KsProcgenPackingBudgets { PreferredPrefabCoveragePercent = 75 });
        Assert.Multiple(() =>
        {
            Assert.That(threeRoomPlan.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady));
            Assert.That(threeRoomPlan.SearchComplete, Is.True);
            Assert.That(threeRoomPlan.Placements.Count, Is.EqualTo(1));
            Assert.That(threeRoomPlan.Placements[0].OptionId, Is.EqualTo("L"));
            Assert.That(threeRoomPlan.CellClaims.Count, Is.EqualTo(4));
            Assert.That(threeRoomPlan.CellClaims.Count(entry => entry.Claim.Disposition == KsProcgenCellDisposition.Prefab), Is.EqualTo(3));
            Assert.That(threeRoomPlan.CellClaims.Any(entry => entry.Claim.OwnerId.Contains("Four")), Is.False);
        });

        var fourRoomPlan = KsProcgenPackingPlanner.Plan(request, [family],
            new KsProcgenPackingBudgets { PreferredPrefabCoveragePercent = 100 });
        Assert.That(fourRoomPlan.Placements.Single().OptionId, Is.EqualTo("Four"));
        Assert.That(fourRoomPlan.PrefabCells, Is.EqualTo(4));
    }

    [Test]
    public void PlacesLargeAndSmallRoomsWithoutPresetSlots()
    {
        var largeCells = new[] { new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(0, 1), new Vector2i(1, 1) };
        var smallCells = new[] { new Vector2i(0, 0), new Vector2i(0, 1) };
        var large = new KsProcgenLayoutFamily("Large",
            [new KsProcgenLayoutOption("Single", largeCells,
            [
                new KsProcgenRoomShape("LargeRoom", largeCells,
                [
                    new KsProcgenBoundaryEdge(new Vector2i(0, 0), new Vector2i(-1, 0)),
                    new KsProcgenBoundaryEdge(new Vector2i(0, 1), new Vector2i(-1, 0)),
                ]),
            ])]);
        var small = new KsProcgenLayoutFamily("Small",
            [new KsProcgenLayoutOption("Single", smallCells, [new KsProcgenRoomShape("SmallRoom", smallCells)])]);
        var request = new KsProcgenRequest
        {
            RequestId = "MixedSizes",
            Mode = KsProcgenMode.Hybrid,
            Shape = new KsProcgenShapeSpec
            {
                AddRectangles =
                [
                    new KsProcgenTileRect { Min = new Vector2i(10, 4), Max = new Vector2i(13, 6) },
                ],
            },
        };

        var result = KsProcgenPackingPlanner.Plan(request, [large, small],
            new KsProcgenPackingBudgets { PreferredPrefabCoveragePercent = 100 });

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady));
            Assert.That(result.SearchComplete, Is.True);
            Assert.That(result.PrefabCells, Is.EqualTo(6));
            Assert.That(result.Placements.Select(placement => placement.FamilyId),
                Is.EquivalentTo(new[] { "Large", "Small" }));
            Assert.That(result.CellClaims.Select(entry => entry.Cell).Distinct().Count(), Is.EqualTo(6));
        });
    }

    [Test]
    public void TinyPureFillAndBudgetedHybridRemainHonest()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "Tiny",
            Mode = KsProcgenMode.Procedural,
            Shape = new KsProcgenShapeSpec
            {
                Cells = [new Vector2i(5, 5), new Vector2i(5, 6), new Vector2i(5, 7)],
            },
        };

        var pure = KsProcgenPackingPlanner.Plan(request, []);
        Assert.Multiple(() =>
        {
            Assert.That(pure.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady));
            Assert.That(pure.SearchComplete, Is.True);
            Assert.That(pure.CellClaims.Count, Is.EqualTo(3));
            Assert.That(pure.CellClaims.All(entry => entry.Claim.Disposition == KsProcgenCellDisposition.ProceduralFloor), Is.True);
        });

        request.Mode = KsProcgenMode.Hybrid;
        var budgeted = KsProcgenPackingPlanner.Plan(request, [],
            new KsProcgenPackingBudgets { MaxSearchNodes = 1 });
        Assert.Multiple(() =>
        {
            Assert.That(budgeted.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady));
            Assert.That(budgeted.SearchComplete, Is.False);
            Assert.That(budgeted.CellClaims.Count, Is.EqualTo(3));
        });

        request.Mode = KsProcgenMode.Prefabs;
        var prefabOnly = KsProcgenPackingPlanner.Plan(request, []);
        Assert.That(prefabOnly.Status, Is.EqualTo(KsProcgenPackingStatus.NoGeometricCover));
    }

    [Test]
    public void PackingSelectsTheAlternativeWhosePortCanReachTheRoot()
    {
        var cells = new[] { new Vector2i(0, 0), new Vector2i(1, 0) };
        var blocked = new KsProcgenLayoutOption("Blocked", cells,
        [
            new KsProcgenRoomShape("Room", cells,
                ports: [new KsProcgenRoomPort("Door", cells[1], new Vector2i(1, 0))]),
        ]);
        var reachable = new KsProcgenLayoutOption("Reachable", cells,
        [
            new KsProcgenRoomShape("Room", cells,
                ports: [new KsProcgenRoomPort("Door", cells[0], new Vector2i(-1, 0))]),
        ]);
        var family = new KsProcgenLayoutFamily("Choice", [blocked, reachable]);
        var request = new KsProcgenRequest
        {
            RequestId = "RouteChoice",
            Mode = KsProcgenMode.Hybrid,
            Shape = new KsProcgenShapeSpec
            {
                Cells =
                [
                    new Vector2i(0, 0), new Vector2i(1, 0),
                    new Vector2i(2, 0), new Vector2i(3, 0),
                ],
                PreservedCells = [new Vector2i(3, 0)],
            },
            RootCells = [new Vector2i(0, 0)],
        };

        var result = KsProcgenPackingPlanner.Plan(request, [family],
            new KsProcgenPackingBudgets { PreferredPrefabCoveragePercent = 100 });
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady));
            Assert.That(result.Placements.Single().OptionId, Is.EqualTo("Reachable"));
            Assert.That(result.Placements.Single().Origin, Is.EqualTo(new Vector2i(1, 0)));
            Assert.That(result.ResidualRouting?.Status, Is.EqualTo(KsProcgenResidualStatus.PreliminaryReady));
            Assert.That(result.ResidualRouting?.ReservedPassageCells, Is.EquivalentTo(new[] { new Vector2i(0, 0) }));
        });
    }

    [Test]
    public void GeometricCoverWithDisconnectedRequiredRootsIsNotReportedReady()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "IsolatedRoots",
            Mode = KsProcgenMode.Procedural,
            Shape = new KsProcgenShapeSpec
            {
                Cells = [new Vector2i(0, 0), new Vector2i(2, 0)],
            },
            RootCells = [new Vector2i(0, 0), new Vector2i(2, 0)],
        };

        var result = KsProcgenPackingPlanner.Plan(request, []);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenPackingStatus.NoPreliminaryRoute));
            Assert.That(result.SearchComplete, Is.True);
            Assert.That(result.Issue?.Code, Is.EqualTo("DisconnectedProceduralFloor"));
            Assert.That(result.Placements, Is.Empty);
        });
    }

    [Test]
    public void PureSingleNetworkChecksEveryFloorCellEvenWithoutRoots()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "PureIslands",
            Mode = KsProcgenMode.Procedural,
            Shape = new KsProcgenShapeSpec
            {
                Cells = [new Vector2i(0, 0), new Vector2i(2, 0)],
            },
        };

        var oneNetwork = KsProcgenPackingPlanner.Plan(request, []);
        Assert.That(oneNetwork.Status, Is.EqualTo(KsProcgenPackingStatus.NoPreliminaryRoute));
        Assert.That(oneNetwork.Issue?.Code, Is.EqualTo("DisconnectedProceduralFloor"));

        request.ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland;
        var perIsland = KsProcgenPackingPlanner.Plan(request, []);
        Assert.That(perIsland.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady));
    }
}
