#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Chemistry.PressureGatedInjector;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Chemistry;

/// <summary>
///     The survival medipen: a full dose at once where the air is thin, and part of one after a long wait anywhere else.
///         The delay and the fraction are read from the pen, so retuning it doesn't break the test.
/// </summary>
[TestFixture]
[TestOf(typeof(KsPressureGatedInjectorSystem))]
public sealed class KsPressureGatedInjectorTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId SurvivalMedipen = "KsSurvivalMedipen";
    private static readonly ProtoId<ReagentPrototype> Reagent = "Bicaridine";

    private sealed class Scene
    {
        public EntityUid UserUid;
        public EntityUid PenUid;

        /// <summary>
        ///     How much of <see cref="Reagent"/> the pen held before it was used.
        /// </summary>
        public float Dose;

        public KsPressureGatedInjectorComponent GateComponent = default!;
    }

    private int TicksFor(double seconds) => (int)Math.Ceiling(seconds * (double)Server.ResolveDependency<IGameTiming>().TickRate);

    private async Task<Scene> SetUpAsync(bool air)
    {
        var testMap = await Pair.CreateTestMap();
        var coordinates = new EntityCoordinates(testMap.Grid, 0.5f, 0.5f);
        var scene = new Scene();

        await Server.WaitAssertion(() =>
        {
            if (air)
            {
                var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
                mixture.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
                mixture.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
                SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, false, mixture);
            }

            scene.UserUid = SEntMan.SpawnEntity(Human, coordinates);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, scene.UserUid);
            scene.PenUid = SEntMan.SpawnEntity(SurvivalMedipen, coordinates);
            scene.GateComponent = SEntMan.GetComponent<KsPressureGatedInjectorComponent>(scene.PenUid);
            Assert.That(TryGetPenSolution(scene.PenUid, out var penSolution), Is.True);
            scene.Dose = (float)penSolution!.GetTotalPrototypeQuantity(Reagent.Id);
            Assert.That(scene.Dose, Is.GreaterThan(0f), $"precondition: the pen holds {Reagent}");
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(scene.UserUid, scene.PenUid), Is.True);
        });
        await Pair.RunTicksSync(TicksFor(1));

        await Server.WaitAssertion(() =>
        {
            Assert.That(scene.GateComponent.LowPressure, Is.EqualTo(!air), "precondition: the pen knows the pressure where it is");
            SEntMan.System<SharedInteractionSystem>().UseInHandInteraction(scene.UserUid, scene.PenUid);
        });

        return scene;
    }

    private float Injected(EntityUid userUid)
    {
        Assert.That(SEntMan.System<SharedSolutionContainerSystem>().TryGetInjectableSolution(userUid, out _, out var solution), Is.True);
        return (float)solution!.GetTotalPrototypeQuantity(Reagent.Id);
    }

    private bool TryGetPenSolution(EntityUid penUid, out Solution? solution)
    {
        var injectorComponent = SEntMan.GetComponent<InjectorComponent>(penUid);
        return SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(penUid, injectorComponent.SolutionName, out _, out solution);
    }

    private bool ShowsHighPressurePopup()
    {
        var text = Robust.Shared.Localization.Loc.GetString("ks-pressure-gated-injector-high-pressure");
        return CEntMan.System<Content.Client.Popups.PopupSystem>().WorldLabels.Any(label => label.Text == text);
    }

    [Test]
    public async Task WhereTheAirIsThinTheWholeDoseGetsInAtOnce()
    {
        var scene = await SetUpAsync(air: false);
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Injected(scene.UserUid), Is.GreaterThan(scene.Dose - 1f), "The whole dose must get in at once at low pressure.");
        });
        await Client.WaitAssertion(() =>
        {
            Assert.That(ShowsHighPressurePopup(), Is.False, "Thin air must not tell the user to bleed the gauge.");
        });
    }

    [Test]
    public async Task InThickAirItTakesLongAndOnlyPartGetsIn()
    {
        var scene = await SetUpAsync(air: true);
        var delaySeconds = scene.GateComponent.HighPressureDelay.TotalSeconds;
        var expected = scene.Dose * scene.GateComponent.HighPressureAmount;

        await Pair.RunTicksSync(5);
        await Client.WaitAssertion(() =>
        {
            Assert.That(ShowsHighPressurePopup(), Is.True, "The user must be told why the injection takes longer.");
        });

        await Pair.RunTicksSync(TicksFor(delaySeconds / 2));
        await Server.WaitAssertion(() =>
        {
            Assert.That(Injected(scene.UserUid), Is.Zero, "Nothing must get in before the gauge is bled.");
        });

        await Pair.RunTicksSync(TicksFor(delaySeconds / 2 + 1));
        await Server.WaitAssertion(() =>
        {
            var injected = Injected(scene.UserUid);
            Assert.That(injected, Is.GreaterThan(expected - 1f).And.LessThanOrEqualTo(expected),
                "Only part of the dose must get in at high pressure.");
            Assert.That(SEntMan.Deleted(scene.PenUid) || !TryGetPenSolution(scene.PenUid, out var penSolution) || penSolution!.Volume == 0,
                Is.True, "What didn't get in must be lost.");
        });
    }
}
