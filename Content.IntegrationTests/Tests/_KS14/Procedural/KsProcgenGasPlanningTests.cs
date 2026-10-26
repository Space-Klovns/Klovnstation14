#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Server.Atmos.Components;
using Content.Shared._KS14.Procedural;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenGasPlanningContext))]
public sealed class KsProcgenGasPlanningTests : KsProcgenGameTest
{
    [TestCase(1)] [TestCase(3)]
    public async Task PrimaryEntryRetainsNamedGasForTinyFootprintsWithoutAllocating(int length)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var request = Strip(length);
            request.AtmosphereProfile = "KsProcgenStandardAir";
            var result = Propose(request);
            AssertCandidate(result);
            Assert.That(result.Gas!.Domains.Single().Cells, Is.EquivalentTo(request.Shape.Cells));
            Assert.That(result.Gas.Domains.Single().NominalProfile!.SourceProfileId, Is.EqualTo("KsProcgenStandardAir"));
            Assert.That(result.Gas.Domains.Single().Id, Is.EqualTo("Gas/0/0"));
            Assert.That(result.Gas.PrototypeDeclarationHash, Has.Length.EqualTo(64));
            Assert.That(result.Gas.ContextDeclarationHash, Is.Not.Zero);
            Assert.That(Propose(request).SemanticHash, Is.EqualTo(result.SemanticHash));
            var staged = Pair.Server.System<KsProcgenOwnedStageSystem>().TryGenerate(request, "KsProcgenNoDoorThemeFixture", planningBudgets: Budgets());
            Assert.That(staged.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: staged.Issue?.Code);
            Assert.That(staged.Stage!.InitialAtmosphere, Is.Null);
            var initialized = Pair.Server.System<KsProcgenOwnedStageSystem>().TryInitialize(staged.Stage);
            Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected));
            Assert.That(initialized.Issue?.Code, Is.EqualTo("OwnedGasDomainLeaks"));
            Assert.That(staged.Stage.Active, Is.False); Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task GasFailuresRetryCompleteSubdivisionsWithoutExposingDiscardedArtifacts()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var request = Room();
            request.Utilities = new() { GasAssignments = new() { new() { Id = "AbsentRoom", Scope = KsProcgenUtilityScope.Room,
                ScopeId = "NotInAnyCandidate", ProfileId = "KsProcgenStandardAir" } } };
            var result = Propose(request, budgets: Budgets() with { PreferredMaximumRoomCells = 8, AlternativeMaximumRoomCells = [64] },
                themeId: "KsProcgenEmptyOfficeThemeFixture");
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Rejected), message: result.Issue?.Code);
            Assert.That(result.Attempts.Count, Is.EqualTo(2));
            Assert.That(result.Attempts.Select(attempt => attempt.ReasonCode), Is.All.EqualTo("UnknownGasAssignmentTarget"));
            Assert.That(result.Stages.Count(stage => stage.Stage == KsProcgenOwnedPlanStage.GasBinding), Is.EqualTo(2));
            Assert.That(result.ExpandedCells, Is.EqualTo(result.Attempts.Sum(attempt => attempt.ExpandedCells)));
            Assert.That(result.Gas, Is.Null); Assert.That(result.Partition, Is.Null); Assert.That(result.Furnishings, Is.Empty);
            Assert.That(result.SemanticHash, Is.Zero); Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task GasWorkUsesTheSharedBudgetAndChangesCandidateIdentity()
    {
        await AssertOnServer(() =>
        {
            var request = Strip(3); request.AtmosphereProfile = "KsProcgenStandardAir";
            var result = Propose(request); AssertCandidate(result);
            var exact = Propose(request, budgets: Budgets() with { MaximumReportedGraphExpansions = result.ExpandedCells });
            AssertCandidate(exact); Assert.That(exact.SemanticHash, Is.EqualTo(result.SemanticHash));
            var shortResult = Propose(request, budgets: Budgets() with { MaximumReportedGraphExpansions = result.ExpandedCells - 1 });
            Assert.That(shortResult.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.BudgetExceeded));
            Assert.That(shortResult.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.GasBinding));
            Assert.That(shortResult.Gas, Is.Null); Assert.That(shortResult.SemanticHash, Is.Zero);
            Assert.That(shortResult.ExpandedCells, Is.LessThanOrEqualTo(result.ExpandedCells - 1));
            request.AtmosphereProfile = "KsProcgenNitrogenRoom";
            var nitrogen = Propose(request); AssertCandidate(nitrogen);
            Assert.That(nitrogen.SemanticHash, Is.Not.EqualTo(result.SemanticHash));
            Assert.That(nitrogen.Gas!.BindingHash, Is.Not.EqualTo(result.Gas!.BindingHash));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task GasContextCannotSilentlyAcceptElectricalContracts(bool reservationsOnly)
    {
        await AssertOnServer(() =>
        {
            var request = Strip(3); request.AtmosphereProfile = "KsProcgenStandardAir";
            request.Utilities = new();
            if (reservationsOnly) request.Utilities.CableWriteCells = new();
            else request.Utilities.Consumers.Add(new() { Id = "Equipment" });
            var result = Propose(request);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.InvalidInput));
            Assert.That(result.Issue?.Code, Is.EqualTo("ElectricalPlanningUnsupported"));
            Assert.That(result.Gas, Is.Null); Assert.That(result.SemanticHash, Is.Zero);
        });
    }

    [Test]
    public async Task CapturedThemeDefaultsAreCarriedThroughSelectionAndCanBeOverridden()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var result = Pair.Server.System<KsProcgenPlanningSystem>().Propose(Room(), "KsProcgenNitrogenOfficeTheme", Context(), [], budgets: Budgets());
            AssertCandidate(result);
            Assert.That(result.Themes!.Regions.All(region => region.Theme.Content!.Atmosphere!.MoleFractions.Single().GasId == "Nitrogen"), Is.True);
            Assert.That(result.Gas!.Domains.All(domain => domain.NominalProfile!.MoleFractions.Single().GasId == "Nitrogen"), Is.True);
            var request = Room(); request.AtmosphereProfile = "KsProcgenStandardAir";
            var overridden = Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, "KsProcgenNitrogenOfficeTheme", Context(), [], budgets: Budgets());
            AssertCandidate(overridden);
            Assert.That(overridden.Gas!.Domains.All(domain => domain.NominalProfile!.MoleFractions.Count == 2), Is.True);
            Assert.That(KsProcgenThemeSelector.TrySelect(manager, "KsProcgenNitrogenOfficeTheme", 17, "Legacy", out _, out var issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("AtmospherePlanningUnsupported"));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task UnknownPreservedOrHostGasContextCannotBeReplacedWithEmptySpace(bool host)
    {
        await AssertOnServer(() =>
        {
            var request = Strip(3); request.AtmosphereProfile = "KsProcgenStandardAir";
            if (host) request.EntranceDomain!.Entrances.Add(new() { PortId = "Host", ThresholdCells = new() { new(-1, 0) }, InwardNormal = new(1, 0) });
            else request.Shape.PreservedCells.Add(new(1, 0));
            var result = Propose(request);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.InvalidInput));
            Assert.That(result.Issue?.Code, Is.EqualTo("HostedGasContextCaptureRequired"));
            Assert.That(result.Gas, Is.Null); Assert.That(result.Attempts, Is.Empty);
        });
    }

    [Test]
    public async Task ProposedGasVolumesAgreeWithInitializedAnchoredSceneBlockers()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var request = Room();
            var target = request.Shape.AddRectangles[0];
            request.Shape.EnvelopeCells = Enumerable.Range(-1, 9).SelectMany(x => Enumerable.Range(-1, 9)
                .Select(y => new Vector2i(x, y))).Where(cell => cell.X < target.Min.X || cell.X >= target.Max.X ||
                    cell.Y < target.Min.Y || cell.Y >= target.Max.Y).ToList();
            var before = SEntMan.GetEntities().ToHashSet();
            var staged = stages.TryGenerate(request, "KsProcgenEmptyOfficeThemeFixture", planningBudgets: Budgets());
            try
            {
                Assert.That(staged.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: staged.Issue?.Code);
                Assert.That(stages.TryInitialize(staged.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                request.AtmosphereProfile = "KsProcgenStandardAir";
                var proposed = Propose(request, themeId: "KsProcgenEmptyOfficeThemeFixture"); AssertCandidate(proposed);
                var actualDirections = new Dictionary<Vector2i, AtmosDirection>(); var noAir = new HashSet<Vector2i>();
                foreach (var uid in staged.Stage!.Entities)
                {
                    var transform = SEntMan.GetComponent<TransformComponent>(uid);
                    if (!transform.Anchored || !SEntMan.TryGetComponent<AirtightComponent>(uid, out var airtight) || !airtight.AirBlocked) continue;
                    var cell = new Vector2i((int) System.Math.Floor((double) transform.LocalPosition.X), (int) System.Math.Floor((double) transform.LocalPosition.Y));
                    actualDirections[cell] = actualDirections.GetValueOrDefault(cell) | airtight.AirBlockedDirection;
                    if (airtight.NoAirWhenFullyAirBlocked) noAir.Add(cell);
                }
                Assert.That(actualDirections.Count, Is.GreaterThan(0));
                var expected = proposed.Partition!.FloorCells.Concat(proposed.Partition.WallCells)
                    .Concat(proposed.Structure!.RemainingWalls.Select(wall => wall.Cell)).Concat(proposed.Structure.Windows.Select(window => window.Cell))
                    .Where(cell => actualDirections.GetValueOrDefault(cell) != AtmosDirection.All || !noAir.Contains(cell)).ToHashSet();
                Assert.That(proposed.Gas!.Domains.SelectMany(domain => domain.Cells), Is.EquivalentTo(expected));
                Assert.That(proposed.Gas.Domains.SelectMany(domain => domain.Cells).Any(cell =>
                    actualDirections.GetValueOrDefault(cell) == AtmosDirection.All && noAir.Contains(cell)), Is.False);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task SupportedAndContainedMembersRemainInTheGasBearingCandidate()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var catalog = KsProcgenOwnedLayeredTests.Catalog(manager, contained: true);
            var request = Room(); request.AtmosphereProfile = "KsProcgenStandardAir";
            request.Shape.AddRectangles[0].Max = new(6, 6);
            var result = Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, "Layered", Context(), [], budgets: Budgets(), content: catalog);
            AssertCandidate(result);
            var layered = result.Furnishings.Single(region => region.Proposal.Layered != null).Proposal.Layered!;
            Assert.That(layered.Access.Poses!.Members.Any(member => member.Support.Layer == KsProcgenPlacementLayer.Surface), Is.True);
            Assert.That(layered.Access.Poses.Members.Any(member => member.Support.Layer == KsProcgenPlacementLayer.Container), Is.True);
            Assert.That(result.Gas!.Domains.SelectMany(domain => domain.Cells), Is.SupersetOf(layered.FloorEntities.Select(entity => entity.Cell)));
        });
    }

    private KsProcgenOwnedInteriorResult Propose(KsProcgenRequest request, KsProcgenOwnedPlanBudgets? budgets = null,
        string themeId = "KsProcgenNoDoorThemeFixture") =>
        Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, themeId, Context(), [], budgets: budgets ?? Budgets());
    private static void AssertCandidate(KsProcgenOwnedInteriorResult result)
    {
        Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), message: result.Issue?.Code);
        Assert.That(result.Gas?.Status, Is.EqualTo(KsProcgenGasBindingStatus.Bound));
        Assert.That(result.Gas!.GasClosureVerified || result.Gas.InitialGasVerified || result.Published, Is.False);
    }
    private static KsProcgenOwnedPlanBudgets Budgets() => new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64 };
    private static KsProcgenGroupRouteContext Context() => new(new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true)
        { ExteriorIsEmptySpace = true };
    private static KsProcgenRequest Strip(int length) => new() { SchemaVersion = 2, RequestId = "GasCandidate", Seed = 17,
        ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland, EntranceDomain = new(),
        Shape = new() { Cells = Enumerable.Range(0, length).Select(x => new Vector2i(x, 0)).ToList() } };
    private static KsProcgenRequest Room()
    {
        var request = Strip(0); request.Shape.AddRectangles.Add(new() { Min = new(0, 0), Max = new(7, 7) }); return request;
    }
}
