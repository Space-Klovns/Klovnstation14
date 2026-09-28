using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenPureFillPlannerTests
{
    [Test]
    public void TinyOneByThreeFallsBackToUnpartitionedPassage()
    {
        var request = Request([new(0, 0), new(0, 1), new(0, 2)]);
        var (shape, packing) = Pack(request);
        var fill = KsProcgenPureFillPlanner.Plan(shape, packing, request.Seed);

        Assert.Multiple(() =>
        {
            Assert.That(fill.Status, Is.EqualTo(KsProcgenPureFillStatus.Proposed));
            Assert.That(fill.ProceduralCells, Is.EqualTo(3));
            Assert.That(fill.TinyPassageComponents, Is.EqualTo(1));
            Assert.That(fill.Zones.Single().Kind, Is.EqualTo(KsProcgenZoneKind.Passage));
            Assert.That(fill.Zones.Single().Cells, Is.EquivalentTo(request.Shape.Cells));
        });
    }

    [Test]
    public void ConcaveFillKeepsReservedPassageAndHasExactConnectedCoverage()
    {
        var cells = Enumerable.Range(0, 6).SelectMany(x => Enumerable.Range(0, 5)
                .Select(y => new Vector2i(x, y)))
            .Where(cell => cell is not { X: 2, Y: 2 } and not { X: 3, Y: 2 }).ToList();
        var request = Request(cells);
        var (shape, packing) = Pack(request);
        var reserved = new HashSet<Vector2i>(Enumerable.Range(0, 6).Select(x => new Vector2i(x, 0)));
        var routed = new KsProcgenPackingResult
        {
            Status = packing.Status,
            CellClaims = packing.CellClaims,
            ResidualRouting = new KsProcgenResidualResult
            {
                Status = KsProcgenResidualStatus.PreliminaryReady,
                ReservedPassageCells = reserved,
            },
        };

        var first = KsProcgenPureFillPlanner.Plan(shape, routed, request.Seed, preferredMaxRoomCells: 6);
        var replay = KsProcgenPureFillPlanner.Plan(shape, routed, request.Seed, preferredMaxRoomCells: 6);
        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(KsProcgenPureFillStatus.Proposed));
            Assert.That(first.Zones.SelectMany(zone => zone.Cells), Is.EquivalentTo(cells));
            Assert.That(first.Zones.Sum(zone => zone.Cells.Count), Is.EqualTo(cells.Count));
            Assert.That(first.Zones.Where(zone => zone.Kind == KsProcgenZoneKind.RoomProposal)
                .SelectMany(zone => zone.Cells).Intersect(reserved), Is.Empty);
            Assert.That(first.Zones.All(zone => KsProcgenGeometry.ConnectedComponents(zone.Cells.ToHashSet()).Count == 1), Is.True);
            Assert.That(first.Zones.Select(zone => (zone.Id, zone.Kind, Cells: string.Join(";", zone.Cells))),
                Is.EqualTo(replay.Zones.Select(zone => (zone.Id, zone.Kind, Cells: string.Join(";", zone.Cells)))));
            Assert.That(first.CardinalInterfaces, Is.EqualTo(replay.CardinalInterfaces));
            Assert.That(first.CardinalInterfaces.Count, Is.GreaterThan(0));
        });
    }

    [Test]
    public void InvalidRouteAndBudgetNeverReturnPartialZones()
    {
        var request = Request([new(0, 0), new(1, 0), new(0, 1), new(1, 1)]);
        var (shape, packing) = Pack(request);
        var invalid = new KsProcgenPackingResult
        {
            Status = packing.Status,
            CellClaims = packing.CellClaims,
            ResidualRouting = new KsProcgenResidualResult
            {
                Status = KsProcgenResidualStatus.PreliminaryReady,
                ReservedPassageCells = new HashSet<Vector2i> { new(10, 10) },
            },
        };
        var badRoute = KsProcgenPureFillPlanner.Plan(shape, invalid, 0);
        var budget = KsProcgenPureFillPlanner.Plan(shape, packing, 0, maxProceduralCells: 3);

        Assert.Multiple(() =>
        {
            Assert.That(badRoute.Status, Is.EqualTo(KsProcgenPureFillStatus.InvalidInput));
            Assert.That(badRoute.Issue?.Code, Is.EqualTo("InvalidPureFillRoute"));
            Assert.That(badRoute.Zones, Is.Empty);
            Assert.That(budget.Status, Is.EqualTo(KsProcgenPureFillStatus.BudgetExceeded));
            Assert.That(budget.Zones, Is.Empty);
        });
    }

    private static KsProcgenRequest Request(List<Vector2i> cells) => new()
    {
        RequestId = "PureFill",
        Seed = 19,
        Shape = new KsProcgenShapeSpec { Cells = cells },
    };

    private static (KsProcgenNormalizedShape Shape, KsProcgenPackingResult Packing) Pack(KsProcgenRequest request)
    {
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Message);
        var packing = KsProcgenPackingPlanner.Plan(request, []);
        Assert.That(packing.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady), packing.Issue?.Message);
        return (shape!, packing);
    }
}
