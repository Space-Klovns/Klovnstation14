#nullable enable
using System.Linq;
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared.EntityTable;
using Content.Server.Spawners.Components;
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
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> GasOilReagent = "KsGasOil";
    private static readonly ProtoId<ReagentPrototype> WaxReagent = "KsParaffinWax";
    private static readonly ProtoId<ReagentPrototype> KeroseneReagent = "KsKerosene";
    private static readonly ProtoId<ReagentPrototype> NaphthaReagent = "KsNaphtha";

    private static readonly EntProtoId CrudeGeyserPrototype = "KsGeyserCrudeOil";
    private static readonly EntProtoId GeyserSpawnerPrototype = "GeyserSpawner";

    private const string PetroleumGroup = "KsPetroleum";
    private const string HydrocarbonFamily = "KsHydrocarbon";

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
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));

            using (Assert.EnterMultipleScope())
            {
                foreach (var reagent in server.ProtoMan.EnumeratePrototypes<ReagentPrototype>())
                {
                    if (reagent.Fluidmos?.MixingFamily?.Id != HydrocarbonFamily)
                        continue;

                    Assert.That(reagent.KsInseparableGroup?.Id, Is.EqualTo(PetroleumGroup), $"{reagent.ID} can be picked out by machine.");
                    Assert.That(propertiesSystem.TryGetProperties(reagent.ID, out var properties));
                    Assert.That(properties!.Linked, Is.False, $"{reagent.ID} is linked to a gas, so a gas filter could pick it out.");
                    Assert.That(propertiesSystem.Immiscible(properties, water!), $"{reagent.ID} mixes with water.");
                }
            }
        });
    }

    [Test]
    public async Task CrudeGeyserGivesAMixtureOfCuts()
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

            Assert.That(kinds, Is.GreaterThan(1), "Crude isn't a mixture of cuts.");
        });
    }

    /// <summary>
    ///     Whichever geysers give petroleum, the random spawner has to be able to place one.
    /// </summary>
    [Test]
    public async Task PetroleumGeysersSpawnNaturally()
    {
        var server = Pair.Server;
        var factory = server.EntMan.ComponentFactory;
        var entityTableSystem = server.System<EntityTableSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(server.ProtoMan.Index(GeyserSpawnerPrototype).TryComp<EntityTableSpawnerComponent>(out var spawner, factory));

            bool GivesPetroleum(string prototype) =>
                server.ProtoMan.Index<EntityPrototype>(prototype).TryComp<SolutionRegenerationComponent>(out var regeneration, factory) &&
                regeneration.Generated.Contents.Any(quantity =>
                    server.ProtoMan.Index<ReagentPrototype>(quantity.Reagent.Prototype).KsInseparableGroup?.Id == PetroleumGroup);

            var rolled = new HashSet<string>();
            for (var i = 0; i < 5000; i++)
            {
                foreach (var prototype in entityTableSystem.GetSpawns(spawner!.Table))
                {
                    rolled.Add(prototype);
                }
            }

            Assert.That(rolled.Any(GivesPetroleum), "The geyser spawner never places a petroleum geyser.");
        });
    }

    [Test]
    public async Task WaxCrystallisesOutOfChilledGasOilOnly()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaxReagent, out var wax));
            Assert.That(propertiesSystem.TryGetProperties(GasOilReagent, out var gasOil));

            // Under wax's own melting point, but dissolved in oil; and cold, but not so cold that the oil itself sets.
            var warmTemperature = wax!.MeltingPoint - 5f;
            var chilledTemperature = gasOil!.MeltingPoint + 10f;

            var warm = MakePipe(warmTemperature);
            warm.Fluid!.AddLiquid(new KsFluidReagentKey(GasOilReagent, null), 300f);
            warm.Fluid.AddLiquid(new KsFluidReagentKey(WaxReagent, null), 100f);

            var chilled = MakePipe(chilledTemperature);
            chilled.Fluid!.AddLiquid(new KsFluidReagentKey(GasOilReagent, null), 300f);
            chilled.Fluid.AddLiquid(new KsFluidReagentKey(WaxReagent, null), 100f);

            Step(phaseSystem, gameTiming, warm, 40, holdTemperature: warmTemperature);
            Step(phaseSystem, gameTiming, chilled, 40, holdTemperature: chilledTemperature);

            warm.Fluid!.TryGetAmount(new KsFluidReagentKey(WaxReagent, null), out var warmWax);
            chilled.Fluid!.TryGetAmount(new KsFluidReagentKey(WaxReagent, null), out var chilledWax);
            chilled.Fluid.TryGetAmount(new KsFluidReagentKey(GasOilReagent, null), out var chilledOil);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(warmWax.Solid, Is.Zero, "Wax dissolved in warm oil froze at its pure melting point.");
                Assert.That(chilledWax.Solid, Is.GreaterThan(0f), "Wax didn't crystallise out of chilled oil.");
                Assert.That(chilledOil.Solid, Is.Zero, "The oil froze with the wax, so it can't be pumped off.");
            }
        });
    }

    [Test]
    public async Task VaporOverKeroseneAndNaphthaIsRicherInNaphtha()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(NaphthaReagent, out var naphthaProperties));

            var mixture = MakePipe(naphthaProperties!.BoilingPoint - 10f);
            mixture.Fluid!.AddLiquid(new KsFluidReagentKey(NaphthaReagent, null), 100f);
            mixture.Fluid.AddLiquid(new KsFluidReagentKey(KeroseneReagent, null), 100f);

            Step(phaseSystem, gameTiming, mixture, 200);

            mixture.Fluid!.TryGetAmount(new KsFluidReagentKey(NaphthaReagent, null), out var naphtha);
            mixture.Fluid.TryGetAmount(new KsFluidReagentKey(KeroseneReagent, null), out var kerosene);

            Assert.That(naphtha.Vapor, Is.GreaterThan(kerosene.Vapor), "One still stage doesn't enrich the lighter cut.");
            Assert.That(kerosene.Vapor, Is.GreaterThan(0f), "One stage separated them perfectly, so staging would mean nothing.");
        });
    }
}
