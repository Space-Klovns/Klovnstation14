#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Content.Server._KS14.Procedural;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Procedural;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenOwnedAtmosphereSystem))]
public sealed class KsProcgenOwnedAtmosphereTests : KsProcgenGameTest
{
    [TestCase(1)] [TestCase(3)]
    public async Task TinyFootprintsCanReceiveGasWhenAnExplicitEnvelopeProvidesASeal(int length)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var request = Room("KsProcgenStandardAir");
            request.Shape.Cells = Enumerable.Range(0, length).Select(x => new Vector2i(x, 0)).ToList();
            request.Shape.EnvelopeCells = Enumerable.Range(-1, length + 2).SelectMany(x => Enumerable.Range(-1, 3)
                .Select(y => new Vector2i(x, y))).Where(cell => !request.Shape.Cells.Contains(cell)).ToList();
            var generated = Generate(request);
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, message: generated.Issue?.Code);
                var initialized = stages.TryInitialize(generated.Stage!);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), message: initialized.Issue?.Code);
                Assert.That(generated.Stage!.InitialAtmosphere!.Cells.Count(state => state.OwnedMixture), Is.EqualTo(length));
                Assert.That(request.Shape.Cells.All(cell => System.Math.Abs(Mixture(generated.Stage, cell).Pressure - 101.325f) <= 0.1f), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase("KsProcgenStandardAir")] [TestCase("KsProcgenNitrogenRoom")] [TestCase("KsProcgenVacuum")]
    public async Task RealClosedRoomReceivesIndependentTileMixturesAndAReadOnlyInitialReceipt(string profile)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var prototypes = Pair.Server.ResolveDependency<IPrototypeManager>();
            var factory = Pair.Server.ResolveDependency<IComponentFactory>();
            foreach (var prototypeId in new[] { "WallReinforced", "WallSolid", "Window" })
            {
                prototypes.Index<EntityPrototype>(prototypeId).TryComp<AirtightComponent>(out var defaults, factory);
                Assert.That(defaults!.InitialAirBlockedDirection, Is.EqualTo((int) AtmosDirection.All), message: prototypeId);
                Assert.That(defaults.NoAirWhenFullyAirBlocked, Is.True, message: prototypeId);
            }
            var result = Generate(Room(profile));
            try
            {
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.Code);
                Assert.That(result.Stage!.InitialAtmosphere, Is.Null);
                foreach (var uid in result.Stage.Entities.Where(uid => SEntMan.GetComponent<TransformComponent>(uid).LocalPosition == new System.Numerics.Vector2(-0.5f, 6.5f)))
                {
                    var airtight = SEntMan.GetComponent<AirtightComponent>(uid);
                    Assert.That(airtight.InitialAirBlockedDirection, Is.EqualTo((int) AtmosDirection.All));
                    Assert.That(airtight.AirBlockedDirection, Is.EqualTo(AtmosDirection.All));
                    Assert.That(airtight.NoAirWhenFullyAirBlocked, Is.True);
                    Assert.That(SEntMan.GetComponent<TransformComponent>(uid).Anchored, Is.True);
                }
                var initialized = stages.TryInitialize(result.Stage);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), message: initialized.Issue?.Code);
                var receipt = result.Stage.InitialAtmosphere!;
                Assert.That(receipt.InitialGasVerified, Is.True);
                Assert.That(receipt.AtmosphereAtRestVerified || result.Stage.Published || result.Stage.GasClosureVerified, Is.False);
                Assert.That(receipt.BindingHash, Is.EqualTo(result.Proposal!.Gas!.BindingHash));
                var expectedCells = result.Proposal.Gas.Domains.SelectMany(domain => domain.Cells).ToArray();
                Assert.That(receipt.Cells.Where(cell => cell.OwnedMixture).Select(cell => cell.Cell), Is.EquivalentTo(expectedCells));
                var mixtures = expectedCells.Select(cell => Mixture(result.Stage, cell)).ToArray();
                Assert.That(mixtures.Length, Is.GreaterThan(1));
                Assert.That(mixtures[0], Is.Not.SameAs(mixtures[1]));
                foreach (var mixture in mixtures)
                {
                    Assert.That(mixture.Immutable, Is.False); Assert.That(mixture.Volume, Is.EqualTo(Atmospherics.CellVolume));
                    Assert.That(mixture.Pressure, Is.EqualTo(profile == "KsProcgenVacuum" ? 0f : 101.325f).Within(0.1f));
                    if (profile == "KsProcgenNitrogenRoom") Assert.That(mixture.GetMoles(Gas.Oxygen), Is.Zero);
                    if (profile == "KsProcgenStandardAir") Assert.That(mixture.GetMoles(Gas.Oxygen) / mixture.TotalMoles, Is.EqualTo(0.21f).Within(0.001f));
                }
                var firstReference = mixtures[0];
                Assert.That(stages.TryInitialize(result.Stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(result.Stage.InitialAtmosphere, Is.SameAs(receipt));
                Assert.That(Mixture(result.Stage, expectedCells[0]), Is.SameAs(firstReference));
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task GasLossOrUnexpectedSpeciesRejectsRepeatInitializationWithoutRefilling(bool contaminant)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var result = Generate(Room("KsProcgenStandardAir"));
            try
            {
                Assert.That(result.Stage, Is.Not.Null, message: result.Issue?.Code);
                Assert.That(stages.TryInitialize(result.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                var receipt = result.Stage!.InitialAtmosphere!;
                var cell = receipt.Cells.First(state => state.OwnedMixture).Cell;
                var mixture = Mixture(result.Stage, cell);
                var originalOxygen = mixture.GetMoles(Gas.Oxygen);
                if (contaminant) mixture.SetMoles(Gas.Plasma, 10f);
                else mixture.SetMoles(Gas.Oxygen, 0f);
                Assert.That(stages.Verify(result.Stage), Is.False);
                Assert.That(stages.TryInitialize(result.Stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected));
                Assert.That(result.Stage.Active, Is.False);
                Assert.That(mixture.GetMoles(Gas.Oxygen), Is.EqualTo(contaminant ? originalOxygen : 0f));
                Assert.That(receipt.Cells.Single(state => state.Cell == cell).Gases.Single(gas => gas.GasId == "Oxygen").Moles, Is.EqualTo(originalOxygen));
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task MissingBarrierBeforeOrAfterFillRejectsAndRetiresTheWholeStage(bool afterFill)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var result = Generate(Room("KsProcgenStandardAir"));
            try
            {
                Assert.That(result.Stage, Is.Not.Null, message: result.Issue?.Code);
                if (afterFill) Assert.That(stages.TryInitialize(result.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                var wall = result.Stage!.Entities.First(uid => SEntMan.GetComponent<TransformComponent>(uid).LocalPosition == new System.Numerics.Vector2(-0.5f, 3.5f));
                Pair.Server.System<AirtightSystem>().SetAirblocked((wall, SEntMan.GetComponent<AirtightComponent>(wall)), false);
                var initialized = stages.TryInitialize(result.Stage);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected));
                Assert.That(initialized.Issue?.Code, Is.EqualTo(afterFill ? "OwnedAtmosBoundaryDrift" : "OwnedGasVolumeTopologyDrift"));
                Assert.That(result.Stage.Active, Is.False);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ASecondPrivateGridKeepsItsOriginalGasWhenAnotherRoomIsFilled()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var first = Generate(Room("KsProcgenNitrogenRoom"));
            try
            {
                Assert.That(stages.TryInitialize(first.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                var cell = first.Stage!.InitialAtmosphere!.Cells.First(state => state.OwnedMixture).Cell;
                var original = Mixture(first.Stage, cell); var nitrogen = original.GetMoles(Gas.Nitrogen);
                var second = Generate(Room("KsProcgenStandardAir"));
                Assert.That(stages.TryInitialize(second.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(Mixture(first.Stage, cell), Is.SameAs(original));
                Assert.That(original.GetMoles(Gas.Nitrogen), Is.EqualTo(nitrogen)); Assert.That(original.GetMoles(Gas.Oxygen), Is.Zero);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task RoomScopedGasRemainsDifferentAcrossActualClosedInternalDoors()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var request = Room("KsProcgenStandardAir");
            request.EntranceDomain = new();
            var budgets = new KsProcgenOwnedPlanBudgets { MinimumRoomCells = 1, PreferredMaximumRoomCells = 12 };
            var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true)
                { ExteriorIsEmptySpace = true };
            var baseline = Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, "KsProcgenEmptyOfficeThemeFixture", context, [], budgets: budgets);
            Assert.That(baseline.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), message: baseline.Issue?.Code);
            Assert.That(baseline.Themes!.Regions.Count, Is.GreaterThan(1));
            var roomId = baseline.Themes.Regions.First().Id;
            request.Utilities = new() { GasAssignments = new() { new() { Id = "Nitrogen", Scope = KsProcgenUtilityScope.Room,
                ScopeId = roomId, ProfileId = "KsProcgenNitrogenRoom" } } };
            var result = stages.TryGenerate(request, "KsProcgenEmptyOfficeThemeFixture", planningBudgets: budgets);
            try
            {
                Assert.That(result.Stage, Is.Not.Null, message: result.Issue?.Code);
                var initialized = stages.TryInitialize(result.Stage!);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), message: initialized.Issue?.Code);
                var nitrogen = result.Proposal!.Gas!.Domains.Where(domain => domain.RoomIds.Contains(roomId)).SelectMany(domain => domain.Cells).ToArray();
                Assert.That(nitrogen, Is.Not.Empty);
                Assert.That(nitrogen.All(cell => Mixture(result.Stage!, cell).GetMoles(Gas.Oxygen) == 0f), Is.True);
                Assert.That(result.Stage!.InitialAtmosphere!.Cells.Any(state => state.Gases.Any(gas => gas.GasId == "Oxygen" && gas.Moles > 0f)), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task HardGasRestrictionRejectsAContaminantBelowOrdinaryFractionTolerance()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var request = Room("KsProcgenStandardAir");
            request.Utilities = new() { GasRestrictions = new() { AllowedGases = new() { "Oxygen", "Nitrogen" } } };
            var result = Generate(request);
            try
            {
                var initialized = stages.TryInitialize(result.Stage!);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), message: initialized.Issue?.Code);
                var cell = result.Stage!.InitialAtmosphere!.Cells.First(state => state.OwnedMixture).Cell;
                Mixture(result.Stage, cell).SetMoles(Gas.Plasma, 0.00001f);
                Assert.That(stages.Verify(result.Stage), Is.False);
                var repeated = stages.TryInitialize(result.Stage);
                Assert.That(repeated.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected));
                Assert.That(repeated.Issue?.Code, Is.EqualTo("OwnedGasRestrictionDrift"));
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)]
    public async Task TileChangeCallbacksCannotCorruptMoveOrCancelTheStageAndStillGetAReceipt(int action)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var maps = Pair.Server.System<SharedMapSystem>();
            var transforms = Pair.Server.System<SharedTransformSystem>();
            var callbacks = Pair.Server.System<KsProcgenInsertionTestSystem>();
            using var cancellation = new CancellationTokenSource();
            var before = SEntMan.GetEntities().ToHashSet();
            var foreignMapUid = maps.CreateMap(out var foreignMapId, runMapInit: false);
            maps.SetPaused(foreignMapId, true); maps.InitializeMap(foreignMapId, unpause: false);
            callbacks.Reset();
            try
            {
                var catalog = KsProcgenOwnedLayeredTests.Catalog(Pair.Server.ResolveDependency<IPrototypeManager>(), lighting: false);
                var result = stages.TryGenerate(Room("KsProcgenStandardAir"), "Layered", content: catalog,
                    planningBudgets: new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64 });
                Assert.That(result.Stage, Is.Not.Null, message: result.Issue?.Code);
                var stage = result.Stage!;
                var tableUid = stage.Assemblies.SelectMany(assembly => assembly.Values)
                    .First(uid => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "Table");
                var attempts = 0;
                callbacks.GasTileChangedAction = uid =>
                {
                    if (uid != tableUid) return;
                    var position = SEntMan.GetComponent<TransformComponent>(uid).LocalPosition;
                    var cell = new Vector2i((int) System.Math.Floor((double) position.X), (int) System.Math.Floor((double) position.Y));
                    var mixture = Mixture(stage, cell);
                    if (mixture.Pressure < 1f) return;
                    attempts++; callbacks.GasTileChangedAction = null;
                    if (action == 1) transforms.SetCoordinates(stage.GridUid, new EntityCoordinates(foreignMapUid, System.Numerics.Vector2.Zero));
                    else if (action == 2) cancellation.Cancel();
                    else mixture.SetMoles(Gas.Oxygen, 0f);
                };
                var initialized = stages.TryInitialize(stage, cancellationToken: cancellation.Token);
                Assert.That(attempts, Is.EqualTo(1));
                Assert.That(initialized.Status, Is.EqualTo(action == 2 ? KsProcgenOwnedStageStatus.Cancelled : KsProcgenOwnedStageStatus.Rejected), message: initialized.Issue?.Code);
                Assert.That(initialized.Issue?.Code, Is.EqualTo(action switch
                { 1 => "OwnedAtmosFrameCallbackDrift", 2 => "OwnedStageInitializationCancelled", _ => "OwnedGasInitialStateDrift" }));
                Assert.That(stage.Active, Is.False); Assert.That(stage.InitialAtmosphere, Is.Null);
                Assert.That(SEntMan.EntityExists(foreignMapUid), Is.True);
            }
            finally
            {
                callbacks.Reset(); stages.DiscardAll();
                if (maps.MapExists(foreignMapId)) maps.DeleteMap(foreignMapId);
            }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task NarrowGasPermissionsAndStableAtRestRemainExplicitGuards(bool stability)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var request = Room("KsProcgenStandardAir");
            if (stability)
            {
                request.AtmosphereProfile = null;
                request.Atmosphere = new() { Mode = KsProcgenAtmosphereMode.Vacuum, PressureKPa = 0f, TemperatureKelvin = 293.15f,
                    StateRequirement = KsProcgenAtmosphereState.StableAtRest, ObservationTicks = 5 };
            }
            else request.Utilities = new() { GasWriteCells = new() };
            var result = Generate(request);
            Assert.That(result.Stage, Is.Null);
            Assert.That(result.Issue?.Code, Is.EqualTo(stability ? "OwnedGasStabilityUnsupported" : "GasDomainWritePermissionRequired"));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private GasMixture Mixture(KsProcgenOwnedStage stage, Vector2i cell) =>
        Pair.Server.System<AtmosphereSystem>().GetTileMixture(stage.GridUid, stage.MapUid, cell)!;
    private KsProcgenOwnedStageResult Generate(KsProcgenRequest request) =>
        Pair.Server.System<KsProcgenOwnedStageSystem>().TryGenerate(request, "KsProcgenEmptyOfficeThemeFixture",
            planningBudgets: new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64 });
    private static KsProcgenRequest Room(string profile) => new()
    {
        SchemaVersion = 2, RequestId = "OwnedGas", Seed = 17, AtmosphereProfile = profile,
        ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
        Shape = new() { Cells = Enumerable.Range(0, 7).SelectMany(x => Enumerable.Range(0, 7).Select(y => new Vector2i(x, y))).ToList(),
            EnvelopeCells = Enumerable.Range(-1, 9).SelectMany(x => Enumerable.Range(-1, 9).Select(y => new Vector2i(x, y)))
                .Where(cell => cell.X is -1 or 7 || cell.Y is -1 or 7).ToList() },
    };
}
