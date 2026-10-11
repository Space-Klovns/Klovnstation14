using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenGroupRouteSearchTests
{
    [Test]
    public void SearchRetriesACompleteOrderAndDiscardsCanonicalClaims()
    {
        var request = DetourRequest();
        var context = Context(request);
        var canonical = KsProcgenGroupRoutePlanner.Plan(request, "Separate", context);
        Assert.That(canonical.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Rejected));
        Assert.That(canonical.RoutedGroups, Is.EqualTo(1));
        Assert.That(canonical.Routes, Is.Empty);
        var reverse = KsProcgenGroupRoutePlanner.Plan(request, "Separate", context, groupOrder: new[] { "B", "A" });
        var search = KsProcgenGroupRoutePlanner.Search(request, "Separate", context);
        Assert.That(search.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), search.Issue?.Code);
        Assert.That(search.OrderProbes, Is.EqualTo(2));
        Assert.That(search.ExpandedCells, Is.EqualTo(canonical.ExpandedCells + reverse.ExpandedCells));
        Assert.That(search.Accepted!.RouteHash, Is.EqualTo(reverse.RouteHash));
        Assert.That(search.Accepted.GroupOrder, Is.EqualTo(new[] { "B", "A" }));
        Assert.That(search.Accepted.FloorCells.Count, Is.EqualTo(22));
        Assert.That(search.Accepted.Analysis!.Components.Count, Is.EqualTo(2));
        Assert.That(search.Accepted.Routes.SelectMany(route => route.NewCells).ToHashSet(),
            Is.EquivalentTo(search.Accepted.FloorCells));
        var selection = KsProcgenGroupRoutePlanner.Select(request, context);
        Assert.That(selection.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        Assert.That(selection.CandidateProbes, Is.EqualTo(1));
        Assert.That(selection.GroupOrderProbes, Is.EqualTo(2));
        Assert.That(selection.Accepted!.RouteHash, Is.EqualTo(reverse.RouteHash));
        Assert.That(selection.ExpandedCells, Is.EqualTo(search.ExpandedCells));
    }

    [Test]
    public void OrderAndExpansionBudgetsBoundDiscardedWork()
    {
        var request = DetourRequest();
        var context = Context(request);
        var canonical = KsProcgenGroupRoutePlanner.Plan(request, "Separate", context);
        var accepted = KsProcgenGroupRoutePlanner.Search(request, "Separate", context);
        var limited = KsProcgenGroupRoutePlanner.Search(request, "Separate", context, maximumOrderProbes: 1);
        AssertFailure(limited, KsProcgenEntranceSceneStatus.BudgetExceeded, "GroupRouteOrderProbeBudget");
        Assert.That(limited.OrderProbes, Is.EqualTo(1));
        Assert.That(limited.ExpandedCells, Is.EqualTo(canonical.ExpandedCells));
        var exhausted = KsProcgenGroupRoutePlanner.Search(request, "Separate", context,
            maximumExpandedCells: accepted.ExpandedCells - 1);
        Assert.That(exhausted.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(exhausted.ExpandedCells, Is.EqualTo(accepted.ExpandedCells - 1));
        Assert.That(exhausted.Accepted, Is.Null);
        var sufficient = KsProcgenGroupRoutePlanner.Search(request, "Separate", context,
            maximumExpandedCells: accepted.ExpandedCells);
        Assert.That(sufficient.Accepted!.RouteHash, Is.EqualTo(accepted.Accepted!.RouteHash));
        var zero = KsProcgenGroupRoutePlanner.Search(request, "Separate", context, maximumOrderProbes: 0);
        AssertFailure(zero, KsProcgenEntranceSceneStatus.BudgetExceeded, "GroupRouteOrderProbeBudget");
        Assert.That(zero.OrderProbes, Is.Zero);
        Assert.That(zero.ExpandedCells, Is.Zero);
        var selection = KsProcgenGroupRoutePlanner.Select(request, context, maximumGroupOrderProbes: 1);
        Assert.That(selection.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(selection.GroupOrderProbes, Is.EqualTo(1));
        Assert.That(selection.Accepted, Is.Null);
        Assert.That(selection.SelectionHash, Is.Zero);
    }

    [Test]
    public void RejectedOrderWorkCannotBeReusedAsAllowanceForTheAcceptedOrder()
    {
        var request = DetourRequest();
        var context = Context(request);
        var canonical = KsProcgenGroupRoutePlanner.Plan(request, "Separate", context);
        var reverse = KsProcgenGroupRoutePlanner.Plan(request, "Separate", context, groupOrder: new[] { "B", "A" });
        Assert.That(canonical.ExpandedCells, Is.GreaterThan(0));
        var required = canonical.ExpandedCells + reverse.ExpandedCells;
        var exhausted = KsProcgenGroupRoutePlanner.Search(request, "Separate", context, maximumExpandedCells: required - 1);
        Assert.That(exhausted.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(exhausted.Accepted, Is.Null);
        Assert.That(exhausted.ExpandedCells, Is.EqualTo(required - 1));
        Assert.That(KsProcgenGroupRoutePlanner.Search(request, "Separate", context, maximumExpandedCells: required).Status,
            Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
    }

    [Test]
    public void ExhaustedPermutationsHaveNoAcceptedGeometryAndRespectProbeLimits()
    {
        var request = CrossingRequest();
        var context = Context(request);
        var exhausted = KsProcgenGroupRoutePlanner.Search(request, "Separate", context);
        AssertFailure(exhausted, KsProcgenEntranceSceneStatus.Rejected, "NoGroupRouteProposal");
        Assert.That(exhausted.OrderProbes, Is.EqualTo(6), "Every three-group permutation is tried once.");
        var limited = KsProcgenGroupRoutePlanner.Search(request, "Separate", context, maximumOrderProbes: 5);
        AssertFailure(limited, KsProcgenEntranceSceneStatus.BudgetExceeded, "GroupRouteOrderProbeBudget");
        Assert.That(limited.OrderProbes, Is.EqualTo(5));
    }

    [Test]
    public void ExplicitOrdersAreCompleteAndAcceptedWitnessesAreDetached()
    {
        var request = DetourRequest();
        var context = Context(request);
        foreach (var malformed in new[] { Array.Empty<string>(), new[] { "A" }, new[] { "A", "A" }, new[] { "A", "Missing" } })
        {
            var invalid = KsProcgenGroupRoutePlanner.Plan(request, "Separate", context, groupOrder: malformed);
            Assert.That(invalid.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.InvalidInput));
            Assert.That(invalid.Issue?.Code, Is.EqualTo("InvalidGroupRouteOrder"));
            Assert.That(invalid.ExpandedCells, Is.Zero);
            Assert.That(invalid.GroupOrder, Is.Empty);
            Assert.That(invalid.Scene, Is.Null);
        }
        var order = new List<string> { "B", "A" };
        var accepted = KsProcgenGroupRoutePlanner.Plan(request, "Separate", context, groupOrder: order);
        order.Clear();
        Assert.That(accepted.GroupOrder, Is.EqualTo(new[] { "B", "A" }));
        Assert.That(accepted.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        Assert.That(accepted.EngineAccessVerified, Is.False);
    }

    [Test]
    public void CanonicalSearchReplayIgnoresDeclarationOrderAndUnusedSufficientAllowances()
    {
        var request = DetourRequest();
        var context = Context(request);
        var original = KsProcgenGroupRoutePlanner.Select(request, context);
        request.Shape.Cells.Reverse();
        request.EntranceDomain!.Entrances.Reverse();
        request.EntranceDomain.Configurations[0].Groups.Reverse();
        foreach (var group in request.EntranceDomain.Configurations[0].Groups)
            group.Ports.Reverse();
        var replay = KsProcgenGroupRoutePlanner.Select(request, context, maximumExpandedCells: original.ExpandedCells,
            maximumGroupOrderProbes: 2);
        Assert.That(replay.SelectionHash, Is.EqualTo(original.SelectionHash));
        Assert.That(replay.ExpandedCells, Is.EqualTo(original.ExpandedCells));
        Assert.That(replay.Accepted!.GroupOrder, Is.EqualTo(original.Accepted!.GroupOrder));
    }

    [Test]
    public void ConfigurationSelectionSharesOrderProbesAndExpansionsAcrossAlternatives()
    {
        var request = CrossingRequest();
        var first = request.EntranceDomain!.Configurations[0];
        first.Weight = float.MaxValue;
        request.EntranceDomain.Configurations.Add(new()
        {
            Id = "Minimum", Weight = float.Epsilon, Mode = KsProcgenEntranceConnectionMode.RequiredConnections,
            Groups = first.Groups,
        });
        var context = Context(request);
        var rejected = KsProcgenGroupRoutePlanner.Search(request, "Separate", context);
        var fallback = KsProcgenGroupRoutePlanner.Search(request, "Minimum", context);
        var exhausted = KsProcgenGroupRoutePlanner.Select(request, context, maximumGroupOrderProbes: 6);
        Assert.That(exhausted.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(exhausted.Issue?.Code, Is.EqualTo("GroupRouteOrderProbeBudget"));
        Assert.That(exhausted.GroupOrderProbes, Is.EqualTo(6));
        Assert.That(exhausted.ExpandedCells, Is.EqualTo(rejected.ExpandedCells));
        Assert.That(exhausted.Accepted, Is.Null);
        var accepted = KsProcgenGroupRoutePlanner.Select(request, context, maximumGroupOrderProbes: 7);
        Assert.That(accepted.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), accepted.Issue?.Code);
        Assert.That(accepted.Accepted!.Analysis!.ConfigurationId, Is.EqualTo("Minimum"));
        Assert.That(accepted.GroupOrderProbes, Is.EqualTo(7));
        Assert.That(accepted.CandidateProbes, Is.EqualTo(2));
        Assert.That(accepted.ExpandedCells, Is.EqualTo(rejected.ExpandedCells + fallback.ExpandedCells));
        Assert.That(accepted.Accepted.RouteHash, Is.EqualTo(fallback.Accepted!.RouteHash));
    }

    [Test]
    public void InvalidSearchBoundsAndUnknownConfigurationsFailBeforeProbing()
    {
        var request = DetourRequest();
        var context = Context(request);
        foreach (var bounds in new[] { -1, 65 })
        {
            var invalid = KsProcgenGroupRoutePlanner.Search(request, "Separate", context, maximumOrderProbes: bounds);
            AssertFailure(invalid, KsProcgenEntranceSceneStatus.InvalidInput, "InvalidGroupRouteSearch");
            Assert.That(invalid.OrderProbes, Is.Zero);
        }
        AssertFailure(KsProcgenGroupRoutePlanner.Search(request, "Unknown", context),
            KsProcgenEntranceSceneStatus.InvalidInput, "InvalidGroupRouteSearch");
    }

    private static KsProcgenRequest DetourRequest()
    {
        var removed = new HashSet<Vector2i> { new(2, 3), new(2, 4), new(3, 5), new(3, 6), new(4, 4), new(5, 5) };
        return Request(Grid().Where(cell => !removed.Contains(cell)).ToList(), new[]
        {
            Port("A1", new(-1, 1), new(1, 0)), Port("A2", new(7, 1), new(-1, 0)),
            Port("B1", new(-1, 5), new(1, 0)), Port("B2", new(7, 5), new(-1, 0)),
        }, new[] { Group("A"), Group("B") });
    }

    private static KsProcgenRequest CrossingRequest() => Request(Grid().ToList(), new[]
    {
        Port("A1", new(1, -1), new(0, 1)), Port("A2", new(5, 7), new(0, -1)),
        Port("B1", new(-1, 1), new(1, 0)), Port("B2", new(7, 5), new(-1, 0)),
        Port("C1", new(5, -1), new(0, 1)), Port("C2", new(1, 7), new(0, -1)),
    }, new[] { Group("A"), Group("B"), Group("C") });

    private static IEnumerable<Vector2i> Grid() => Enumerable.Range(0, 7).SelectMany(x => Enumerable.Range(0, 7).Select(y => new Vector2i(x, y)));
    private static KsProcgenEntranceSpec Port(string id, Vector2i threshold, Vector2i normal) => new()
        { PortId = id, ThresholdCells = new List<Vector2i> { threshold }, InwardNormal = normal };
    private static KsProcgenEntranceGroupSpec Group(string id) => new() { Id = id, Ports = new List<string> { id + "1", id + "2" } };
    private static KsProcgenRequest Request(List<Vector2i> cells, KsProcgenEntranceSpec[] ports, KsProcgenEntranceGroupSpec[] groups) => new()
    {
        RequestId = "OrderSearch", ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland, Shape = new() { Cells = cells },
        EntranceDomain = new() { Entrances = ports.ToList(), Configurations = new List<KsProcgenEntranceConfigurationSpec>
            { new() { Id = "Separate", Groups = groups.ToList() } } },
    };
    private static KsProcgenGroupRouteContext Context(KsProcgenRequest request) => new(new HashSet<Vector2i>(),
        request.EntranceDomain!.Entrances.SelectMany(port => port.ThresholdCells.SelectMany(cell => new[] { cell, cell - port.InwardNormal })).ToHashSet(),
        new HashSet<Vector2i>(), true, true);
    private static void AssertFailure(KsProcgenGroupRouteSearchResult result, KsProcgenEntranceSceneStatus status, string code)
    {
        Assert.That(result.Status, Is.EqualTo(status));
        Assert.That(result.Issue?.Code, Is.EqualTo(code));
        Assert.That(result.Accepted, Is.Null);
    }
}
