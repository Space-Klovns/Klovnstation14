#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenOwnedInteriorPlanner))]
public sealed class KsProcgenStandaloneTests : KsProcgenGameTest
{
    [TestCase(1)] [TestCase(3)]
    public async Task MinimalStripsNeedNoAuthoredEntranceOrHostCorridor(int length)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var request = Request();
            request.Shape.Cells = Enumerable.Range(0, length).Select(x => new Vector2i(x, 0)).ToList();
            var result = Plan(request);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), message: result.Issue?.Code);
            Assert.That(result.Partition!.FloorCells, Is.EquivalentTo(request.Shape.Cells));
            Assert.That(result.Access!.Analysis!.Ports, Is.Empty);
            Assert.That(result.Access.Scene!.CleanHostCells, Is.Empty);
            Assert.That(result.Partition.ProtectedCells, Does.Contain(new Vector2i(0, 0)));
            Assert.That(result.EngineAccessVerified || result.GasClosureVerified || result.Published, Is.False);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task PerIslandRootsAreDeterministicAndDoNotMutateTheAuthoringRequest()
    {
        await AssertOnServer(() =>
        {
            var request = Request(islands: true);
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out var snapshot, out var issue), Is.True, message: issue?.Code);
            var first = Plan(request);
            Assert.That(first.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), message: first.Issue?.Code);
            Assert.That(first.Partition!.Analysis!.Components.Count, Is.EqualTo(2));
            Assert.That(first.Partition.ProtectedCells, Does.Contain(new Vector2i(0, 0)));
            Assert.That(first.Partition.ProtectedCells, Does.Contain(new Vector2i(10, 0)));
            request.Shape.Cells.Reverse();
            var replay = Plan(request);
            Assert.That(replay.SemanticHash, Is.EqualTo(first.SemanticHash));
            Assert.That(request.RootCells, Is.Empty);
            Assert.That(request.EntranceDomain!.Entrances, Is.Empty);
            Assert.That(request.EntranceDomain.Configurations, Is.Empty);
            var captured = snapshot!.CopyRequest();
            Assert.That(captured.EntranceDomain!.Configurations.Single().Groups.Count, Is.EqualTo(2));
            Assert.That(Plan(captured).SemanticHash, Is.EqualTo(first.SemanticHash));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task StandaloneOwnedPlanningRetainsSupportedAndContainedRoomMembers()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var request = Request();
            request.Shape.Cells = Enumerable.Range(0, 7).SelectMany(x => Enumerable.Range(0, 7).Select(y => new Vector2i(x, y))).ToList();
            var prototypes = Pair.Server.ResolveDependency<IPrototypeManager>();
            var catalog = KsProcgenOwnedLayeredTests.Catalog(prototypes, contained: true);
            var result = Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, "Layered", Context(), [],
                budgets: Budgets() with { PreferredMaximumRoomCells = 64 }, content: catalog);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), message: result.Issue?.Code);
            var layered = result.Furnishings.Single(region => region.Proposal.Layered != null).Proposal.Layered!;
            Assert.That(layered.Access.Poses!.Members.Count, Is.EqualTo(3));
            Assert.That(layered.Access.Poses.Members.Count(member => member.Support.Layer == KsProcgenPlacementLayer.Container), Is.EqualTo(1));
            Assert.That(layered.Access.Poses.Members.Count(member => member.Support.Layer == KsProcgenPlacementLayer.Surface), Is.EqualTo(1));
            Assert.That(result.Access!.Scene!.CleanHostCells, Is.Empty);
            var audit = KsProcgenGroupInteriorAnalyzer.Analyze(request, result.Partition!, result.Themes!,
                result.Furnishings, result.Lighting!.Regions, structure: result.Structure);
            Assert.That(audit.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), message: audit.Issue?.Code);
            Assert.That(audit.AccessHash, Is.EqualTo(result.Access.AccessHash));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task DisconnectedIslandsCannotMasqueradeAsOneNetwork()
    {
        await AssertOnServer(() =>
        {
            var request = Request(islands: true);
            request.ConnectivityPolicy = KsProcgenConnectivityPolicy.SingleNetwork;
            var result = Plan(request);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Rejected), message: result.Issue?.Code);
            Assert.That(result.SemanticHash, Is.Zero);
            Assert.That(result.Furnishings, Is.Empty);
        });
    }

    [Test]
    public async Task ExplicitRootsRemainRequiredTerminals()
    {
        await AssertOnServer(() =>
        {
            var request = Request();
            request.RootCells.Add(new(2, 0));
            var result = Plan(request);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), message: result.Issue?.Code);
            Assert.That(result.Partition!.ProtectedCells, Does.Contain(new Vector2i(2, 0)));
            Assert.That(request.RootCells, Is.EqualTo(new[] { new Vector2i(2, 0) }));
        });
    }

    [Test]
    public async Task DeclaredRootOnlyNetworksUseTheSameCompleteOwnedPipeline()
    {
        await AssertOnServer(() =>
        {
            var request = Request(islands: true);
            request.ConnectivityPolicy = KsProcgenConnectivityPolicy.DeclaredNetworks;
            request.EntranceDomain!.Configurations.Add(Configuration(new(0, 0), new(10, 0)));
            var result = Plan(request);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), message: result.Issue?.Code);
            Assert.That(result.Partition!.Analysis!.Components.Count, Is.EqualTo(2));
            Assert.That(result.Access!.Analysis!.Ports, Is.Empty);
            Assert.That(result.Access.Analysis.Components.SelectMany(component => component.GroupIds), Is.EquivalentTo(new[] { "A", "B" }));
        });
    }

    [Test]
    public async Task IndependentAuditRejectsAnAccidentalJoinBetweenRootOnlyGroups()
    {
        await AssertOnServer(() =>
        {
            var request = Request();
            request.ConnectivityPolicy = KsProcgenConnectivityPolicy.DeclaredNetworks;
            request.EntranceDomain!.Configurations.Add(Configuration(new(0, 0), new(2, 0)));
            var scene = new KsProcgenEntranceScene(request.Shape.Cells.ToHashSet(), new HashSet<Vector2i>(),
                new HashSet<Vector2i>(), true);
            var result = KsProcgenEntranceSceneAnalyzer.Analyze(request, "Split", scene);
            Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Rejected));
            Assert.That(result.Issue?.Code, Is.EqualTo("ForbiddenEntranceGroupJoin"));
        });
    }

    [Test]
    public async Task EmptyGroupsAndUndeclaredNetworksStillFailValidation()
    {
        await AssertOnServer(() =>
        {
            var request = Request();
            request.ConnectivityPolicy = KsProcgenConnectivityPolicy.DeclaredNetworks;
            Assert.That(KsProcgenGeometry.TryNormalize(request, out _, out var issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("MissingDeclaredNetworkConfigurations"));
            request.EntranceDomain!.Configurations.Add(new() { Id = "Empty", Groups = new() { new() { Id = "Empty", Ports = [], RootCells = [] } } });
            Assert.That(KsProcgenGeometry.TryNormalize(request, out _, out issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("InvalidEntranceGroup"));
            request = Request();
            request.Shape.Cells = Enumerable.Range(0, 1025).Select(x => new Vector2i(x, 0)).ToList();
            request.RootCells = request.Shape.Cells.ToList();
            Assert.That(KsProcgenGeometry.TryNormalize(request, out _, out issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("EntranceRootBudget"));
        });
    }

    [Test]
    public async Task ExhaustedGraphWorkCannotYieldAStandaloneCandidate()
    {
        await AssertOnServer(() =>
        {
            var result = Pair.Server.System<KsProcgenPlanningSystem>().Propose(Request(), "KsProcgenNoDoorThemeFixture", Context(), [],
                budgets: Budgets() with { MaximumReportedGraphExpansions = 0 });
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.BudgetExceeded), message: result.Issue?.Code);
            Assert.That(result.SemanticHash, Is.Zero);
            Assert.That(result.Furnishings, Is.Empty);
        });
    }

    private KsProcgenOwnedInteriorResult Plan(KsProcgenRequest request) =>
        Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, "KsProcgenNoDoorThemeFixture", Context(), [], budgets: Budgets());

    private static KsProcgenOwnedPlanBudgets Budgets() => new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 16 };
    private static KsProcgenGroupRouteContext Context() => new(new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true);
    private static KsProcgenRequest Request(bool islands = false) => new()
    {
        RequestId = "Standalone", Seed = 17, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
        Shape = new() { Cells = islands ? new() { new(0, 0), new(1, 0), new(2, 0), new(10, 0), new(11, 0), new(12, 0) } : new() { new(0, 0), new(1, 0), new(2, 0) } },
        EntranceDomain = new(),
    };
    private static KsProcgenEntranceConfigurationSpec Configuration(Vector2i first, Vector2i second) => new()
    {
        Id = "Split", IsolationScope = KsProcgenEntranceIsolationScope.RequestNetwork,
        Groups = new()
        {
            new() { Id = "A", Ports = [], RootCells = new() { first } },
            new() { Id = "B", Ports = [], RootCells = new() { second } },
        },
    };
}
