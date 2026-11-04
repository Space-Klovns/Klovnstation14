#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Atmos.Reactions;
using Content.Server._KS14.Fluidmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     Hydrogen as an atmos gas: linked to its reagent, so what a pipe makes of it is gas, and flammable.
/// </summary>
[TestOf(typeof(KsHydrogenFireReaction))]
public sealed class KsHydrogenGasTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> HydrogenReagent = "Hydrogen";
    private static readonly ProtoId<GasPrototype> HydrogenGas = "Hydrogen";

    [Test]
    public async Task HydrogenIsLinkedBothWays()
    {
        var server = Pair.Server;
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            var gas = server.ProtoMan.Index(HydrogenGas);
            Assert.That(propertiesSystem.TryGetProperties(HydrogenReagent, out var properties));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(gas.Reagent, Is.EqualTo(HydrogenReagent.Id));
                Assert.That(gas.IsFuel, "Hydrogen doesn't count as a fuel, so a spark won't light it.");
                Assert.That(properties!.Linked, "The hydrogen reagent's vapor isn't the gas.");
                Assert.That(propertiesSystem.ByGas[(int)Gas.Hydrogen], Is.SameAs(properties));
                Assert.That(properties.MolesPerUnit, Is.EqualTo(0.5f).Within(0.001f), "Hydrogen isn't its real molar mass.");
            }
        });
    }

    [Test]
    public async Task LiquidHydrogenBoilsIntoTheGas()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();

        await server.WaitAssertion(() =>
        {
            var mixture = new GasMixture(1000f) { Temperature = 293.15f, FluidCapable = true, Fluid = new KsFluidMixture() };
            var key = new KsFluidReagentKey(HydrogenReagent, null);
            mixture.Fluid.AddLiquid(key, 20f);

            for (var i = 0; i < 50; i++)
            {
                phaseSystem.UpdatePhases(mixture, null);
            }

            // A mixture that has boiled dry drops its fluid altogether.
            var amount = default(KsFluidAmount);
            mixture.Fluid?.TryGetAmount(key, out amount);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(mixture.GetMoles(Gas.Hydrogen), Is.GreaterThan(0f), "Boiling hydrogen didn't make the gas.");
                Assert.That(amount.Liquid, Is.Zero.Within(0.01f), "Hydrogen stayed liquid far above its critical point.");
                Assert.That(amount.Vapor, Is.Zero, "A linked reagent's vapor was kept as fluid vapor.");
            }
        });
    }

    [Test]
    public async Task HydrogenBurnsToSteamInProportion()
    {
        var server = Pair.Server;
        var atmosphereSystem = server.System<AtmosphereSystem>();

        await server.WaitAssertion(() =>
        {
            const float hydrogenMoles = 10f;
            const float oxygenMoles = 2f; // The limiting reactant: burns 4 mol of hydrogen at most.

            var mixture = new GasMixture(1000f) { Temperature = 500f };
            mixture.SetMoles(Gas.Hydrogen, hydrogenMoles);
            mixture.SetMoles(Gas.Oxygen, oxygenMoles);

            for (var i = 0; i < 200; i++)
            {
                atmosphereSystem.React(mixture, null);
            }

            var burned = hydrogenMoles - mixture.GetMoles(Gas.Hydrogen);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(burned, Is.EqualTo(4f).Within(0.05f), "Hydrogen didn't burn two to one with oxygen.");
                Assert.That(mixture.GetMoles(Gas.Oxygen), Is.Zero.Within(0.05f));
                Assert.That(mixture.GetMoles(Gas.WaterVapor), Is.EqualTo(burned).Within(0.05f), "Burning hydrogen didn't leave steam.");
                Assert.That(mixture.Temperature, Is.GreaterThan(500f), "Burning hydrogen gave off no heat.");
            }
        });
    }

    [Test]
    public async Task ColdHydrogenDoesNotBurn()
    {
        var server = Pair.Server;
        var atmosphereSystem = server.System<AtmosphereSystem>();

        await server.WaitAssertion(() =>
        {
            var mixture = new GasMixture(1000f) { Temperature = 293.15f };
            mixture.SetMoles(Gas.Hydrogen, 10f);
            mixture.SetMoles(Gas.Oxygen, 10f);

            atmosphereSystem.React(mixture, null);

            Assert.That(mixture.GetMoles(Gas.Hydrogen), Is.EqualTo(10f), "Hydrogen and oxygen burned at room temperature with nothing to light them.");
        });
    }
}
