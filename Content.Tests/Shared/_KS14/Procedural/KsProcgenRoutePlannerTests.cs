using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenRoutePlannerTests
{
    [Test]
    public void ConnectsThreeTerminalsThroughAuthorizedCells()
    {
        var request = new KsProcgenRouteRequest();
        for (var x = 0; x <= 4; x++)
            request.WritableCells.Add(new Vector2i(x, 0));
        request.WritableCells.Add(new Vector2i(2, 1));
        request.WritableCells.Add(new Vector2i(2, 2));
        request.Terminals.AddRange([new Vector2i(0, 0), new Vector2i(4, 0), new Vector2i(2, 2)]);

        var result = KsProcgenRoutePlanner.Connect(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenRouteStatus.Connected));
        Assert.That(result.ReservedCells, Is.EquivalentTo(request.WritableCells));
    }

    [Test]
    public void NeverCrossesProtectedCellsOrDiagonals()
    {
        var request = new KsProcgenRouteRequest { MaxExpandedCells = 1_000 };
        request.WritableCells.UnionWith([new Vector2i(0, 0), new Vector2i(1, 1)]);
        request.Terminals.AddRange([new Vector2i(0, 0), new Vector2i(1, 1)]);

        var result = KsProcgenRoutePlanner.Connect(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenRouteStatus.NoRoute));
        Assert.That(result.ReservedCells, Is.Empty);
    }

    [Test]
    public void PrefersExistingPassageOverCheaperGeometricDistance()
    {
        var request = new KsProcgenRouteRequest();
        request.WritableCells.UnionWith([new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0)]);
        request.ExistingPassageCells.UnionWith(
        [
            new Vector2i(0, 1), new Vector2i(1, 1), new Vector2i(2, 1),
        ]);
        request.Terminals.AddRange([new Vector2i(0, 0), new Vector2i(2, 0)]);

        var result = KsProcgenRoutePlanner.Connect(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenRouteStatus.Connected));
        Assert.That(result.ReservedCells, Is.EquivalentTo(new[] { new Vector2i(0, 0), new Vector2i(2, 0) }));
    }

    [Test]
    public void SearchBudgetIsExplicitAndDoesNotReturnPartialRoute()
    {
        var request = new KsProcgenRouteRequest { MaxExpandedCells = 1 };
        request.WritableCells.UnionWith([new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0)]);
        request.Terminals.AddRange([new Vector2i(0, 0), new Vector2i(2, 0)]);

        var result = KsProcgenRoutePlanner.Connect(request);
        Assert.That(result.Status, Is.EqualTo(KsProcgenRouteStatus.BudgetExceeded));
        Assert.That(result.ReservedCells, Is.Empty);
    }
}
