using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenAlternateRouteAnalyzerTests
{
    [Test]
    public void CorridorChokeDisconnectsPairAndEndpointRemovalIsExplicit()
    {
        var corridor = Enumerable.Range(0, 5).Select(x => new Vector2i(x, 0)).ToHashSet();
        var result = KsProcgenAlternateRouteAnalyzer.Analyze(corridor,
            new HashSet<Vector2i>(),
            [new KsProcgenRoutePair("A-B", new Vector2i(0, 0), new Vector2i(4, 0))],
            new HashSet<Vector2i> { new(2, 0), new(0, 0) });

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenAlternateRouteStatus.Complete));
            Assert.That(result.Pairs[0].BaselineShortestSteps, Is.EqualTo(4));
            Assert.That(result.Pairs[0].ChokesWithAlternateRoute, Is.Zero);
            Assert.That(result.Pairs[0].Chokes[0].EndpointRemoved, Is.True);
            Assert.That(result.Pairs[0].Chokes[1].ConnectedWithoutChoke, Is.False);
            Assert.That(result.Pairs[0].Chokes[1].ShortestStepsWithoutChoke, Is.EqualTo(-1));
            Assert.That(result.Searches, Is.EqualTo(2));
            Assert.That(result.EngineTraversalVerified, Is.False);
        });
    }

    [Test]
    public void LoopRetainsLongerRouteWhenDirectCellIsRemoved()
    {
        var room = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 2)
            .Select(y => new Vector2i(x, y))).ToHashSet();
        var result = KsProcgenAlternateRouteAnalyzer.Analyze(room,
            new HashSet<Vector2i>(),
            [new KsProcgenRoutePair("Doors", new Vector2i(0, 0), new Vector2i(2, 0))],
            new HashSet<Vector2i> { new(1, 0) });

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenAlternateRouteStatus.Complete));
            Assert.That(result.Pairs[0].BaselineShortestSteps, Is.EqualTo(2));
            Assert.That(result.Pairs[0].ChokesWithAlternateRoute, Is.EqualTo(1));
            Assert.That(result.Pairs[0].Chokes[0].ShortestStepsWithoutChoke, Is.EqualTo(4));
        });
    }

    [Test]
    public void VaultOnlyCellsDoNotBecomeAlternateRoutes()
    {
        var room = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 3)
            .Select(y => new Vector2i(x, y))).ToHashSet();
        var vaultOnly = new HashSet<Vector2i> { new(1, 0), new(1, 2) };
        var result = KsProcgenAlternateRouteAnalyzer.Analyze(room, vaultOnly,
            [new KsProcgenRoutePair("Sides", new Vector2i(0, 1), new Vector2i(2, 1))],
            new HashSet<Vector2i> { new(1, 1) });

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenAlternateRouteStatus.Complete));
            Assert.That(result.Pairs[0].BaselineShortestSteps, Is.EqualTo(2));
            Assert.That(result.Pairs[0].Chokes[0].ConnectedWithoutChoke, Is.False);
        });
    }

    [Test]
    public void DisconnectedPairAndWorkBudgetsReturnNoPartialOutcomes()
    {
        var corridor = Enumerable.Range(0, 5).Select(x => new Vector2i(x, 0)).ToHashSet();
        var pair = new KsProcgenRoutePair("A-B", new Vector2i(0, 0), new Vector2i(4, 0));
        var disconnected = KsProcgenAlternateRouteAnalyzer.Analyze(
            new HashSet<Vector2i> { new(0, 0), new(4, 0) },
            new HashSet<Vector2i>(), [pair], new HashSet<Vector2i>());
        var searchBudget = KsProcgenAlternateRouteAnalyzer.Analyze(corridor,
            new HashSet<Vector2i>(), [pair], new HashSet<Vector2i> { new(2, 0) },
            maxSearches: 1);
        var expansionBudget = KsProcgenAlternateRouteAnalyzer.Analyze(corridor,
            new HashSet<Vector2i>(), [pair], new HashSet<Vector2i> { new(2, 0) },
            maxExpandedCells: 2);

        Assert.Multiple(() =>
        {
            Assert.That(disconnected.Status, Is.EqualTo(KsProcgenAlternateRouteStatus.InvalidInput));
            Assert.That(disconnected.Issue?.Code, Is.EqualTo("RoutePairDisconnected"));
            Assert.That(disconnected.Pairs, Is.Empty);
            Assert.That(searchBudget.Status, Is.EqualTo(KsProcgenAlternateRouteStatus.BudgetExceeded));
            Assert.That(searchBudget.Issue?.Code, Is.EqualTo("AlternateRouteBudget"));
            Assert.That(searchBudget.Pairs, Is.Empty);
            Assert.That(expansionBudget.Status, Is.EqualTo(KsProcgenAlternateRouteStatus.BudgetExceeded));
            Assert.That(expansionBudget.Issue?.Code, Is.EqualTo("AlternateRouteExpansionBudget"));
            Assert.That(expansionBudget.Pairs, Is.Empty);
        });
    }
}
