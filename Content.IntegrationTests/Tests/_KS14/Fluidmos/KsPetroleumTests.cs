#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     The refinery's reagents: every hydrocarbon stays out of reach of gas filters and in its inseparable group, geysers
///         give a mixture of them, and they behave as the refinery needs in a pipe.
/// </summary>
[TestOf(typeof(KsFluidmosPhaseSystem))]
public sealed class KsPetroleumTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> GasOilReagent = "KsGasOil";
    private static readonly ProtoId<ReagentPrototype> WaxReagent = "KsParaffinWax";
    private static readonly ProtoId<ReagentPrototype> KeroseneReagent = "KsKerosene";
    private static readonly ProtoId<ReagentPrototype> NaphthaReagent = "KsNaphtha";

    private static readonly EntProtoId CrudeGeyserPrototype = "KsGeyserCrudeOil";
    private static readonly EntProtoId GeyserSpawnerPrototype = "GeyserSpawner";

    private const string PetroleumGroup = "KsPetroleum";

    private static readonly ProtoId<ReagentPrototype>[] Hydrocarbons =
    [
        "KsRefineryGas", "KsEthane", "KsEthylene", "KsNaphtha", "KsGasoline", "KsKerosene", "KsDiesel", "KsGasOil",
        "KsParaffinWax", "KsBitumen", "KsOrganosulfur", "KsPolyethylene",
    ];

    private static GasMixture MakePipe(float temperature, float pressure = Atmospherics.OneAtmosphere)
    {
        var mixture = new GasMixture(200f) { Temperature = temperature, FluidCapable = true, Fluid = new KsFluidMixture() };
        mixture.AdjustMoles(Gas.Nitrogen, pressure * mixture.Volume / (Atmospherics.R * temperature));
        return mixture;
    }

    private static void Step(KsFluidmosPhaseSystem phaseSystem, IGameTiming gameTiming, GasMixture mixture, int steps, float? holdTemperature = null)
    {
        for (var i = 0; i < steps; i++)
        {
            if (holdTemperature is { } temperature)
                mixture.Temperature = temperature;

            mixture.Fluid!.LastPhaseTime = gameTiming.CurTime - TimeSpan.FromSeconds(0.5f);
            phaseSystem.UpdatePhases(mixture, holder: null);
        }
    }

    [Test]
    public async Task EveryHydrocarbonIsUnlinkedGroupedAndInItsFamily()
    {
        var server = Pair.Server;
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties("Water", out var water));

            using (Assert.EnterMultipleScope())
            {
                foreach (var reagent in Hydrocarbons)
                {
                    Assert.That(server.ProtoMan.Index(reagent).KsInseparableGroup?.Id, Is.EqualTo(PetroleumGroup), $"{reagent} can be picked out by machine.");
                    Assert.That(propertiesSystem.TryGetProperties(reagent, out var properties));
                    Assert.That(properties!.Linked, Is.False, $"{reagent} is linked to a gas, so a gas filter could pick it out.");
                    Assert.That(propertiesSystem.Immiscible(properties, water!), $"{reagent} mixes with water.");
                }
            }
        });
    }

    [Test]
    public async Task CrudeGeyserGivesAMixtureAndSpawnsNaturally()
    {
        var server = Pair.Server;
        var factory = server.EntMan.ComponentFactory;

        await server.WaitAssertion(() =>
        {
            Assert.That(server.ProtoMan.Index(CrudeGeyserPrototype).TryComp<SolutionRegenerationComponent>(out var regeneration, factory));

            var kinds = 0;
            foreach (var quantity in regeneration!.Generated.Contents)
            {
                Assert.That(server.ProtoMan.Index<ReagentPrototype>(quantity.Reagent.Prototype).KsInseparableGroup?.Id, Is.EqualTo(PetroleumGroup));
                kinds++;
            }

            Assert.That(kinds, Is.GreaterThan(5), "Crude isn't a mixture of cuts.");
            Assert.That(server.ProtoMan.HasIndex(GeyserSpawnerPrototype));
        });
    }

    [Test]
    public async Task WaxCrystallisesOutOfChilledGasOilOnly()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            var warm = MakePipe(325f);
            warm.Fluid!.AddLiquid(new KsFluidReagentKey(GasOilReagent, null), 300f);
            warm.Fluid.AddLiquid(new KsFluidReagentKey(WaxReagent, null), 100f);

            var chilled = MakePipe(300f);
            chilled.Fluid!.AddLiquid(new KsFluidReagentKey(GasOilReagent, null), 300f);
            chilled.Fluid.AddLiquid(new KsFluidReagentKey(WaxReagent, null), 100f);

            Step(phaseSystem, gameTiming, warm, 40, holdTemperature: 325f);
            Step(phaseSystem, gameTiming, chilled, 40, holdTemperature: 300f);

            warm.Fluid!.TryGetAmount(new KsFluidReagentKey(WaxReagent, null), out var warmWax);
            chilled.Fluid!.TryGetAmount(new KsFluidReagentKey(WaxReagent, null), out var chilledWax);
            chilled.Fluid.TryGetAmount(new KsFluidReagentKey(GasOilReagent, null), out var chilledOil);

            using (Assert.EnterMultipleScope())
            {
                // Below wax's own 330 K, but dissolved in oil.
                Assert.That(warmWax.Solid, Is.Zero, "Wax dissolved in warm oil froze at its pure melting point.");
                Assert.That(chilledWax.Solid, Is.GreaterThan(90f), "Wax didn't crystallise out of chilled oil.");
                Assert.That(chilledOil.Solid, Is.Zero, "The oil froze with the wax, so it can't be pumped off.");
            }
        });
    }

    [Test]
    public async Task VaporOverKeroseneAndNaphthaIsRicherInNaphtha()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            var mixture = MakePipe(360f);
            mixture.Fluid!.AddLiquid(new KsFluidReagentKey(NaphthaReagent, null), 100f);
            mixture.Fluid.AddLiquid(new KsFluidReagentKey(KeroseneReagent, null), 100f);

            Step(phaseSystem, gameTiming, mixture, 200);

            mixture.Fluid!.TryGetAmount(new KsFluidReagentKey(NaphthaReagent, null), out var naphtha);
            mixture.Fluid.TryGetAmount(new KsFluidReagentKey(KeroseneReagent, null), out var kerosene);

            Assert.That(naphtha.Vapor, Is.GreaterThan(5f * kerosene.Vapor), "One still stage doesn't enrich the lighter cut.");
            Assert.That(kerosene.Vapor, Is.GreaterThan(0f), "One stage separated them perfectly, so staging would mean nothing.");
        });
    }
}
