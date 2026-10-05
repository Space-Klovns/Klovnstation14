using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenEntranceSceneTests
{
    [Test]
    public void ExactGroupsProduceSeparateLocalComponentsAndACompletePortMatrix()
    {
        var result = Analyze(Request(), Scene());
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Components.Count, Is.EqualTo(2));
        Assert.That(result.Ports.Count, Is.EqualTo(4));
        Assert.That(result.Ports.Single(port => port.PortId == "1").LocalComponent,
            Is.EqualTo(result.Ports.Single(port => port.PortId == "3").LocalComponent));
        Assert.That(result.Ports.Single(port => port.PortId == "1").ScopeComponent,
            Is.Not.EqualTo(result.Ports.Single(port => port.PortId == "8").ScopeComponent));
        Assert.That(result.ExpandedCells, Is.EqualTo(12));
        Assert.That(result.EngineAccessVerified, Is.False);
        Assert.That(result.Ports.All(port => port.GlobalComponent == null), Is.True,
            "An unchecked host graph cannot report global component identity.");
    }

    [Test]
    public void ExactRejectsAJoinThatRequiredConnectionsExplicitlyPermits()
    {
        var request = Request();
        AssertFailure(Analyze(request, Scene(fullFloor: true)), "ForbiddenEntranceGroupJoin");
        request.EntranceDomain!.Configurations[0].Mode = KsProcgenEntranceConnectionMode.RequiredConnections;
        var accepted = Analyze(request, Scene(fullFloor: true));
        Assert.That(accepted.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        Assert.That(accepted.Components.Single().GroupIds, Is.EqualTo(new[] { "Service", "Storage" }));
    }

    [Test]
    public void HostBypassesCannotSatisfyLocalGroupsOrRequestWideIsolation()
    {
        var request = Request();
        var scene = Scene();
        Assert.That(Analyze(request, scene).Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        request.EntranceDomain!.Configurations[0].IsolationScope = KsProcgenEntranceIsolationScope.RequestNetwork;
        AssertFailure(Analyze(request, scene with { HostContextComplete = false }), "IncompleteEntranceHostContext");
        AssertFailure(Analyze(request, scene), "ForbiddenEntranceGroupJoin");
        request.EntranceDomain.Configurations[0].Mode = KsProcgenEntranceConnectionMode.RequiredConnections;
        request.EntranceDomain.Configurations[0].Groups[0].Ports = ["1", "8"];
        request.EntranceDomain.Configurations[0].Groups[1].Ports = ["3", "9"];
        AssertFailure(Analyze(request, scene), "RequiredEntranceGroupDisconnected");
    }

    [Test]
    public void GlobalSingleNetworkCanUseKnownHostRoutesWithoutMergingLocalExactGroups()
    {
        var request = Request();
        request.ConnectivityPolicy = KsProcgenConnectivityPolicy.SingleNetwork;
        var accepted = Analyze(request, Scene());
        Assert.That(accepted.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), accepted.Issue?.Code);
        Assert.That(accepted.Ports.Select(port => port.GlobalComponent).Distinct().Count(), Is.EqualTo(1));
        Assert.That(accepted.Ports.Select(port => port.ScopeComponent).Distinct().Count(), Is.EqualTo(2));
        Assert.That(accepted.HostComponents.Single(), Does.Contain(new Vector2i(-1, 2)));
        var host = Scene().CleanHostCells.Where(cell => cell.Y != 2).ToHashSet();
        AssertFailure(Analyze(request, Scene() with { CleanHostCells = host }), "GlobalEntranceNetworkDisconnected");
    }

    [Test]
    public void DeclaredNetworksNeedsExplicitReachableRootsAndDoesNotInventHostAttachment()
    {
        var request = Request();
        request.ConnectivityPolicy = KsProcgenConnectivityPolicy.DeclaredNetworks;
        AssertFailure(Analyze(request, Scene()), "MissingDeclaredNetworkRoot", status: KsProcgenEntranceSceneStatus.InvalidInput);
        var groups = request.EntranceDomain!.Configurations[0].Groups;
        groups[0].RootCells = [new(2, 0)];
        groups[1].RootCells = [new(2, 4)];
        Assert.That(Analyze(request, Scene()).Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        groups[0].RootCells = [new(2, 4)];
        AssertFailure(Analyze(request, Scene()), "EntranceGroupRootUnreachable");
        groups[0].RootCells = [new(20, 4)];
        AssertFailure(Analyze(request, Scene()), "EntranceRootOutsideDomain", status: KsProcgenEntranceSceneStatus.InvalidInput);
        Assert.That(KsProcgenGeometry.TryNormalize(new() { RequestId = "NoContract", ConnectivityPolicy = KsProcgenConnectivityPolicy.DeclaredNetworks },
            out var shape, out var issue), Is.False);
        Assert.That(shape, Is.Null);
        Assert.That(issue?.Code, Is.EqualTo("MissingDeclaredEntranceDomain"));
    }

    [Test]
    public void ApproachAndSealingChecksCannotBeSatisfiedByOmittingFloorMetadata()
    {
        var request = Request();
        var scene = Scene();
        AssertFailure(Analyze(request, scene with { CleanDomainCells = scene.CleanDomainCells.Where(cell => cell != new Vector2i(0, 0)).ToHashSet() }),
            "EntranceApproachUnavailable");
        request.EntranceDomain!.Entrances.Single(port => port.PortId == "9").OptionalSealable = true;
        request.EntranceDomain.Configurations[0] = new()
        {
            Id = "Separate", Mode = KsProcgenEntranceConnectionMode.RequiredConnections,
            Groups = [new() { Id = "All", Ports = ["1", "3", "8"] }], SealedPorts = ["9"],
        };
        scene = Scene(fullFloor: true);
        scene = scene with { CleanHostCells = scene.CleanHostCells.Where(cell => cell != new Vector2i(-1, 4)).ToHashSet() };
        AssertFailure(Analyze(request, scene), "UnblockedSealedEntrance");
        scene = scene with { BlockedThresholdCells = new HashSet<Vector2i> { new(-1, 4) } };
        var accepted = Analyze(request, scene);
        Assert.That(accepted.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), accepted.Issue?.Code);
        Assert.That(accepted.SealedPorts, Is.EqualTo(new[] { "9" }));
        Assert.That(accepted.Ports.Select(port => port.PortId), Does.Not.Contain("9"));
    }

    [Test]
    public void UnassignedFloorAndDiagonalOnlyConnectionsAreRejected()
    {
        var request = Request();
        request.Shape.Cells.Add(new(4, 4));
        var scene = Scene();
        AssertFailure(Analyze(request, scene with { CleanDomainCells = scene.CleanDomainCells.Append(new(4, 4)).ToHashSet() }), "UnassignedEntranceFloor");
        var diagonal = new KsProcgenRequest
        {
            RequestId = "Diagonal", ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
            Shape = new() { Cells = [new(0, 0), new(1, 1)] }, EntranceDomain = new()
            {
                Entrances = [new() { PortId = "1", ThresholdCells = [new(-1, 0)], InwardNormal = new(1, 0) },
                    new() { PortId = "3", ThresholdCells = [new(1, 0)], InwardNormal = new(0, 1) }],
            },
        };
        var diagonalScene = new KsProcgenEntranceScene(diagonal.Shape.Cells.ToHashSet(),
            new HashSet<Vector2i> { new(-1, 0), new(-2, 0), new(1, 0), new(1, -1) }, new HashSet<Vector2i>(), true);
        AssertFailure(KsProcgenEntranceSceneAnalyzer.Analyze(diagonal, "KsDefaultAllConnected", diagonalScene), "RequiredEntranceGroupDisconnected");
    }

    [Test]
    public void SelectorRejectsAWholeSceneThenAcceptsAnotherWithoutRetainingRejectedFloor()
    {
        var (request, candidates) = Selection();
        var accepted = KsProcgenEntranceSceneSelector.Select(request, candidates);
        Assert.That(accepted.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), accepted.Issue?.Code);
        Assert.That(accepted.Accepted!.ConfigurationId, Is.EqualTo("Fallback"));
        Assert.That(accepted.CandidateProbes, Is.EqualTo(2));
        Assert.That(accepted.ExpandedCells, Is.EqualTo(27));
        Assert.That(accepted.Accepted.Components.SelectMany(component => component.Cells).Any(cell => cell.Y == 2), Is.False);
        ((HashSet<Vector2i>) candidates[1].Scene.CleanDomainCells).Clear();
        Assert.That(accepted.Accepted.Components.Sum(component => component.Cells.Count), Is.EqualTo(12));
    }

    [Test]
    public void SelectionSharesExpansionAndProbeBudgetsAcrossRejectedAlternatives()
    {
        var (request, candidates) = Selection();
        var exhausted = KsProcgenEntranceSceneSelector.Select(request, candidates, maximumExpandedCells: 26);
        AssertSelectionFailure(exhausted, "EntranceSceneExpansionBudget");
        Assert.That(exhausted.ExpandedCells, Is.EqualTo(26));
        var probes = KsProcgenEntranceSceneSelector.Select(request, candidates, maximumCandidateProbes: 1);
        AssertSelectionFailure(probes, "EntranceCandidateProbeBudget");
        Assert.That(probes.CandidateProbes, Is.EqualTo(1));
        AssertSelectionFailure(KsProcgenEntranceSceneSelector.Select(request, candidates, maximumCandidateProbes: 0), "EntranceCandidateProbeBudget");
        Assert.That(KsProcgenEntranceSceneSelector.Select(request, candidates, maximumExpandedCells: 27).Status,
            Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
    }

    [Test]
    public void ReplayIsCanonicalAndHashesNonwalkableDomainGeometryAndUnusedBudgetIndependently()
    {
        var (request, candidates) = Selection();
        var original = KsProcgenEntranceSceneSelector.Select(request, candidates, maximumExpandedCells: 27);
        request.Shape.Cells.Reverse();
        request.EntranceDomain!.Entrances.Reverse();
        request.EntranceDomain.Configurations.Reverse();
        var replay = KsProcgenEntranceSceneSelector.Select(request, candidates.Reverse().ToArray());
        Assert.That(replay.SelectionHash, Is.EqualTo(original.SelectionHash));
        request.Shape.Cells.Add(new(3, 2));
        var changed = KsProcgenEntranceSceneSelector.Select(request, candidates);
        Assert.That(changed.SelectionHash, Is.Not.EqualTo(original.SelectionHash));
    }

    [Test]
    public void InvalidScenesAndUnknownCandidatesHaveNoAcceptedRecordsOrIdentity()
    {
        var (request, candidates) = Selection();
        AssertSelectionFailure(KsProcgenEntranceSceneSelector.Select(request, [candidates[0] with { ConfigurationId = "Unknown" }]),
            "UnknownEntranceSceneConfiguration", status: KsProcgenEntranceSceneStatus.InvalidInput);
        AssertSelectionFailure(KsProcgenEntranceSceneSelector.Select(request, [candidates[0], candidates[0]]),
            "InvalidEntranceSceneCandidates", status: KsProcgenEntranceSceneStatus.InvalidInput);
        AssertSelectionFailure(KsProcgenEntranceSceneSelector.Select(request, [candidates[0]]),
            "MissingEntranceSceneConfiguration", status: KsProcgenEntranceSceneStatus.InvalidInput);
        AssertFailure(Analyze(Request(), Scene() with { CleanHostCells = new HashSet<Vector2i> { new(0, 0) } }),
            "InvalidEntranceScene", status: KsProcgenEntranceSceneStatus.InvalidInput);
        AssertFailure(KsProcgenEntranceSceneAnalyzer.Analyze(Request(), "Separate", Scene(), maximumExpandedCells: 0),
            "EntranceSceneExpansionBudget", status: KsProcgenEntranceSceneStatus.BudgetExceeded);
    }

    [Test]
    public void RequestCopyCarriesRootsAndCompleteContractsWhileLegacyGenerationRejectsBeforeSearch()
    {
        var request = Request();
        request.EntranceDomain!.Configurations[0].Groups[0].RootCells = [new(1, 0)];
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Code);
        var profile = new KsProcgenAreaProfile("Office", request.Mode, request.GeometryMode, request.ConnectivityPolicy, null, 65536, 1000000);
        var blob = new KsProcgenAreaBlob(request.RequestId, "Default", profile, shape!.TargetCells)
            { Entrances = shape.Entrances, ConnectionContract = shape.EntranceContract };
        var copy = blob.CreateRequest(17);
        Assert.That(KsProcgenGeometry.TryNormalize(copy, out var copiedShape, out issue), Is.True, issue?.Code);
        Assert.That(copiedShape!.EntranceContract!.ContractHash, Is.EqualTo(shape.EntranceContract!.ContractHash));
        copy.EntranceDomain!.Configurations[0].Groups[0].RootCells.Clear();
        Assert.That(shape.EntranceContract.Configurations[0].Groups[0].RootCells.Count, Is.EqualTo(1));
        var packing = KsProcgenPackingPlanner.Plan(request, []);
        Assert.That(packing.Issue?.Code, Is.EqualTo("UnsupportedEntranceAwarePacking"));
        Assert.That(packing.SearchNodes, Is.Zero);
        Assert.That(packing.CellClaims, Is.Empty);
        var forgedPacking = new KsProcgenPackingResult { Status = KsProcgenPackingStatus.GeometryReady };
        Assert.That(KsProcgenResidualConnector.Connect(shape, forgedPacking, [], new HashSet<Vector2i>()).Issue?.Code,
            Is.EqualTo("UnsupportedEntranceAwareResidual"));
        Assert.That(KsProcgenPureFillPlanner.Plan(shape, forgedPacking, 0).Issue?.Code, Is.EqualTo("UnsupportedEntranceAwarePureFill"));
        Assert.That(KsProcgenPortNetworkAnalyzer.Analyze(shape, forgedPacking, new HashSet<Vector2i>(), []).Issue?.Code,
            Is.EqualTo("UnsupportedEntranceAwarePortNetwork"));
    }

    [Test]
    public void RootDeclarationsAreCanonicalBoundedAndPartOfAcceptedIdentity()
    {
        var request = Request();
        var groups = request.EntranceDomain!.Configurations[0].Groups;
        groups[0].RootCells = [new(2, 0), new(1, 0)];
        var original = Analyze(request, Scene());
        groups[0].RootCells.Reverse();
        Assert.That(Analyze(request, Scene()).SceneHash, Is.EqualTo(original.SceneHash));
        groups[0].RootCells = [new(1, 0)];
        Assert.That(Analyze(request, Scene()).SceneHash, Is.Not.EqualTo(original.SceneHash));
        groups[0].RootCells = [new(1, 0), new(1, 0)];
        AssertFailure(Analyze(request, Scene()), "InvalidEntranceGroup", status: KsProcgenEntranceSceneStatus.InvalidInput);
        groups[0].RootCells = Enumerable.Range(0, 1024).Select(x => new Vector2i(x, 0)).ToList();
        groups[1].RootCells = [new(0, 3)];
        AssertFailure(Analyze(request, Scene()), "EntranceRootBudget", status: KsProcgenEntranceSceneStatus.InvalidInput);
    }

    private static KsProcgenRequest Request() => new()
    {
        RequestId = "Fixture", ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
        Shape = new() { Cells = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToList() },
        EntranceDomain = new()
        {
            Entrances = new[] { ("1", 0), ("3", 1), ("8", 3), ("9", 4) }.Select(item => new KsProcgenEntranceSpec
                { PortId = item.Item1, ThresholdCells = [new(-1, item.Item2)], InwardNormal = new(1, 0) }).ToList(),
            Configurations = [new() { Id = "Separate", Groups = [new() { Id = "Service", Ports = ["1", "3"] },
                new() { Id = "Storage", Ports = ["8", "9"] }] }],
        },
    };

    private static KsProcgenEntranceScene Scene(bool fullFloor = false) => new(
        Request().Shape.Cells.Where(cell => fullFloor || cell.Y != 2).ToHashSet(),
        Enumerable.Range(-2, 2).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToHashSet(), new HashSet<Vector2i>(), true);

    private static (KsProcgenRequest Request, KsProcgenEntranceSceneCandidate[] Candidates) Selection()
    {
        var request = Request();
        request.EntranceDomain!.Configurations[0].Weight = float.MaxValue;
        request.EntranceDomain.Configurations.Add(new() { Id = "Fallback", Weight = float.Epsilon,
            Mode = KsProcgenEntranceConnectionMode.RequiredConnections,
            Groups = [new() { Id = "Service", Ports = ["1", "3"] }, new() { Id = "Storage", Ports = ["8", "9"] }] });
        return (request, [new("Separate", Scene(fullFloor: true)), new("Fallback", Scene())]);
    }

    private static KsProcgenEntranceSceneResult Analyze(KsProcgenRequest request, KsProcgenEntranceScene scene) =>
        KsProcgenEntranceSceneAnalyzer.Analyze(request, "Separate", scene);

    private static void AssertFailure(KsProcgenEntranceSceneResult result, string code,
        KsProcgenEntranceSceneStatus status = KsProcgenEntranceSceneStatus.Rejected)
    {
        Assert.That(result.Status, Is.EqualTo(status));
        Assert.That(result.Issue?.Code, Is.EqualTo(code));
        Assert.That(result.Components, Is.Empty);
        Assert.That(result.Ports, Is.Empty);
        Assert.That(result.HostComponents, Is.Empty);
        Assert.That(result.SealedPorts, Is.Empty);
        Assert.That(result.ConfigurationId, Is.Null);
        Assert.That(result.SceneHash, Is.Zero);
    }

    private static void AssertSelectionFailure(KsProcgenEntranceSceneSelection result, string code,
        KsProcgenEntranceSceneStatus status = KsProcgenEntranceSceneStatus.BudgetExceeded)
    {
        Assert.That(result.Status, Is.EqualTo(status));
        Assert.That(result.Issue?.Code, Is.EqualTo(code));
        Assert.That(result.Accepted, Is.Null);
        Assert.That(result.SelectionHash, Is.Zero);
    }
}
