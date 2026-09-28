using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenGasClosureTests
{
    private static readonly Vector2i[] Cardinal = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    [Test]
    public void ClosedStripIsOnlyPreliminaryAndMissingSideIsUnknown()
    {
        var cells = Enumerable.Range(0, 3).Select(x => new Vector2i(x, 0)).ToArray();
        var edges = StripEdges(cells);
        var closed = KsProcgenGasClosure.Check(new KsProcgenGasSnapshot
        {
            GasCells = cells,
            ProtectedCells = [cells[0]],
            Edges = edges,
        });
        var missing = KsProcgenGasClosure.Check(new KsProcgenGasSnapshot
        {
            GasCells = cells,
            ProtectedCells = [cells[0]],
            Edges = edges.Where(edge => !(edge.First == cells[0] &&
                edge.Second == new Vector2i(0, 1))).ToArray(),
        });

        Assert.Multiple(() =>
        {
            Assert.That(closed.Status, Is.EqualTo(KsProcgenGasClosureStatus.PreliminarilyClosed));
            Assert.That(closed.ReachedGasCells.Count, Is.EqualTo(3));
            Assert.That(closed.MaterializedEngineVerified, Is.False);
            Assert.That(missing.Status, Is.EqualTo(KsProcgenGasClosureStatus.UnverifiedBoundary));
            Assert.That(missing.UnknownEdges.Select(edge => edge.Second),
                Does.Contain(new Vector2i(0, 1)));
        });
    }

    [Test]
    public void ExplicitVacuumLeakWinsOverUnknownAndClosedDoorStopsReach()
    {
        var cells = Enumerable.Range(0, 3).Select(x => new Vector2i(x, 0)).ToArray();
        var edges = StripEdges(cells);
        var outlet = new Vector2i(3, 0);
        edges.RemoveAll(edge => edge.First == cells[2] && edge.Second == outlet);
        edges.Add(new KsProcgenGasEdge(cells[2], outlet, KsProcgenGasEdgeState.Open));
        var leaking = KsProcgenGasClosure.Check(new KsProcgenGasSnapshot
        {
            GasCells = cells,
            ProtectedCells = [cells[0]],
            VacuumCells = [outlet],
            Edges = edges.Where(edge => !(edge.First == cells[0] &&
                edge.Second == new Vector2i(0, 1))).ToArray(),
        });
        var closedDoor = KsProcgenGasClosure.Check(new KsProcgenGasSnapshot
        {
            GasCells = cells,
            ProtectedCells = [cells[0]],
            VacuumCells = [outlet],
            Edges = edges.Select(edge => edge.First == cells[1] && edge.Second == cells[2]
                ? edge with { State = KsProcgenGasEdgeState.Blocked } : edge).ToArray(),
        });

        Assert.Multiple(() =>
        {
            Assert.That(leaking.Status, Is.EqualTo(KsProcgenGasClosureStatus.LeakToVacuum));
            Assert.That(leaking.LeakEdges.Select(edge => edge.Second), Does.Contain(outlet));
            Assert.That(leaking.UnknownEdges, Is.Not.Empty);
            Assert.That(closedDoor.Status, Is.EqualTo(KsProcgenGasClosureStatus.PreliminarilyClosed));
            Assert.That(closedDoor.ReachedGasCells.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void DuplicateAndBudgetInputsFailWithoutClosureClaim()
    {
        var cell = new Vector2i(0, 0);
        var side = new Vector2i(0, 1);
        var duplicate = KsProcgenGasClosure.Check(new KsProcgenGasSnapshot
        {
            GasCells = [cell],
            ProtectedCells = [cell],
            Edges =
            [
                new KsProcgenGasEdge(cell, side, KsProcgenGasEdgeState.Blocked),
                new KsProcgenGasEdge(side, cell, KsProcgenGasEdgeState.Blocked),
            ],
        });
        var budget = KsProcgenGasClosure.Check(new KsProcgenGasSnapshot
        {
            GasCells = [cell, side],
            ProtectedCells = [cell],
        }, maxGasCells: 1);
        var noProtected = KsProcgenGasClosure.Check(new KsProcgenGasSnapshot
        {
            GasCells = [cell],
            Edges = [new KsProcgenGasEdge(cell, side, KsProcgenGasEdgeState.Blocked)],
        });

        Assert.Multiple(() =>
        {
            Assert.That(duplicate.Status, Is.EqualTo(KsProcgenGasClosureStatus.InvalidInput));
            Assert.That(duplicate.Issue?.Code, Is.EqualTo("DuplicateGasEdge"));
            Assert.That(budget.Status, Is.EqualTo(KsProcgenGasClosureStatus.BudgetExceeded));
            Assert.That(noProtected.Status, Is.EqualTo(KsProcgenGasClosureStatus.InvalidInput));
        });
    }

    [Test]
    public void UnlistedSharedEdgeIsReportedOnce()
    {
        var first = new Vector2i(0, 0);
        var second = new Vector2i(1, 0);
        var edges = StripEdges([first, second]);
        edges.RemoveAll(edge => edge.First == first && edge.Second == second);
        var result = KsProcgenGasClosure.Check(new KsProcgenGasSnapshot
        {
            GasCells = [first, second],
            ProtectedCells = [first, second],
            Edges = edges,
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenGasClosureStatus.UnverifiedBoundary));
            Assert.That(result.UnknownEdges.Count, Is.EqualTo(1));
        });
    }

    private static List<KsProcgenGasEdge> StripEdges(IReadOnlyCollection<Vector2i> cells)
    {
        var gas = cells.ToHashSet();
        var result = new List<KsProcgenGasEdge>();
        foreach (var cell in cells)
        foreach (var offset in Cardinal)
        {
            var neighbor = cell + offset;
            if (gas.Contains(neighbor) && neighbor.X < cell.X)
                continue;
            result.Add(new KsProcgenGasEdge(cell, neighbor,
                gas.Contains(neighbor) ? KsProcgenGasEdgeState.Open :
                KsProcgenGasEdgeState.Blocked));
        }
        return result;
    }
}
