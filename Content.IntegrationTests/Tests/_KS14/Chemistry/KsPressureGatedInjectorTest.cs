#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Chemistry.PressureGatedInjector;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.EntitySystems;
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
///     The survival medipen, as tg's: a full dose at once where the air is thin, and half of one after a long wait anywhere else.
/// </summary>
[TestFixture]
[TestOf(typeof(KsPressureGatedInjectorSystem))]
public sealed class KsPressureGatedInjectorTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId SurvivalMedipen = "KsSurvivalMedipen";
    private const string Reagent = "Bicaridine";

    /// <summary>
    ///     How much of <see cref="Reagent"/> the pen holds.
    /// </summary>
    private const float FullDose = 10f;

    private int TicksFor(float seconds) => (int)(seconds * (float)Server.ResolveDependency<IGameTiming>().TickRate);

    private async Task<(EntityUid User, EntityUid Pen)> SetUpAsync(bool air)
    {
        var testMap = await Pair.CreateTestMap();
        var coordinates = new EntityCoordinates(testMap.Grid, 0.5f, 0.5f);
        var userUid = EntityUid.Invalid;
        var penUid = EntityUid.Invalid;

        await Server.WaitAssertion(() =>
        {
            if (air)
            {
                var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
                mixture.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
                mixture.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
                SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, false, mixture);
            }

            userUid = SEntMan.SpawnEntity(Human, coordinates);
            penUid = SEntMan.SpawnEntity(SurvivalMedipen, coordinates);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(userUid, penUid), Is.True);
        });
        await Pair.RunTicksSync(TicksFor(1f));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<KsPressureGatedInjectorComponent>(penUid).LowPressure, Is.EqualTo(!air),
                "precondition: the pen knows the pressure where it is");
            SEntMan.System<SharedInteractionSystem>().UseInHandInteraction(userUid, penUid);
        });

        return (userUid, penUid);
    }

    private float Injected(EntityUid userUid)
    {
        Assert.That(SEntMan.System<SharedSolutionContainerSystem>().TryGetInjectableSolution(userUid, out _, out var solution), Is.True);
        return solution!.GetTotalPrototypeQuantity(Reagent).Float();
    }

    [Test]
    public async Task WhereTheAirIsThinTheWholeDoseGetsInAtOnce()
    {
        var (userUid, _) = await SetUpAsync(air: false);
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Injected(userUid), Is.GreaterThan(FullDose - 1f), "The whole dose must get in at once at low pressure.");
        });
    }

    [Test]
    public async Task InThickAirItTakesLongAndOnlyHalfGetsIn()
    {
        var (userUid, penUid) = await SetUpAsync(air: true);
        await Pair.RunTicksSync(TicksFor(5f));

        await Server.WaitAssertion(() =>
        {
            Assert.That(Injected(userUid), Is.Zero, "Nothing must get in before the gauge is bled.");
        });

        await Pair.RunTicksSync(TicksFor(6f));

        await Server.WaitAssertion(() =>
        {
            var injected = Injected(userUid);
            Assert.That(injected, Is.GreaterThan(FullDose / 2f - 1f).And.LessThanOrEqualTo(FullDose / 2f),
                "Only half the dose must get in at high pressure.");
            Assert.That(SEntMan.Deleted(penUid) || SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(penUid, "hypospray", out _, out var left) && left.Volume == 0,
                Is.True, "What didn't get in must be lost.");
        });
    }
}
