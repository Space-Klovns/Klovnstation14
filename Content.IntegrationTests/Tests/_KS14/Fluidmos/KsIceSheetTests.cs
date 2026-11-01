#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos.Ice;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Fluidmos;
using Content.Shared._KS14.Fluidmos.Ice;
using Content.Shared._KS14.TileEffects;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     Frozen reagents on tiles: puddles freeze at a plateau into one ice sheet per tile, keep their data, melt back
///         into the puddle, sublime in thin air, spill when the sheet breaks, and are only slippery past frost.
/// </summary>
[TestOf(typeof(KsIceSheetSystem))]
public sealed class KsIceSheetTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> BloodReagent = "Blood";
    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";

    private const float ColdTemperature = 253.15f;

    // Deliberately NOT static: it only exists in this test's YAML, and the YAML linter validates static ids.
    private readonly ProtoId<ReagentPrototype> _inertReagent = "KsTestIceSheetInert";

    // No boiling or melting point, so nothing but heat exchange changes its temperature.
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestIceSheetInert
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
";

    private async Task<TestMapData> CreateColdTestMap()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = ColdTemperature };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);

            server.EntMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
        });

        await server.WaitRunTicks(30);
        return testMap;
    }

    private GasMixture TileAir(TestMapData testMap)
    {
        return Pair.Server.System<AtmosphereSystem>().GetTileMixture(testMap.Grid.Owner, null, testMap.Tile.GridIndices, excite: true)!;
    }

    /// <summary>
    ///     Saturates the tile's air with water vapor, so nothing evaporates and only freezing moves heat.
    /// </summary>
    private void Saturate(TestMapData testMap, float temperature)
    {
        Assert.That(Pair.Server.System<KsFluidmosPropertiesSystem>().TryGetProperties(WaterReagent, out var water));
        var air = TileAir(testMap);
        var saturationPressure = KsPhaseMath.SaturationPressure(water!.VaporizationEntropy, water.BoilingPoint, temperature);
        air.SetMoles(Gas.WaterVapor, saturationPressure * air.Volume / (Atmospherics.R * air.Temperature));
    }

    private Entity<SolutionComponent> SpillPuddle(TestMapData testMap, Solution spilled, out EntityUid puddleUid)
    {
        var entityManager = Pair.Server.EntMan;
        var temperature = spilled.Temperature;
        Assert.That(entityManager.System<PuddleSystem>().TrySpillAt(testMap.Tile, spilled, out puddleUid, sound: false, tileReact: false));

        var puddleComponent = entityManager.GetComponent<PuddleComponent>(puddleUid);
        Assert.That(entityManager.System<SharedSolutionContainerSystem>()
            .ResolveSolution(puddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution, out _));
        var solutionEntity = puddleComponent.Solution!.Value;
        solutionEntity.Comp.Solution.Temperature = temperature;
        return solutionEntity;
    }

    private Solution IceSolution(TestMapData testMap, out EntityUid sheetUid)
    {
        var server = Pair.Server;
        Assert.That(server.System<KsIceSheetSystem>().TryGetIceSheet(testMap.Grid.Owner, testMap.Tile.GridIndices, out var sheet),
            "The tile has no ice sheet.");
        sheetUid = sheet.Owner;
        Assert.That(server.System<SharedSolutionContainerSystem>().TryGetSolution(sheet.Owner, KsIceSheetComponent.SolutionName, out _, out var solution));
        return solution!;
    }

    private int CountIceSheets()
    {
        var count = 0;
        var query = Pair.Server.EntMan.AllEntityQueryEnumerator<KsIceSheetComponent>();
        while (query.MoveNext(out _, out _))
            count++;

        return count;
    }

    /// <summary>
    ///     One phase pass of the ice, with no heat exchange, so only what the test set up moves.
    /// </summary>
    private void StepIce()
    {
        Pair.Server.System<KsIceSheetSystem>().UpdateHeatExchange(1f, conductance: 0f, referenceVolume: KsFluidmos.PuddleReferenceVolume, emissivity: 0f);
    }

    [Test]
    public async Task ColdWaterFreezesAtAPlateauIntoOneSheet()
    {
        var server = Pair.Server;
        var testMap = await CreateColdTestMap();
        var startTemperature = 0f;
        var heatCapacity = 0f;
        Solution puddle = null!;
        EntityUid puddleUid = default;

        await server.WaitPost(() =>
        {
            Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(WaterReagent, out var water));
            startTemperature = water!.MeltingPoint - 5f;
            // Saturated even at the melting point the puddle warms to, so nothing evaporates in the same pass.
            Saturate(testMap, water.MeltingPoint);

            puddle = SpillPuddle(testMap, new Solution(WaterReagent, FixedPoint2.New(20)) { Temperature = startTemperature }, out puddleUid).Comp.Solution;
            heatCapacity = puddle.GetHeatCapacity(server.ProtoMan);
            server.System<KsTileEffectSystem>().TryUpdateTileEffects(puddleUid, testMap.Tile, puddle, scale: 1f);

            // Before the cold air gets at it.
            Assert.That(puddle.Temperature, Is.EqualTo(water.MeltingPoint).Within(0.05f), "The freezing puddle did not plateau at its melting point.");
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(WaterReagent, out var water));
            var ice = IceSolution(testMap, out _);
            var expectedFrozen = heatCapacity * (water!.MeltingPoint - startTemperature) / water.FusionHeat;

            using (Assert.EnterMultipleScope())
            {
                Assert.That((float)ice.Volume, Is.EqualTo(expectedFrozen).Within(0.02f), "The ice formed does not match the heat of fusion released.");
                Assert.That(puddle.Volume, Is.GreaterThan(FixedPoint2.Zero), "Water froze past what its sensible heat paid for.");
            }

            // Freezing again adds to the same sheet.
            puddle.Temperature = startTemperature;
            server.System<KsTileEffectSystem>().TryUpdateTileEffects(puddleUid, testMap.Tile, puddle, scale: 1f);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() => Assert.That(CountIceSheets(), Is.EqualTo(1), "A tile grew a second ice sheet."));
    }

    [Test]
    public async Task FrozenBloodKeepsItsDnaAndMeltsBackIntoThePuddle()
    {
        var server = Pair.Server;
        var testMap = await CreateColdTestMap();
        var dna = new List<ReagentData> { new DnaData { DNA = "TACG" } };

        await server.WaitPost(() =>
        {
            var blood = new Solution { Temperature = ColdTemperature };
            blood.AddReagent(new ReagentId(BloodReagent, dna), FixedPoint2.New(20));
            Saturate(testMap, ColdTemperature);
            var puddleSolution = SpillPuddle(testMap, blood, out var puddleUid);
            server.System<KsTileEffectSystem>().TryUpdateTileEffects(puddleUid, testMap.Tile, puddleSolution.Comp.Solution, scale: 1f);
        });
        await server.WaitRunTicks(1);

        await server.WaitPost(() =>
        {
            var ice = IceSolution(testMap, out _);
            Assert.That(ice.GetReagentQuantity(new ReagentId(BloodReagent, dna)), Is.GreaterThan(FixedPoint2.Zero), "Frozen blood lost its DNA.");

            // Warm the sheet past blood's melting point: it melts back into the tile's puddle.
            ice.Temperature = server.ProtoMan.Index(BloodReagent).MeltingPoint!.Value + 20f;
            // With the old puddle gone, any blood on the tile afterwards came out of the ice.
            Assert.That(server.System<PuddleSystem>().TryGetCachedPuddle(testMap.Grid.Owner, testMap.Tile.GridIndices, out var puddle));
            server.EntMan.DeleteEntity(puddle);
            StepIce();
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<PuddleSystem>().TryGetCachedPuddle(testMap.Grid.Owner, testMap.Tile.GridIndices, out var puddle),
                "Melting ice left no puddle.");
            Assert.That(puddle.Comp.Solution is { } solution &&
                        solution.Comp.Solution.GetReagentQuantity(new ReagentId(BloodReagent, dna)) > FixedPoint2.Zero,
                "The melted blood lost its DNA, or did not reach the puddle.");
        });
    }

    /// <summary>
    ///     A thin sheet between colder air and a colder puddle cools to their temperature and no further. Both exchanges
    ///         start from the sheet's temperature and are applied together, so if each could take it all the way, together
    ///         they would carry it below both.
    /// </summary>
    [Test]
    public async Task ThinIceBetweenColdAirAndAColdPuddleStopsAtTheirTemperature()
    {
        const float surroundingTemperature = 150f;

        var server = Pair.Server;
        var testMap = await CreateColdTestMap();

        await server.WaitPost(() =>
        {
            var air = TileAir(testMap);
            air.Temperature = surroundingTemperature;
            SpillPuddle(testMap, new Solution(_inertReagent, FixedPoint2.New(20)) { Temperature = surroundingTemperature }, out _);
            server.System<KsIceSheetSystem>().AddIce(testMap.GridCoords, new Solution(WaterReagent, FixedPoint2.New(0.2f)) { Temperature = ColdTemperature });
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            // Conductive enough that either contact alone would bring the sheet all the way within the step.
            server.System<KsIceSheetSystem>().UpdateHeatExchange(1f, conductance: 1000f, referenceVolume: KsFluidmos.PuddleReferenceVolume, emissivity: 0f);
            var temperature = IceSolution(testMap, out _).Temperature;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(temperature, Is.GreaterThanOrEqualTo(surroundingTemperature - 0.5f),
                    $"The sheet fell to {temperature} K, below the {surroundingTemperature} K of the air and puddle it touches.");
                Assert.That(temperature, Is.LessThan(surroundingTemperature + 20f), "The sheet didn't cool, so this proves nothing.");
            }
        });
    }

    [Test]
    public async Task IceInVacuumSublimes()
    {
        var server = Pair.Server;
        var testMap = await CreateColdTestMap();

        await server.WaitPost(() =>
        {
            TileAir(testMap).Multiply(0.001f);
            server.System<KsIceSheetSystem>().AddIce(testMap.GridCoords, new Solution(WaterReagent, FixedPoint2.New(10)) { Temperature = ColdTemperature });
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var air = TileAir(testMap);
            var vaporBefore = air.GetMoles(Gas.WaterVapor);
            var iceBefore = IceSolution(testMap, out _).Volume;

            StepIce();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(air.GetMoles(Gas.WaterVapor), Is.GreaterThan(vaporBefore), "Ice in vacuum put no water vapor into the air.");
                Assert.That(IceSolution(testMap, out _).Volume, Is.LessThan(iceBefore), "Ice in vacuum did not sublime.");
            }
        });
    }

    [Test]
    public async Task BrokenSheetSpillsAPuddleAndOnlyThickIceIsSlippery()
    {
        var server = Pair.Server;
        var testMap = await CreateColdTestMap();
        var iceSheetSystem = server.System<KsIceSheetSystem>();

        await server.WaitPost(() =>
            iceSheetSystem.AddIce(testMap.GridCoords, new Solution(WaterReagent, FixedPoint2.New(KsIceSheetSystem.SlipThresholdUnits / 2f)) { Temperature = ColdTemperature }));
        await server.WaitRunTicks(1);

        EntityUid sheetUid = default;
        await server.WaitPost(() =>
        {
            IceSolution(testMap, out sheetUid);
            Assert.That(server.EntMan.GetComponent<PhysicsComponent>(sheetUid).CanCollide, Is.False, "Frost alone is slippery.");
            iceSheetSystem.AddIce(testMap.GridCoords, new Solution(WaterReagent, FixedPoint2.New(KsIceSheetSystem.SlipThresholdUnits * 2f)) { Temperature = ColdTemperature });
        });
        await server.WaitRunTicks(1);

        await server.WaitPost(() =>
        {
            Assert.That(server.EntMan.GetComponent<PhysicsComponent>(sheetUid).CanCollide, Is.True, "A thick sheet of ice is not slippery.");

            var damage = new DamageSpecifier(server.ProtoMan.Index(BluntDamage), FixedPoint2.New(1000));
            server.System<DamageableSystem>().TryChangeDamage(sheetUid, damage, ignoreResistances: true);
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(sheetUid) || server.EntMan.IsQueuedForDeletion(sheetUid), "The sheet did not break.");
            Assert.That(server.System<PuddleSystem>().TryGetCachedPuddle(testMap.Grid.Owner, testMap.Tile.GridIndices, out _),
                "A broken sheet spilled no puddle.");
        });
    }
}
