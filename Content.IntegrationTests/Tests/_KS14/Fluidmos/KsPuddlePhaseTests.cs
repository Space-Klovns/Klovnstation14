#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Fluidmos;
using Content.Shared._KS14.TileEffects;
using Content.Shared._KS14.TileEffects.Effects;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     A tile's fluid is its puddle: puddles boil at the boiling point for the tile's pressure, boil into smoke when they
///         have no gas, and evaporate into the tile as humidity at the calibrated Hertz–Knudsen rate.
/// </summary>
[TestOf(typeof(BoilTileEffect))]
public sealed class KsPuddlePhaseTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> EthanolReagent = "Ethanol";
    private static readonly ProtoId<ReagentPrototype> AmmoniaReagent = "Ammonia";

    private async Task<TestMapData> CreateBreathableTestMap()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);

            server.EntMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
        });

        await server.WaitRunTicks(30);
        return testMap;
    }

    private Entity<SolutionComponent> SpillPuddle(TestMapData testMap, ProtoId<ReagentPrototype> reagent, float volume, float temperature, out EntityUid puddleUid)
    {
        var entityManager = Pair.Server.EntMan;
        var spilled = new Solution(reagent.Id, FixedPoint2.New(volume)) { Temperature = temperature };
        Assert.That(entityManager.System<PuddleSystem>().TrySpillAt(testMap.Tile, spilled, out puddleUid, sound: false, tileReact: false),
            "Puddle failed to spill.");

        var puddleComponent = entityManager.GetComponent<PuddleComponent>(puddleUid);
        Assert.That(entityManager.System<SharedSolutionContainerSystem>()
            .ResolveSolution(puddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution, out _));
        var solutionEntity = puddleComponent.Solution!.Value;
        solutionEntity.Comp.Solution.Temperature = temperature;
        return solutionEntity;
    }

    private GasMixture TileAir(TestMapData testMap)
    {
        return Pair.Server.System<AtmosphereSystem>().GetTileMixture(testMap.Grid.Owner, null, testMap.Tile.GridIndices, excite: true)!;
    }

    [Test]
    public async Task PuddleOnALowPressureTileBoilsAtALowerTemperature()
    {
        var server = Pair.Server;
        var testMap = await CreateBreathableTestMap();

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(WaterReagent, out var water));
            var air = TileAir(testMap);
            air.Multiply(0.1f);

            var localBoilingPoint = KsPhaseMath.BoilingPointAt(water!.VaporizationEntropy, water.BoilingPoint, air.Pressure);
            var startTemperature = (localBoilingPoint + water.BoilingPoint) / 2f;
            var vaporBefore = air.GetMoles(Gas.WaterVapor);

            var solutionEntity = SpillPuddle(testMap, WaterReagent, 50f, startTemperature, out var puddleUid);
            var solution = solutionEntity.Comp.Solution;
            server.System<KsTileEffectSystem>().TryUpdateTileEffects(puddleUid, testMap.Tile, solution, scale: 1f);

            var boiled = 50f - (float)solution.Volume;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(boiled, Is.GreaterThan(0f), "Water below its one-atmosphere boiling point did not boil at low pressure.");
                Assert.That(solution.Temperature, Is.EqualTo(localBoilingPoint).Within(0.5f),
                    "The puddle did not boil down to the boiling point for the tile's pressure.");
                Assert.That(air.GetMoles(Gas.WaterVapor) - vaporBefore, Is.EqualTo(boiled * water.MolesPerUnit).Within(0.001f),
                    "The boiled water did not reach the air as its moles of vapor.");
            }
        });
    }

    [Test]
    public async Task BoilingUnlinkedReagentMakesSmoke()
    {
        var server = Pair.Server;
        var testMap = await CreateBreathableTestMap();

        await server.WaitAssertion(() =>
        {
            var ethanol = server.ProtoMan.Index(EthanolReagent);
            var solutionEntity = SpillPuddle(testMap, EthanolReagent, 30f, ethanol.BoilingPoint!.Value + 30f, out var puddleUid);
            server.System<KsTileEffectSystem>().TryUpdateTileEffects(puddleUid, testMap.Tile, solutionEntity.Comp.Solution, scale: 1f);
            Assert.That(solutionEntity.Comp.Solution.Volume, Is.LessThan(FixedPoint2.New(30)), "Hot ethanol did not boil.");
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var solutionContainerSystem = server.System<SharedSolutionContainerSystem>();
            var found = false;
            var query = server.EntMan.AllEntityQueryEnumerator<SmokeComponent>();
            while (query.MoveNext(out var smokeUid, out _))
            {
                found |= solutionContainerSystem.TryGetSolution(smokeUid, SmokeComponent.SolutionName, out _, out var smoke) &&
                         smoke.GetTotalPrototypeQuantity(EthanolReagent) > FixedPoint2.Zero;
            }

            Assert.That(found, "Boiled ethanol made no smoke.");
        });
    }

    [Test]
    public async Task WaterEvaporatesAtUpstreamsRateOnADryTileAndNotIntoASaturatedOne()
    {
        var server = Pair.Server;
        var testMap = await CreateBreathableTestMap();

        await server.WaitAssertion(() =>
        {
            // Upstream's rate for a puddle of only water: EvaporationAmount times water's evaporation speed, per second.
            var upstreamRate = (float)(new EvaporationComponent().EvaporationAmount * server.ProtoMan.Index(WaterReagent).EvaporationSpeed);

            var air = TileAir(testMap);
            air.SetMoles(Gas.WaterVapor, 0f);

            // A tile-covering puddle, at the air's temperature.
            var solutionEntity = SpillPuddle(testMap, WaterReagent, KsFluidmos.PuddleReferenceVolume * 2f, air.Temperature, out var puddleUid);
            var solution = solutionEntity.Comp.Solution;
            var volumeBefore = (float)solution.Volume;
            server.System<KsTileEffectSystem>().TryUpdateTileEffects(puddleUid, testMap.Tile, solution, scale: 1f);
            var dryRate = volumeBefore - (float)solution.Volume;

            Assert.That(dryRate, Is.EqualTo(upstreamRate).Within(10f).Percent,
                "A water puddle on a dry room-temperature tile does not dry at upstream's rate.");

            // Now saturate the air: it barely evaporates.
            Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(WaterReagent, out var water));
            var saturationPressure = KsPhaseMath.SaturationPressure(water!.VaporizationEntropy, water.BoilingPoint, solution.Temperature);
            air.SetMoles(Gas.WaterVapor, saturationPressure * air.Volume / (Atmospherics.R * air.Temperature));

            volumeBefore = (float)solution.Volume;
            server.System<KsTileEffectSystem>().TryUpdateTileEffects(puddleUid, testMap.Tile, solution, scale: 1f);
            Assert.That(volumeBefore - (float)solution.Volume, Is.LessThan(dryRate * 0.05f), "A puddle in saturated air kept drying.");
        });
    }

    /// <summary>
    ///     A small puddle of one volatile reagent, given a long step, evaporates only as far as its own heat pays for: it
    ///         cools to the air's dew point and stops, rather than losing nearly all of itself and with it the heat
    ///         capacity its new temperature is divided by.
    /// </summary>
    [Test]
    public async Task EvaporationStopsAtTheDewPointHoweverLongTheStep()
    {
        var server = Pair.Server;
        var testMap = await CreateBreathableTestMap();

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(AmmoniaReagent, out var ammonia));
            // Just under its boiling point, so it evaporates rather than boils, and fast.
            var solutionEntity = SpillPuddle(testMap, AmmoniaReagent, 1f, ammonia!.BoilingPoint - 5f, out var puddleUid);
            var solution = solutionEntity.Comp.Solution;
            var air = TileAir(testMap);
            var partialPressure = air.GetMoles(Gas.Ammonia) * Atmospherics.R * air.Temperature / air.Volume;
            var dewPoint = KsPhaseMath.BoilingPointAt(ammonia.VaporizationEntropy, ammonia.BoilingPoint, partialPressure);

            server.System<KsTileEffectSystem>().TryUpdateTileEffects(puddleUid, testMap.Tile, solution, scale: 10f);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(float.IsFinite(solution.Temperature), $"The puddle's temperature became {solution.Temperature}.");
                Assert.That(solution.Temperature, Is.GreaterThanOrEqualTo(dewPoint - 0.5f),
                    $"The puddle evaporated itself down to {solution.Temperature} K, below the {dewPoint} K dew point of the air.");
                Assert.That(air.GetMoles(Gas.Ammonia), Is.GreaterThan(0f), "Nothing evaporated, so this proves nothing.");
            }
        });
    }

    [Test]
    public async Task LinkedReagentsBoilAndEvaporateAsFluidmosSays()
    {
        var server = Pair.Server;
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var reagentPrototype in server.ProtoMan.EnumeratePrototypes<ReagentPrototype>())
                {
                    if (!propertiesSystem.TryGetProperties(reagentPrototype.ID, out var properties) || !properties.Linked)
                        continue;

                    var boils = reagentPrototype.KsTileEffects.OfType<BoilTileEffect>().ToList();

                    // Upstream no longer dries a linked reagent, so something else must.
                    if (reagentPrototype.EvaporationSpeed > FixedPoint2.Zero)
                    {
                        Assert.That(boils.Any(boil => boil.Evaporate), Is.True,
                            $"{reagentPrototype.ID} is linked to a gas but has no evaporating boil effect, so its puddles never dry.");
                    }

                    // Otherwise cycling it between a puddle and a pipe makes or destroys matter or energy.
                    foreach (var boil in boils)
                    {
                        if (boil.Gas is { } gas)
                            Assert.That((int)gas, Is.EqualTo(properties.GasIndex), $"{reagentPrototype.ID} boils into a different gas than its Fluidmos link.");

                        if (boil.LatentHeat is { } latentHeat)
                            Assert.That(latentHeat, Is.EqualTo(properties.LatentHeat), $"{reagentPrototype.ID}'s boil effect has its own latent heat.");

                        if (boil.MolesPerUnit is { } molesPerUnit)
                            Assert.That(molesPerUnit, Is.EqualTo(properties.MolesPerUnit), $"{reagentPrototype.ID}'s boil effect has its own moles per unit.");
                    }
                }
            }
        });
    }
}
