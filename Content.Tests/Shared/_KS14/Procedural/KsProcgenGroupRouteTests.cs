using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenGroupRouteTests
{
    [Test]
    public void ConstructsSeparatedCardinalTreesAndAnExplicitWallDisposition()
    {
        var proposal = Plan(Request());
        Assert.That(proposal.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), proposal.Issue?.Code);
        Assert.That(proposal.Routes.Count, Is.EqualTo(2));
        Assert.That(proposal.FloorCells, Is.EqualTo(new Vector2i[] { new(0, 0), new(0, 1), new(0, 3), new(0, 4) }));
        Assert.That(proposal.ProposedWallCells, Is.EqualTo(new Vector2i[] { new(0, 2) }));
        Assert.That(proposal.Analysis!.Components.Count, Is.EqualTo(2));
        Assert.That(proposal.ExpandedCells, Is.EqualTo(8));
        Assert.That(proposal.EngineAccessVerified, Is.False);
    }

    [Test]
    public void AdjacentGroupTerminalsCannotBeSeparatedButMinimumConnectivityCanMerge()
    {
        var request = Request();
        request.EntranceDomain!.Configurations[0].Groups[0].Ports = ["1", "8"];
        request.EntranceDomain.Configurations[0].Groups[1].Ports = ["3", "9"];
        AssertFailure(Plan(request), "InseparableGroupRouteTerminals");
        request.EntranceDomain.Configurations[0].Mode = KsProcgenEntranceConnectionMode.RequiredConnections;
        var accepted = Plan(request);
        Assert.That(accepted.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), accepted.Issue?.Code);
        Assert.That(accepted.FloorCells.Count, Is.EqualTo(5));
        Assert.That(accepted.Analysis!.Components.Count, Is.EqualTo(1));
    }

    [Test]
    public void ExactRoutesDetourAroundAnotherGroupsLandingRatherThanCreateATouchingJoin()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "Detour", ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
            Shape = new() { Cells = Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 4).Select(y => new Vector2i(x, y))).ToList() },
            EntranceDomain = new()
            {
                Entrances = [new() { PortId = "West", ThresholdCells = [new(-1, 1)], InwardNormal = new(1, 0) },
                    new() { PortId = "East", ThresholdCells = [new(5, 1)], InwardNormal = new(-1, 0) },
                    new() { PortId = "South", ThresholdCells = [new(2, 0)], InwardNormal = new(0, 1) }],
                Configurations = [new() { Id = "Separate", Groups = [new() { Id = "A", Ports = ["West", "East"] },
                    new() { Id = "B", Ports = ["South"] }] }],
            },
        };
        var context = Context() with { CleanHostCells = new HashSet<Vector2i> { new(-1, 1), new(-2, 1), new(5, 1), new(6, 1), new(2, -1) } };
        var proposal = KsProcgenGroupRoutePlanner.Plan(request, "Separate", context);
        Assert.That(proposal.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), proposal.Issue?.Code);
        Assert.That(proposal.Routes.Single(route => route.GroupId == "A").NewCells.Any(cell => cell.Y == 3), Is.True);
        Assert.That(proposal.Analysis!.Components.Count, Is.EqualTo(2));
        Assert.That(proposal.ProposedWallCells, Does.Contain(new Vector2i(2, 2)));
    }

    [Test]
    public void ReadOnlyPassagesArePreservedAndCannotBeCutToForceSeparation()
    {
        var request = Request();
        request.Shape.PreservedCells = [new(0, 0), new(0, 1)];
        var context = Context() with { ExistingCleanDomainCells = request.Shape.PreservedCells.ToHashSet(), PreservedContextComplete = true };
        var accepted = KsProcgenGroupRoutePlanner.Plan(request, "Separate", context);
        Assert.That(accepted.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), accepted.Issue?.Code);
        Assert.That(accepted.Routes.Single(route => route.GroupId == "Service").NewCells, Is.Empty);
        Assert.That(accepted.ProposedWallCells, Does.Not.Contain(new Vector2i(0, 0)));
        request.Shape.PreservedCells = request.Shape.Cells.ToList();
        context = context with { ExistingCleanDomainCells = request.Shape.PreservedCells.ToHashSet() };
        AssertFailure(KsProcgenGroupRoutePlanner.Plan(request, "Separate", context), "PreservedGroupPassageJoinsNetworks");
    }

    [Test]
    public void UnknownPreservedAndUnassignedExistingPassagesFailWithoutProposalClaims()
    {
        var request = Request();
        request.Shape.PreservedCells = [new(0, 2)];
        AssertFailure(KsProcgenGroupRoutePlanner.Plan(request, "Separate", Context() with { PreservedContextComplete = false }),
            "IncompleteGroupRoutePreservedContext");
        var context = Context() with { ExistingCleanDomainCells = new HashSet<Vector2i> { new(0, 2) }, PreservedContextComplete = true };
        AssertFailure(KsProcgenGroupRoutePlanner.Plan(request, "Separate", context), "UnassignedExistingGroupPassage");
    }

    [Test]
    public void NewSealingStructureRequiresWritableThresholdOwnership()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "Sealing", ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
            Shape = new() { Cells = [new(0, 0), new(1, 0), new(2, 0)] }, EntranceDomain = new()
            {
                Entrances = [new() { PortId = "West", ThresholdCells = [new(-1, 0)], InwardNormal = new(1, 0) },
                    new() { PortId = "East", ThresholdCells = [new(2, 0)], InwardNormal = new(-1, 0), OptionalSealable = true }],
                Configurations = [new() { Id = "Separate", Groups = [new() { Id = "Room", Ports = ["West"], RootCells = [new(1, 0)] }],
                    SealedPorts = ["East"] }],
            },
        };
        var accepted = Plan(request);
        Assert.That(accepted.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), accepted.Issue?.Code);
        Assert.That(accepted.ProposedWallCells, Is.EqualTo(new Vector2i[] { new(2, 0) }));
        Assert.That(accepted.Scene!.BlockedThresholdCells, Does.Contain(new Vector2i(2, 0)));
        request.EntranceDomain.Entrances[1].ThresholdCells = [new(3, 0)];
        request.EntranceDomain.Configurations[0].Groups[0].RootCells = [new(1, 0)];
        AssertFailure(Plan(request), "GroupRouteSealingOutsideWriteBounds");
    }

    [Test]
    public void WholeConfigurationRetryDiscardsRejectedHostBypassProposal()
    {
        var request = Alternatives();
        var accepted = KsProcgenGroupRoutePlanner.Select(request, Context());
        Assert.That(accepted.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), accepted.Issue?.Code);
        Assert.That(accepted.Accepted!.Analysis!.ConfigurationId, Is.EqualTo("Fallback"));
        Assert.That(accepted.CandidateProbes, Is.EqualTo(2));
        Assert.That(accepted.ExpandedCells, Is.EqualTo(30));
        Assert.That(accepted.Accepted.ExpandedCells, Is.EqualTo(8));
        Assert.That(accepted.Accepted.Analysis.HostComponents, Is.Empty, "The rejected combined graph must not survive into the local-scope alternative.");
    }

    [Test]
    public void SharedRouteAndFinalAnalysisWorkRemainsBoundedAcrossAlternatives()
    {
        var request = Alternatives();
        var exhausted = KsProcgenGroupRoutePlanner.Select(request, Context(), maximumExpandedCells: 29);
        Assert.That(exhausted.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
        Assert.That(exhausted.ExpandedCells, Is.EqualTo(29));
        Assert.That(exhausted.Accepted, Is.Null);
        Assert.That(exhausted.SelectionHash, Is.Zero);
        var probes = KsProcgenGroupRoutePlanner.Select(request, Context(), maximumCandidateProbes: 1);
        Assert.That(probes.Issue?.Code, Is.EqualTo("GroupRouteProbeBudget"));
        Assert.That(probes.Accepted, Is.Null);
        Assert.That(KsProcgenGroupRoutePlanner.Select(request, Context(), maximumExpandedCells: 30).Status,
            Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        AssertFailure(KsProcgenGroupRoutePlanner.Plan(Request(), "Separate", Context(), maximumExpandedCells: 3),
            "GroupRouteExpansionBudget", status: KsProcgenEntranceSceneStatus.BudgetExceeded);
    }

    [Test]
    public void CanonicalReplayAndDetachedSceneMasksIgnoreUnusedSufficientBudget()
    {
        var request = Alternatives();
        var context = Context();
        var original = KsProcgenGroupRoutePlanner.Select(request, context, maximumExpandedCells: 30);
        request.Shape.Cells.Reverse();
        request.EntranceDomain!.Entrances.Reverse();
        request.EntranceDomain.Configurations.Reverse();
        foreach (var configuration in request.EntranceDomain.Configurations)
        {
            configuration.Groups.Reverse();
            foreach (var group in configuration.Groups)
                group.Ports.Reverse();
        }
        var replay = KsProcgenGroupRoutePlanner.Select(request, context);
        Assert.That(replay.SelectionHash, Is.EqualTo(original.SelectionHash));
        ((HashSet<Vector2i>) context.CleanHostCells).Clear();
        Assert.That(original.Accepted!.Scene!.CleanHostCells.Count, Is.EqualTo(10));
        Assert.That(original.Accepted.FloorCells.Count, Is.EqualTo(4));
    }

    [Test]
    public void InvalidAndUnsupportedContextsLeaveNoPartialFloorOrWalls()
    {
        var request = Request();
        AssertFailure(KsProcgenGroupRoutePlanner.Plan(request, "Separate", Context() with
            { ExistingCleanDomainCells = new HashSet<Vector2i> { new(0, 0) } }), "InvalidGroupRouteContext",
            status: KsProcgenEntranceSceneStatus.InvalidInput);
        request.Mode = KsProcgenMode.Hybrid;
        AssertFailure(Plan(request), "UnsupportedGroupRouteGeometry", status: KsProcgenEntranceSceneStatus.InvalidInput);
        request.Mode = KsProcgenMode.Procedural;
        request.RootCells = [new(0, 2)];
        AssertFailure(Plan(request), "UnassignedGroupRouteRoot");
    }

    private static KsProcgenRequest Request() => new()
    {
        RequestId = "RouteFixture", ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
        Shape = new() { Cells = Enumerable.Range(0, 5).Select(y => new Vector2i(0, y)).ToList() },
        EntranceDomain = new()
        {
            Entrances = new[] { ("1", 0), ("3", 1), ("8", 3), ("9", 4) }.Select(item => new KsProcgenEntranceSpec
                { PortId = item.Item1, ThresholdCells = [new(-1, item.Item2)], InwardNormal = new(1, 0) }).ToList(),
            Configurations = [new() { Id = "Separate", Groups = [new() { Id = "Service", Ports = ["1", "3"] },
                new() { Id = "Storage", Ports = ["8", "9"] }] }],
        },
    };

    private static KsProcgenRequest Alternatives()
    {
        var request = Request();
        var first = request.EntranceDomain!.Configurations[0];
        first.IsolationScope = KsProcgenEntranceIsolationScope.RequestNetwork;
        first.Weight = float.MaxValue;
        request.EntranceDomain.Configurations.Add(new() { Id = "Fallback", Weight = float.Epsilon,
            Groups = [new() { Id = "Service", Ports = ["1", "3"] }, new() { Id = "Storage", Ports = ["8", "9"] }] });
        return request;
    }

    private static KsProcgenGroupRouteContext Context() => new(new HashSet<Vector2i>(),
        Enumerable.Range(-2, 2).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToHashSet(), new HashSet<Vector2i>(), true, true);

    private static KsProcgenGroupRouteResult Plan(KsProcgenRequest request) => KsProcgenGroupRoutePlanner.Plan(request, "Separate", Context());

    private static void AssertFailure(KsProcgenGroupRouteResult result, string code,
        KsProcgenEntranceSceneStatus status = KsProcgenEntranceSceneStatus.Rejected)
    {
        Assert.That(result.Status, Is.EqualTo(status));
        Assert.That(result.Issue?.Code, Is.EqualTo(code));
        Assert.That(result.FloorCells, Is.Empty);
        Assert.That(result.ProposedWallCells, Is.Empty);
        Assert.That(result.Routes, Is.Empty);
        Assert.That(result.Scene, Is.Null);
        Assert.That(result.Analysis, Is.Null);
        Assert.That(result.RouteHash, Is.Zero);
    }
}
