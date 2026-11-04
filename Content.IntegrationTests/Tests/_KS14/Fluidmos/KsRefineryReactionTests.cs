#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Server._KS14.Fluidmos.Devices;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.Fluidmos;
using Content.Shared._KS14.Fluidmos.Reactions;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     The refinery's reactions and catalysts, with the real prototypes: every reaction is well formed, and each step
///         that gives the chain its order does what it is there for.
/// </summary>
[TestOf(typeof(KsCatalyticReactorSystem))]
public sealed class KsRefineryReactionTests : GameTest
{
    private static readonly EntProtoId ReactorPrototype = "KsCatalyticReactor";
    private static readonly EntProtoId PlatinumBedPrototype = "KsCatalystBedPlatinum";
    private static readonly EntProtoId CobaltMolybdenumBedPrototype = "KsCatalystBedCobaltMolybdenum";

    private static readonly ProtoId<ReagentPrototype> NaphthaReagent = "KsNaphtha";
    private static readonly ProtoId<ReagentPrototype> KeroseneReagent = "KsKerosene";
    private static readonly ProtoId<ReagentPrototype> GasOilReagent = "KsGasOil";
    private static readonly ProtoId<ReagentPrototype> GasolineReagent = "KsGasoline";
    private static readonly ProtoId<ReagentPrototype> OrganosulfurReagent = "KsOrganosulfur";
    private static readonly ProtoId<ReagentPrototype> SulfideReagent = "KsHydrogenSulfide";
    private static readonly ProtoId<ReagentPrototype> CokeReagent = "KsPetroleumCoke";
    private static readonly ProtoId<ReagentPrototype> HydrogenReagent = "Hydrogen";
    private static readonly ProtoId<KsFluidReactionPrototype> DecokingReaction = "KsDecoking";
    private static readonly ProtoId<KsFluidReactionPrototype> ThermalCrackingReaction = "KsThermalCrackingGasOil";

    private static KsFluidAmount Amount(GasMixture mixture, ProtoId<ReagentPrototype> reagent)
    {
        mixture.Fluid!.TryGetAmount(new KsFluidReagentKey(reagent, null), out var amount);
        return amount;
    }

    private static void Update(IEntityManager entityManager, EntityUid deviceUid, float deltaTime)
    {
        var ev = new AtmosDeviceUpdateEvent(deltaTime, null, null);
        entityManager.EventBus.RaiseLocalEvent(deviceUid, ref ev);
    }

    private async Task<EntityUid> SpawnReactor(EntProtoId bedPrototype)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid reactorUid = default;
        await server.WaitPost(() =>
        {
            reactorUid = server.EntMan.SpawnEntity(ReactorPrototype, testMap.GridCoords);
            var bedUid = server.EntMan.SpawnEntity(bedPrototype, testMap.GridCoords);
            var reactor = server.EntMan.GetComponent<KsCatalyticReactorComponent>(reactorUid);
            Assert.That(server.System<ItemSlotsSystem>().TryInsert(reactorUid, reactor.CatalystSlotId, bedUid, user: null));
        });
        await server.WaitRunTicks(5);
        return reactorUid;
    }

    private GasMixture ReactorAir(EntityUid reactorUid, float temperature)
    {
        Assert.That(Pair.Server.System<NodeContainerSystem>().TryGetNode(reactorUid, "pipe", out PipeNode? node));
        var mixture = node!.Air;
        mixture.Clear();
        mixture.Temperature = temperature;
        mixture.Fluid = new KsFluidMixture();
        return mixture;
    }

    [Test]
    public async Task EveryReactionIsWellFormed()
    {
        var server = Pair.Server;
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var reaction in server.ProtoMan.EnumeratePrototypes<KsFluidReactionPrototype>())
                {
                    var reactantUnits = 0f;
                    var anyUnlinked = false;
                    foreach (var (reagent, reactant) in reaction.Reactants)
                    {
                        Assert.That(propertiesSystem.TryGetProperties(reagent, out var properties), $"{reaction.ID} reacts {reagent}, which has no properties.");
                        reactantUnits += reactant.Amount;
                        anyUnlinked |= !properties!.Linked;
                    }

                    var productUnits = 0f;
                    foreach (var (reagent, product) in reaction.Products)
                    {
                        Assert.That(propertiesSystem.TryGetProperties(reagent, out var properties));
                        productUnits += product.Amount;
                        if (product.Phase == KsFluidReactionPhase.Vapor)
                            Assert.That(properties!.Volatile, $"{reaction.ID} makes {reagent} as vapor, which never boils.");
                    }

                    Assert.That(anyUnlinked, $"{reaction.ID} has only linked reactants, so it can never be a candidate.");
                    Assert.That(float.IsFinite(reaction.Enthalpy));

                    // Carbon dioxide's units are scaled, so decoking balances by moles instead.
                    if (reaction.ID != DecokingReaction)
                        Assert.That(productUnits, Is.EqualTo(reactantUnits).Within(0.01f * reactantUnits), $"{reaction.ID} doesn't balance by mass.");
                }

                // Beakers must not be a way round the refinery.
                foreach (var reaction in server.ProtoMan.EnumeratePrototypes<ReactionPrototype>())
                {
                    foreach (var reactant in reaction.Reactants.Keys)
                    {
                        Assert.That(reactant, Is.Not.EqualTo(NaphthaReagent.Id).And.Not.EqualTo(GasOilReagent.Id).And.Not.EqualTo(KeroseneReagent.Id),
                            $"{reaction.ID} turns a refinery intermediate into something in a beaker.");
                    }
                }
            }
        });
    }

    [Test]
    public async Task ResidueCokesAtOneAtmosphereButBoilsBelowThatUnderVacuum()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(GasOilReagent, out var gasOil));
            var reaction = server.ProtoMan.Index(ThermalCrackingReaction);

            var atmospheric = KsPhaseMath.BoilingPointAt(gasOil!.VaporizationEntropy, gasOil.BoilingPoint, Atmospherics.OneAtmosphere);
            var vacuum = KsPhaseMath.BoilingPointAt(gasOil.VaporizationEntropy, gasOil.BoilingPoint, 10f);

            const float temperature = 720f;
            var mixture = new GasMixture(200f) { Temperature = temperature, FluidCapable = true, Fluid = new KsFluidMixture() };
            mixture.AdjustMoles(Gas.Nitrogen, 500f * mixture.Volume / (Atmospherics.R * temperature));
            mixture.Fluid.AddLiquid(new KsFluidReagentKey(GasOilReagent, null), 200f);

            for (var i = 0; i < 20; i++)
            {
                mixture.Temperature = temperature;
                mixture.Fluid!.LastPhaseTime = gameTiming.CurTime - TimeSpan.FromSeconds(0.5f);
                if (mixture.Fluid.LastReactionTime != null)
                    mixture.Fluid.LastReactionTime = gameTiming.CurTime - TimeSpan.FromSeconds(0.5f);
                phaseSystem.UpdatePhases(mixture, holder: null);
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(atmospheric, Is.GreaterThan(reaction.MinTemp), "Gas oil boils at one atmosphere before it cracks, so vacuum isn't needed.");
                Assert.That(vacuum, Is.LessThan(reaction.MinTemp), "Gas oil doesn't boil under vacuum before it cracks.");
                Assert.That(Amount(mixture, CokeReagent).Solid, Is.GreaterThan(0f), "Overheated gas oil didn't coke.");
            }
        });
    }

    [Test]
    public async Task SulfurPoisonsReforming()
    {
        var server = Pair.Server;
        var cleanUid = await SpawnReactor(PlatinumBedPrototype);
        var dirtyUid = await SpawnReactor(PlatinumBedPrototype);
        var itemSlotsSystem = server.System<ItemSlotsSystem>();

        await server.WaitAssertion(() =>
        {
            const float temperature = 770f;
            var clean = ReactorAir(cleanUid, temperature);
            clean.Fluid!.AddLiquid(new KsFluidReagentKey(NaphthaReagent, null), 1000f);

            var dirty = ReactorAir(dirtyUid, temperature);
            dirty.Fluid!.AddLiquid(new KsFluidReagentKey(NaphthaReagent, null), 1000f);
            dirty.Fluid.AddLiquid(new KsFluidReagentKey(OrganosulfurReagent, null), 200f);

            for (var i = 0; i < 40; i++)
            {
                // Reheated each step, as a real train of reactors is between beds.
                clean.Temperature = temperature;
                dirty.Temperature = temperature;
                Update(server.EntMan, cleanUid, 0.5f);
                Update(server.EntMan, dirtyUid, 0.5f);
            }

            var reactor = server.EntMan.GetComponent<KsCatalyticReactorComponent>(dirtyUid);
            var bedUid = itemSlotsSystem.GetItemOrNull(dirtyUid, reactor.CatalystSlotId);
            var activity = server.EntMan.GetComponent<KsFluidCatalystComponent>(bedUid!.Value).Activity;

            using (Assert.EnterMultipleScope())
            {
                var cleanGasoline = Amount(clean, GasolineReagent);
                var dirtyGasoline = Amount(dirty, GasolineReagent);
                Assert.That(cleanGasoline.Liquid + cleanGasoline.Vapor, Is.GreaterThan(0f), "Platinum didn't reform clean naphtha.");
                Assert.That(Amount(clean, HydrogenReagent).Vapor, Is.GreaterThan(0f), "Reforming made no hydrogen.");
                Assert.That(activity, Is.Zero, "Sulfur didn't kill the platinum.");
                Assert.That(dirtyGasoline.Liquid + dirtyGasoline.Vapor, Is.LessThan(cleanGasoline.Liquid + cleanGasoline.Vapor),
                    "Unhydrotreated feed reformed as well as clean feed.");
            }
        });
    }

    [Test]
    public async Task HydrotreatingNeedsHydrogenUnderPressure()
    {
        var server = Pair.Server;
        var dryUid = await SpawnReactor(CobaltMolybdenumBedPrototype);
        var fedUid = await SpawnReactor(CobaltMolybdenumBedPrototype);
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(HydrogenReagent, out var hydrogen));
            const float temperature = 640f;

            // Hydrogen, but under the reaction's 300 kPa.
            var dry = ReactorAir(dryUid, temperature);
            dry.Fluid!.AddLiquid(new KsFluidReagentKey(OrganosulfurReagent, null), 50f);
            var lowHydrogenMoles = 100f * dry.Volume / (Atmospherics.R * temperature);
            dry.Fluid.SetAmount(new KsFluidReagentKey(HydrogenReagent, null), new KsFluidAmount(0f, 0f, lowHydrogenMoles));

            var fed = ReactorAir(fedUid, temperature);
            fed.Fluid!.AddLiquid(new KsFluidReagentKey(OrganosulfurReagent, null), 50f);
            var hydrogenMoles = 1000f * fed.Volume / (Atmospherics.R * temperature);
            fed.Fluid.SetAmount(new KsFluidReagentKey(HydrogenReagent, null), new KsFluidAmount(0f, 0f, hydrogenMoles));

            for (var i = 0; i < 10; i++)
            {
                Update(server.EntMan, dryUid, 0.5f);
                Update(server.EntMan, fedUid, 0.5f);
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(hydrogen!.MolesPerUnit, Is.EqualTo(0.5f).Within(0.001f), "Hydrogen isn't its real molar mass.");
                Assert.That(Amount(dry, SulfideReagent).Vapor, Is.Zero, "Hydrotreating ran under its minimum hydrogen pressure.");
                Assert.That(Amount(fed, SulfideReagent).Vapor, Is.GreaterThan(0f), "Hydrotreating made no hydrogen sulfide.");
            }
        });
    }
}
