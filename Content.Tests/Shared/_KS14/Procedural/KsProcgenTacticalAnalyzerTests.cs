using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenTacticalAnalyzerTests
{
    [Test]
    public void NarrowPassageHasExactChokesBridgesAndTerminalSides()
    {
        var corridor = Enumerable.Range(0, 5).Select(x => new Vector2i(x, 0)).ToHashSet();
        var terminals = new HashSet<Vector2i> { new(0, 0), new(4, 0) };

        var result = KsProcgenTacticalAnalyzer.Analyze(corridor,
            new HashSet<Vector2i>(), terminals);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenTacticalStatus.Complete));
            Assert.That(result.ArticulationCells,
                Is.EquivalentTo(new[] { new Vector2i(1, 0), new Vector2i(2, 0), new Vector2i(3, 0) }));
            Assert.That(result.BridgeEdges.Count, Is.EqualTo(4));
            var center = result.ChokeDetails.Single(detail => detail.Cell == new Vector2i(2, 0));
            Assert.That(center.Sides.Select(side => side.CleanCells), Is.EquivalentTo(new[] { 2, 2 }));
            Assert.That(center.Sides.Select(side => side.RequiredTerminals), Is.EquivalentTo(new[] { 1, 1 }));
        });
    }

    [Test]
    public void OpenRoomHasNoMovementChokeAndVaultingCreatesOne()
    {
        var room = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 3)
            .Select(y => new Vector2i(x, y))).ToHashSet();
        var clear = KsProcgenTacticalAnalyzer.Analyze(room, new HashSet<Vector2i>(),
            new HashSet<Vector2i>());
        var vaultOnly = new HashSet<Vector2i> { new(1, 0), new(1, 2) };
        var constrained = KsProcgenTacticalAnalyzer.Analyze(room, vaultOnly,
            new HashSet<Vector2i> { new(0, 1), new(2, 1) });

        Assert.Multiple(() =>
        {
            Assert.That(clear.ArticulationCells, Is.Empty);
            Assert.That(clear.BridgeEdges, Is.Empty);
            Assert.That(constrained.ArticulationCells, Does.Contain(new Vector2i(1, 1)));
            Assert.That(constrained.CleanCells, Is.EqualTo(7));
        });
    }

    [Test]
    public void DetailAndCellBudgetsAreExplicit()
    {
        var corridor = Enumerable.Range(0, 5).Select(x => new Vector2i(x, 0)).ToHashSet();
        var details = KsProcgenTacticalAnalyzer.Analyze(corridor,
            new HashSet<Vector2i>(), new HashSet<Vector2i>(), maxDetailedChokes: 1);
        var rejected = KsProcgenTacticalAnalyzer.Analyze(corridor,
            new HashSet<Vector2i>(), new HashSet<Vector2i>(), maxCells: 4);

        Assert.Multiple(() =>
        {
            Assert.That(details.Status, Is.EqualTo(KsProcgenTacticalStatus.DetailsTruncated));
            Assert.That(details.ArticulationCells.Count, Is.EqualTo(3));
            Assert.That(details.ChokeDetails.Count, Is.EqualTo(1));
            Assert.That(rejected.Status, Is.EqualTo(KsProcgenTacticalStatus.BudgetExceeded));
            Assert.That(rejected.ArticulationCells, Is.Empty);
        });
    }
}
