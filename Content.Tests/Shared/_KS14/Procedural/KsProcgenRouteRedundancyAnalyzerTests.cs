using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenRouteRedundancyAnalyzerTests
{
    [Test]
    public void SingleCorridorAndTwoWideStripHaveDifferentRouteCounts()
    {
        var corridor = Enumerable.Range(0, 3).Select(x => new Vector2i(x, 0)).ToHashSet();
        var strip = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 2)
            .Select(y => new Vector2i(x, y))).ToHashSet();
        var pair = new KsProcgenRoutePair("Doors", new Vector2i(0, 0), new Vector2i(2, 0));
        var one = KsProcgenRouteRedundancyAnalyzer.Analyze(corridor,
            new HashSet<Vector2i>(), [pair], maxIndependentRoutes: 3);
        var two = KsProcgenRouteRedundancyAnalyzer.Analyze(strip,
            new HashSet<Vector2i>(), [pair], maxIndependentRoutes: 3);

        Assert.Multiple(() =>
        {
            Assert.That(one.Status, Is.EqualTo(KsProcgenRouteRedundancyStatus.Complete));
            Assert.That(one.Pairs[0].IndependentRoutes, Is.EqualTo(1));
            Assert.That(one.Pairs[0].AtLeastConfiguredCap, Is.False);
            Assert.That(two.Status, Is.EqualTo(KsProcgenRouteRedundancyStatus.Complete));
            Assert.That(two.Pairs[0].IndependentRoutes, Is.EqualTo(2));
            Assert.That(two.Pairs[0].AtLeastConfiguredCap, Is.False);
            Assert.That(two.EngineTraversalVerified, Is.False);
        });
    }

    [Test]
    public void ThreeIndependentRoutesCanBeReportedAsLowerBoundAtCap()
    {
        var room = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 3)
            .Select(y => new Vector2i(x, y))).ToHashSet();
        var pair = new KsProcgenRoutePair("Sides", new Vector2i(0, 1), new Vector2i(2, 1));
        var exact = KsProcgenRouteRedundancyAnalyzer.Analyze(room,
            new HashSet<Vector2i>(), [pair], maxIndependentRoutes: 4);
        var capped = KsProcgenRouteRedundancyAnalyzer.Analyze(room,
            new HashSet<Vector2i>(), [pair], maxIndependentRoutes: 2);

        Assert.Multiple(() =>
        {
            Assert.That(exact.Pairs[0].IndependentRoutes, Is.EqualTo(3));
            Assert.That(exact.Pairs[0].AtLeastConfiguredCap, Is.False);
            Assert.That(capped.Pairs[0].IndependentRoutes, Is.EqualTo(2));
            Assert.That(capped.Pairs[0].AtLeastConfiguredCap, Is.True);
        });
    }

    [Test]
    public void VaultOnlyTilesRemoveNominalDetours()
    {
        var room = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 3)
            .Select(y => new Vector2i(x, y))).ToHashSet();
        var vaultOnly = new HashSet<Vector2i> { new(1, 0), new(1, 2) };
        var result = KsProcgenRouteRedundancyAnalyzer.Analyze(room, vaultOnly,
            [new KsProcgenRoutePair("Sides", new Vector2i(0, 1), new Vector2i(2, 1))]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenRouteRedundancyStatus.Complete));
            Assert.That(result.Pairs[0].IndependentRoutes, Is.EqualTo(1));
        });
    }

    [Test]
    public void DisconnectedPairAndFlowBudgetReturnNoPartialResults()
    {
        var pair = new KsProcgenRoutePair("Sides", new Vector2i(0, 0), new Vector2i(2, 0));
        var disconnected = KsProcgenRouteRedundancyAnalyzer.Analyze(
            new HashSet<Vector2i> { new(0, 0), new(2, 0) },
            new HashSet<Vector2i>(), [pair]);
        var corridor = Enumerable.Range(0, 3).Select(x => new Vector2i(x, 0)).ToHashSet();
        var budget = KsProcgenRouteRedundancyAnalyzer.Analyze(corridor,
            new HashSet<Vector2i>(), [pair], maxExaminedFlowEdges: 1);

        Assert.Multiple(() =>
        {
            Assert.That(disconnected.Status, Is.EqualTo(KsProcgenRouteRedundancyStatus.InvalidInput));
            Assert.That(disconnected.Issue?.Code, Is.EqualTo("RoutePairDisconnected"));
            Assert.That(disconnected.Pairs, Is.Empty);
            Assert.That(budget.Status, Is.EqualTo(KsProcgenRouteRedundancyStatus.BudgetExceeded));
            Assert.That(budget.Issue?.Code, Is.EqualTo("RouteRedundancyFlowBudget"));
            Assert.That(budget.Pairs, Is.Empty);
        });
    }
}
