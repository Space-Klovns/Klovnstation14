#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Client._KS14.PredictedSpawning;
using Content.Server._KS14.Atmos.FoamBlanket;
using Content.Server._KS14.Atmos.TileFire;
using ClientBlanketSystem = Content.Client._KS14.Atmos.FoamBlanket.KsFoamBlanketSystem;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chemistry.TileReactions;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared._KS14.Atmos.FoamBlanket;
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
using Content.Shared.Gravity;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     Fighting a hot fuel puddle: water boiling off a puddle, spray spilling into one, and foam blankets.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFoamBlanketSystem))]
[TestOf(typeof(BoilTileEffect))]
[TestOf(typeof(SpillIfPuddlePresentTileReaction))]
public sealed class KsFirefightingTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseChemicalFire
  id: KsTestFirefightingUnextinguishableFire
  components:
  - type: ChemicalFire
    color: '#FF9030'
    duration: 30
    heatPower: 0
    extinguishable: false
    foamSmotherable: false

- type: reagent
  id: KsTestFirefightingInertLiquid
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent

- type: entity
  id: KsTestFirefightingFlammable
  name: test flammable
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.25
        hard: false
        layer:
        - MidImpassable
        mask:
        - MidImpassable
  - type: Appearance
  - type: Flammable
    fireSpread: false
    canResistFire: false
    damage:
      types:
        Heat: 1

- type: entity
  parent: KsFoamBlanket
  id: KsTestFirefightingSlowBlanket
  components:
  - type: KsFoamBlanket
    dissolveDuration: 30

- type: entity
  id: KsTestFirefightingMob
  name: test mob
  components:
  - type: Physics
    bodyType: KinematicController
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.25
        hard: true
        layer:
        - MobLayer
        mask:
        - MobMask
  - type: Appearance
  - type: Flammable
    fireSpread: false
    canResistFire: false
    damage:
      types:
        Heat: 1

# The vapor spray the extinguisher used to have, which firebots still use.
- type: entity
  parent: SolutionSpray
  id: KsTestFirefightingSprayer
  components:
  - type: Solution
    solution:
      maxVol: 100
      reagents:
      - ReagentId: FirefightingFoam
        Quantity: 100
  - type: Spray
    transferAmount: 10
    spraySound:
      path: /Audio/Effects/extinguish.ogg
    sprayedPrototype: ExtinguisherSpray
    vaporAmount: 3
    vaporSpread: 90
    sprayVelocity: 2.0
";

    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> WeldingFuelReagent = "WeldingFuel";
    private static readonly ProtoId<ReagentPrototype> FirefightingFoamReagent = "FirefightingFoam";
    private static readonly EntProtoId BlanketPrototype = "KsFoamBlanket";
    private static readonly EntProtoId ChemicalFirePrototype = "ChemicalFire";
    private static readonly EntProtoId ExtinguisherPrototype = "FireExtinguisher";
    private static readonly EntProtoId KlovnideFirePrototype = "ChemicalFireSuspendedKlovnideTrioxide";
    private static readonly ProtoId<ReagentPrototype> KlovnideReagent = "SuspendedKlovnideTrioxide";

    private const string ExtinguisherSolution = "spray";
    private const int RowLength = 8;

    // Deliberately NOT static: these only exist in the test YAML above, and the YAML linter validates static ids.
    private readonly EntProtoId _unextinguishableFirePrototype = "KsTestFirefightingUnextinguishableFire";
    private readonly ProtoId<ReagentPrototype> _inertLiquidReagent = "KsTestFirefightingInertLiquid";
    private readonly EntProtoId _flammablePrototype = "KsTestFirefightingFlammable";
    private readonly EntProtoId _mobPrototype = "KsTestFirefightingMob";
    private readonly EntProtoId _extinguisherShotPrototype = "KsBulletExtinguisherShot";
    private readonly EntProtoId _slowBlanketPrototype = "KsTestFirefightingSlowBlanket";
    private readonly EntProtoId _sprayerPrototype = "KsTestFirefightingSprayer";

    private const float HotPuddleTemperature = 450f;

    /// <summary>
    ///     Hot enough that a 50u fuel puddle is still past its ignition point after a whole second of losing heat to the
    ///         air, by conduction and radiation, before its first tile effect check: at
    ///         <see cref="HotPuddleTemperature"/> it falls below that point in its first air exchange, and whether it
    ///         lights depends on which of the two runs first.
    /// </summary>
    private const float IgnitingPuddleTemperature = 700f;
    private const int TicksPerSecond = 30;
    private const int BoilTestSeconds = 4; // short enough that a blanket over a hot puddle is not boiled away

    private async Task<TestMapData> CreateBreathableTestMap()
    {
        var pair = Pair;
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

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
                .ResolveSolution(puddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution, out var solution),
            "Puddle has no solution.");
        // Water's own boiling already runs once on the spill, so this is only checked for what does not boil.
        if (solution!.Temperature < temperature - 0.01f && Pair.Server.ProtoMan.Index(reagent).KsTileEffects.Length == 0)
            Assert.Fail("Puddle did not keep its temperature.");

        return puddleComponent.Solution!.Value;
    }

    /// <summary>
    ///     Lays a blanket and tops it up until it is full, as spraying a tile for a while would.
    /// </summary>
    private void FillBlanket(TileRef tileRef)
    {
        var blanketSystem = Pair.Server.EntMan.System<KsFoamBlanketSystem>();

        for (var i = 0; i < 1000; i++)
        {
            if (blanketSystem.Deploy(BlanketPrototype, tileRef, 1f) <= 0f)
                return;
        }

        Assert.Fail("The blanket never filled up.");
    }

    /// <summary>
    ///     Floors a row of tiles east of the test map's one tile, for something to be sprayed along.
    /// </summary>
    private void FloorRow(TestMapData testMap)
    {
        var mapSystem = Pair.Server.EntMan.System<SharedMapSystem>();

        for (var x = 1; x < RowLength; x++)
            mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), testMap.Tile.Tile);
    }

    private int CountBlanketedTiles(TestMapData testMap)
    {
        var blanketSystem = Pair.Server.EntMan.System<KsFoamBlanketSystem>();
        var count = 0;

        for (var x = 1; x < RowLength; x++)
        {
            if (blanketSystem.TryGetBlanket(testMap.Grid.Owner, new Vector2i(x, 0), out _))
                count++;
        }

        return count;
    }

    private FixedPoint2 SprayVolume(EntityUid extinguisherUid)
    {
        Assert.That(Pair.Server.EntMan.System<SharedSolutionContainerSystem>()
            .TryGetSolution(extinguisherUid, ExtinguisherSolution, out _, out var solution), "Extinguisher has no solution.");
        return solution!.Volume;
    }

    [Test]
    public async Task TestWaterBoilsOffHotPuddleConservingEnergy()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var atmosphereSystem = entityManager.System<AtmosphereSystem>();
        var protoMan = SProtoMan;

        var testMap = await CreateBreathableTestMap();
        var gridUid = testMap.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            var solutionEntity = SpillPuddle(testMap, WaterReagent, 50f, HotPuddleTemperature, out var puddleUid);
            var solution = solutionEntity.Comp.Solution;
            // Spilling already boiled it once; heat it back up so the effect under test does the boiling.
            solution.Temperature = HotPuddleTemperature;
            var air = atmosphereSystem.GetTileMixture(gridUid, null, testMap.Tile.GridIndices, excite: true)!;

            // Water's boil effect takes its latent heat from Fluidmos, so pipes and puddles agree, and boils at the
            //     boiling point for the tile's pressure.
            Assert.That(entityManager.System<KsFluidmosPropertiesSystem>().TryGetProperties(WaterReagent, out var waterProperties));
            var boilingPoint = KsPhaseMath.BoilingPointAt(waterProperties!.VaporizationEntropy, waterProperties.BoilingPoint, air.Pressure);
            var latentHeat = protoMan.Index(WaterReagent).KsTileEffects.OfType<BoilTileEffect>().Single().LatentHeat
                ?? waterProperties!.LatentHeat;

            var volumeBefore = (float)solution.Volume;
            var energyBefore = solution.GetHeatCapacity(protoMan) * solution.Temperature
                + atmosphereSystem.GetHeatCapacity(air, applyScaling: true) * air.Temperature;
            var vaporBefore = air.GetMoles(Gas.WaterVapor);

            entityManager.System<KsTileEffectSystem>().TryUpdateTileEffects(puddleUid, testMap.Tile, solution, scale: 1f);

            var boiled = volumeBefore - (float)solution.Volume;
            var energyAfter = solution.GetHeatCapacity(protoMan) * solution.Temperature
                + atmosphereSystem.GetHeatCapacity(air, applyScaling: true) * air.Temperature
                + boiled * latentHeat;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(boiled, Is.GreaterThan(0f), "No water boiled off a puddle above its boiling point.");
                Assert.That(air.GetMoles(Gas.WaterVapor), Is.GreaterThan(vaporBefore), "No vapor reached the air.");
                Assert.That(solution.Temperature, Is.LessThan(HotPuddleTemperature), "The puddle was not cooled.");
                Assert.That(solution.Temperature, Is.GreaterThanOrEqualTo(boilingPoint - 0.5f), "The puddle was cooled past its boiling point.");
                Assert.That(energyAfter, Is.EqualTo(energyBefore).Within(energyBefore * 1e-4f), "Thermal energy was not conserved.");
            }
        });
    }

    [Test]
    public async Task TestColdWaterPuddleDoesNotBoil()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var testMap = await CreateBreathableTestMap();

        await server.WaitAssertion(() =>
        {
            var solutionEntity = SpillPuddle(testMap, WaterReagent, 50f, Atmospherics.T20C, out var puddleUid);
            var solution = solutionEntity.Comp.Solution;

            // Saturate the air, so the puddle has nothing to evaporate into either: below its boiling point, nothing leaves.
            Assert.That(entityManager.System<KsFluidmosPropertiesSystem>().TryGetProperties(WaterReagent, out var water));
            var air = entityManager.System<AtmosphereSystem>().GetTileMixture(testMap.Grid.Owner, null, testMap.Tile.GridIndices, excite: true)!;
            var saturationPressure = KsPhaseMath.SaturationPressure(water!.VaporizationEntropy, water.BoilingPoint, solution.Temperature);
            air.SetMoles(Gas.WaterVapor, saturationPressure * air.Volume / (Atmospherics.R * air.Temperature));

            entityManager.System<KsTileEffectSystem>().TryUpdateTileEffects(puddleUid, testMap.Tile, solution, scale: 1f);

            Assert.That(solution.Volume, Is.EqualTo(FixedPoint2.New(50)), "Water below its boiling point boiled.");
        });
    }

    /// <summary>
    ///     Vapor and smoke equalise with a puddle and then open a scope at their (now equal) temperature before reacting
    ///         with the tile, so the spill must not change the puddle's temperature.
    /// </summary>
    [Test]
    public async Task TestSpillIntoPuddleKeepsItsTemperature()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var protoMan = SProtoMan;

        var testMap = await CreateBreathableTestMap();

        await server.WaitAssertion(() =>
        {
            var solutionEntity = SpillPuddle(testMap, _inertLiquidReagent, 50f, HotPuddleTemperature, out _);

            using var temperatureScope = entityManager.System<PuddleSystem>().KsBeginTileReactionTemperature(HotPuddleTemperature);
            var used = new SpillIfPuddlePresentTileReaction().TileReact(testMap.Tile, protoMan.Index(WaterReagent), FixedPoint2.New(3), entityManager, null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(used, Is.EqualTo(FixedPoint2.New(3)), "Nothing was spilled.");
                Assert.That(solutionEntity.Comp.Solution.Temperature, Is.EqualTo(HotPuddleTemperature).Within(0.01f),
                    "The spill changed the puddle's temperature.");
            }
        });
    }

    /// <summary>
    ///     An ordinary spill (not vapor or smoke) onto a puddle re-spills the water through its tile reaction. The water
    ///         brings its own heat, so the puddle must end at the heat-capacity-weighted mean, whatever either started at.
    /// </summary>
    [Test]
    public async Task TestSpillOntoWarmPuddleConservesEnergy()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var protoMan = SProtoMan;

        const float puddleTemperature = 340f; // below water's boiling point, so no boil effect muddies the energy
        const float spilledTemperature = 310f; // neither the puddle's temperature nor the 293.15 K a new solution defaults to

        var testMap = await CreateBreathableTestMap();

        await server.WaitAssertion(() =>
        {
            var solutionEntity = SpillPuddle(testMap, _inertLiquidReagent, 50f, puddleTemperature, out var puddleUid);
            var puddleSolution = solutionEntity.Comp.Solution;

            var spilled = new Solution(WaterReagent.Id, FixedPoint2.New(3)) { Temperature = spilledTemperature };
            var energyBefore = puddleSolution.GetHeatCapacity(protoMan) * puddleSolution.Temperature
                + spilled.GetHeatCapacity(protoMan) * spilled.Temperature;
            var heatCapacityAfter = puddleSolution.GetHeatCapacity(protoMan) + spilled.GetHeatCapacity(protoMan);
            var expectedTemperature = energyBefore / heatCapacityAfter;

            // False is expected: the water's own spill reaction moves all of it into the puddle first, leaving nothing
            //     for TrySpillAt to spill itself. The assertions below are on where it ended up.
            entityManager.System<PuddleSystem>().TrySpillAt(testMap.Tile, spilled, out _, sound: false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(puddleSolution.Volume, Is.EqualTo(FixedPoint2.New(53)), "The water did not all end up in the puddle.");
                Assert.That(puddleSolution.Temperature, Is.EqualTo(expectedTemperature).Within(0.05f),
                    "The puddle did not end at the heat-capacity-weighted mean, so the spill did not conserve energy.");
                Assert.That(entityManager.System<PuddleSystem>().KsTileReactionSourceTemperature, Is.Null,
                    "The tile reaction temperature scope leaked.");
            }
        });
    }

    [Test]
    public async Task TestBlanketSmothersAndKeepsChemfiresOut()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var blanketSystem = entityManager.System<KsFoamBlanketSystem>();
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        var testMap = await CreateBreathableTestMap();
        var gridUid = testMap.Grid.Owner;
        var tile = testMap.Tile.GridIndices;

        EntityUid fireUid = default;
        EntityUid flammableUid = default;

        await server.WaitAssertion(() =>
        {
            fireUid = chemicalFireSystem.SpawnChemicalFire(ChemicalFirePrototype, (gridUid, null), tile)!.Value.Owner;

            flammableUid = entityManager.SpawnEntity(_flammablePrototype, new EntityCoordinates(gridUid, 0.5f, 0.5f));
            entityManager.System<FlammableSystem>().AdjustFireStacks(flammableUid, 5f, ignite: true);
            Assert.That(entityManager.GetComponent<FlammableComponent>(flammableUid).OnFire, "Test flammable did not catch fire.");

            Assert.That(SProtoMan.Index(BlanketPrototype).TryComp<KsFoamBlanketComponent>(out var prototypeComponent, entityManager.ComponentFactory));
            var used = blanketSystem.Deploy(BlanketPrototype, testMap.Tile, 2f);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(used, Is.EqualTo(prototypeComponent!.FoamPerTile).Within(0.001f), "The blanket did not take one tile's worth of foam.");
                Assert.That(blanketSystem.TryGetBlanket(gridUid, tile, out _), "No blanket was laid.");
                Assert.That(entityManager.GetComponent<FlammableComponent>(flammableUid).OnFire, Is.False,
                    "The blanket did not put out what was burning on it.");
                Assert.That(chemicalFireSystem.SpawnChemicalFire(ChemicalFirePrototype, (gridUid, null), tile), Is.Null,
                    "A chemfire started under the blanket.");
                Assert.That(chemicalFireSystem.SpawnChemicalFire(_unextinguishableFirePrototype, (gridUid, null), tile), Is.Not.Null,
                    "A chemfire foam cannot smother was kept out by the blanket.");
            }
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.EntityExists(fireUid), Is.False, "The blanket did not put out the chemfire on its tile.");

            Assert.That(blanketSystem.TryGetBlanket(gridUid, tile, out var blanket));
            entityManager.DeleteEntity(blanket.Owner);

            Assert.That(chemicalFireSystem.SpawnChemicalFire(ChemicalFirePrototype, (gridUid, null), tile), Is.Not.Null,
                "A chemfire could not start once the blanket was gone.");
        });
    }

    /// <summary>
    ///     A welding fuel puddle well above its ignition temperature stays out under a blanket, and lights without one.
    /// </summary>
    [Test]
    public async Task TestHotFuelPuddleStaysOutUnderBlanket([Values(true, false)] bool blanketed)
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        var testMap = await CreateBreathableTestMap();
        var gridUid = testMap.Grid.Owner;
        var tile = testMap.Tile.GridIndices;

        await server.WaitAssertion(() =>
        {
            if (blanketed)
                FillBlanket(testMap.Tile);

            SpillPuddle(testMap, WeldingFuelReagent, 50f, IgnitingPuddleTemperature, out _);
        });

        await server.WaitRunTicks(TicksPerSecond * 2);

        await server.WaitAssertion(() =>
        {
            var burning = chemicalFireSystem.GetTileChemicalFires((gridUid, null), tile) is { Fires.Count: > 0 };
            Assert.That(burning, Is.EqualTo(!blanketed),
                blanketed ? "The fuel puddle lit under the blanket." : "The fuel puddle never lit, so the blanket case proves nothing.");
        });
    }

    /// <summary>
    ///     A blanket over a puddle hotter than the foam's boiling point loses more foam than it does over a cold one,
    ///         by about what boiling takes: the cold one only drains.
    /// </summary>
    [Test]
    public async Task TestBlanketBoilsOffHotPuddleFaster()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var blanketSystem = entityManager.System<KsFoamBlanketSystem>();

        var foamLeft = new float[2];
        var temperatures = new[] { Atmospherics.T20C, 600f };
        var hotPuddleHeatCapacity = 0f;

        for (var i = 0; i < temperatures.Length; i++)
        {
            var testMap = await CreateBreathableTestMap();
            var gridUid = testMap.Grid.Owner;
            var temperature = temperatures[i];

            await server.WaitAssertion(() =>
            {
                var solution = SpillPuddle(testMap, _inertLiquidReagent, 100f, temperature, out _);
                hotPuddleHeatCapacity = solution.Comp.Solution.GetHeatCapacity(server.ProtoMan);
                FillBlanket(testMap.Tile);
            });

            await server.WaitRunTicks(TicksPerSecond * BoilTestSeconds);

            var index = i;
            await server.WaitAssertion(() =>
            {
                Assert.That(blanketSystem.TryGetBlanket(gridUid, testMap.Tile.GridIndices, out var blanket), "The blanket vanished.");
                foamLeft[index] = blanket.Comp.Foam;
            });

            // The pair deletes only the last test map it made, so an earlier one (and its puddle) would outlive the test.
            if (i < temperatures.Length - 1)
                await server.WaitPost(() => entityManager.DeleteEntity(testMap.MapUid));
        }

        // Whole seconds are updates, so a tick or so either side of the count is allowed for.
        var prototype = server.ProtoMan.Index(BlanketPrototype).Components[server.EntMan.ComponentFactory.GetComponentName<KsFoamBlanketComponent>()].Component as KsFoamBlanketComponent;
        Assert.That(prototype, Is.Not.Null);
        var updates = BoilTestSeconds - 1;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(foamLeft[0], Is.EqualTo(prototype!.MaxFoam - prototype.DecayRate * updates).Within(prototype.DecayRate * 2f),
                "A blanket over a cold puddle lost more than it drains by itself.");
            // Whichever runs out first: the rate it may boil at, or the heat the puddle has above the boiling point.
            var energyPerUnit = SharedKsFoamBlanketSystem.GetEnergyPerUnit(prototype);
            var boilable = MathF.Min(prototype.PuddleBoilRate * updates,
                (temperatures[1] - prototype.BoilingPoint) * hotPuddleHeatCapacity / energyPerUnit);
            Assert.That(foamLeft[1], Is.LessThan(foamLeft[0] - boilable * 0.5f),
                "The blanket did not boil off faster over a hot puddle.");
        }
    }

    /// <summary>
    ///     A full blanket draws the heat out of the hot puddle it lies on, not just a token amount: 100u at 600 K ends up
    ///         well over 10 K cooler than the same puddle left to the air alone.
    /// </summary>
    [Test]
    public async Task TestBlanketCoolsTheHotPuddleUnderIt()
    {
        var server = Pair.Server;

        // Side by side, on two maps, so that both see exactly the same updates.
        var bareMap = await CreateBreathableTestMap();
        var blanketedMap = await CreateBreathableTestMap();
        Entity<SolutionComponent> bareSolution = default;
        Entity<SolutionComponent> blanketedSolution = default;

        await server.WaitAssertion(() =>
        {
            bareSolution = SpillPuddle(bareMap, _inertLiquidReagent, 100f, 600f, out _);
            blanketedSolution = SpillPuddle(blanketedMap, _inertLiquidReagent, 100f, 600f, out _);
            FillBlanket(blanketedMap.Tile);
        });

        await server.WaitRunTicks(TicksPerSecond * 4);

        await server.WaitAssertion(() =>
        {
            var bare = bareSolution.Comp!.Solution.Temperature;
            var blanketed = blanketedSolution.Comp!.Solution.Temperature;
            Assert.That(blanketed, Is.LessThan(bare - 10f), $"The blanketed puddle ended at {blanketed} K, against {bare} K without one.");
        });

        // The pair deletes only the last test map it made, so the first would outlive the test.
        await server.WaitPost(() => server.EntMan.DeleteEntity(bareMap.MapUid));
    }

    /// <summary>
    ///     One spray of foam vapor, as a firebot's, lays a blanket on every tile it crosses, not only the first.
    /// </summary>
    [Test]
    public async Task TestSprayBlanketsEveryTileItCrosses()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var testMap = await CreateBreathableTestMap();

        await server.WaitPost(() =>
        {
            FloorRow(testMap);

            var sprayerUid = entityManager.SpawnEntity(_sprayerPrototype, new EntityCoordinates(testMap.Grid.Owner, 0.5f, 0.5f));

            var spray = new Entity<SprayComponent>(sprayerUid, entityManager.GetComponent<SprayComponent>(sprayerUid));
            entityManager.System<SpraySystem>().Spray(spray, new MapCoordinates(new Vector2(RowLength + 0.5f, 0.5f), testMap.MapId));
        });

        await server.WaitRunTicks(TicksPerSecond * 2);

        await server.WaitAssertion(() =>
        {
            Assert.That(CountBlanketedTiles(testMap), Is.GreaterThanOrEqualTo(3),
                "The spray did not lay a blanket on each tile it crossed.");
        });
    }

    /// <summary>
    ///     The extinguisher is a full-auto gun, and its shots lay blankets along their path as the spray does.
    /// </summary>
    /// <remarks>
    ///     <see cref="SharedGunSystem.AttemptShoot(EntityUid, Entity{GunComponent}, EntityCoordinates, EntityUid?)"/>
    ///         resets the shot counter after every call, so calling it each tick fires even a semi-auto gun; that a
    ///         held trigger keeps firing is the gun system's own fire mode handling, so the mode is pinned instead.
    /// </remarks>
    [Test]
    public async Task TestExtinguisherShotsBlanketTheirPath()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var gunSystem = entityManager.System<SharedGunSystem>();

        var testMap = await CreateBreathableTestMap();
        EntityUid extinguisherUid = default;
        var volumeBefore = FixedPoint2.Zero;

        await server.WaitPost(() =>
        {
            FloorRow(testMap);

            extinguisherUid = entityManager.SpawnEntity(ExtinguisherPrototype, new EntityCoordinates(testMap.Grid.Owner, 0.5f, 0.5f));
            entityManager.System<ItemToggleSystem>().TryActivate(extinguisherUid, user: null);
            volumeBefore = SprayVolume(extinguisherUid);

            Assert.That(entityManager.GetComponent<GunComponent>(extinguisherUid).SelectedMode, Is.EqualTo(SelectiveFire.FullAuto),
                "The extinguisher is not full-auto.");
            Assert.That(entityManager.HasComponent<SprayComponent>(extinguisherUid), Is.False,
                "The extinguisher still sprays vapor on a click, alongside its shots.");
        });

        // A held trigger asks to shoot every tick.
        var shotTarget = new EntityCoordinates(testMap.Grid.Owner, RowLength + 0.5f, 0.5f);
        for (var tick = 0; tick < TicksPerSecond; tick++)
        {
            await server.WaitPost(() =>
            {
                var gun = new Entity<GunComponent>(extinguisherUid, entityManager.GetComponent<GunComponent>(extinguisherUid));
                gunSystem.AttemptShoot(extinguisherUid, gun, shotTarget);
            });

            await server.WaitRunTicks(1);
        }

        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SprayVolume(extinguisherUid), Is.LessThan(volumeBefore - FixedPoint2.New(3)),
                    "A second of shooting fired no more than a couple of shots.");
                // Its 0.35 s life carries a shot from the muzzle's tile across the next two.
                Assert.That(CountBlanketedTiles(testMap), Is.GreaterThanOrEqualTo(2),
                    "The shots did not lay a blanket on each tile they crossed.");
            }
        });
    }

    /// <summary>
    ///     With its safety on, an extinguisher does not fire.
    /// </summary>
    [Test]
    public async Task TestExtinguisherSafetyBlocksShooting()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var testMap = await CreateBreathableTestMap();

        await server.WaitAssertion(() =>
        {
            var extinguisherUid = entityManager.SpawnEntity(ExtinguisherPrototype, new EntityCoordinates(testMap.Grid.Owner, 0.5f, 0.5f));
            var volumeBefore = SprayVolume(extinguisherUid);

            var gun = new Entity<GunComponent>(extinguisherUid, entityManager.GetComponent<GunComponent>(extinguisherUid));
            var shot = entityManager.System<SharedGunSystem>().AttemptShoot(extinguisherUid, gun,
                new EntityCoordinates(testMap.Grid.Owner, 4.5f, 0.5f));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(shot, Is.False, "The extinguisher fired with its safety on.");
                Assert.That(SprayVolume(extinguisherUid), Is.EqualTo(volumeBefore), "Foam left an extinguisher with its safety on.");
            }
        });
    }

    /// <summary>
    ///     A blanket the server lays reaches the client with its cache entry and its foam.
    /// </summary>
    [Test]
    public async Task TestBlanketReplicatesToClient()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var blanketSystem = server.EntMan.System<KsFoamBlanketSystem>();

        var testMap = await CreateBreathableTestMap();
        var tile = testMap.Tile.GridIndices;

        // The blanket decays every update, and the client may be one state behind the server's latest.
        var prototype = (KsFoamBlanketComponent)server.ProtoMan.Index(BlanketPrototype)
            .Components[server.EntMan.ComponentFactory.GetComponentName<KsFoamBlanketComponent>()].Component;
        var tolerance = prototype.DecayRate * (float)prototype.UpdateInterval.TotalSeconds + 0.0001f;

        await server.WaitPost(() => Assert.That(blanketSystem.Deploy(BlanketPrototype, testMap.Tile, 1f), Is.GreaterThan(0f)));
        await Pair.RunTicksSync(10);

        var clientGridUid = ToClientUid(testMap.Grid.Owner);
        var serverFoam = 0f;
        await server.WaitAssertion(() =>
        {
            Assert.That(blanketSystem.TryGetBlanket(testMap.Grid.Owner, tile, out var serverBlanket));
            serverFoam = serverBlanket.Comp.Foam;
        });

        await client.WaitAssertion(() =>
        {
            var clientSystem = client.EntMan.System<ClientBlanketSystem>();
            Assert.That(clientSystem.TryGetBlanket(clientGridUid, tile, out var clientBlanket),
                "The client did not register the blanket in its tile cache.");
            Assert.That(clientBlanket.Comp.Foam, Is.EqualTo(serverFoam).Within(tolerance), "The foam was not networked.");
            Assert.That(clientBlanket.Comp.Foam, Is.GreaterThan(0f));
        });

        // Topping it up must reach the client too.
        await server.WaitPost(() => blanketSystem.Deploy(BlanketPrototype, testMap.Tile, 1f));
        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(blanketSystem.TryGetBlanket(testMap.Grid.Owner, tile, out var serverBlanket));
            serverFoam = serverBlanket.Comp.Foam;
        });
        await client.WaitAssertion(() =>
        {
            Assert.That(client.EntMan.System<ClientBlanketSystem>().TryGetBlanket(clientGridUid, tile, out var clientBlanket));
            Assert.That(clientBlanket.Comp.Foam, Is.EqualTo(serverFoam).Within(tolerance), "A top-up did not reach the client.");
        });
    }

    /// <summary>
    ///     The client refuses to start a chemfire under a blanket it has been sent, as the server does.
    /// </summary>
    [Test]
    public async Task TestClientVetoesChemfireUnderBlanket()
    {
        var server = Pair.Server;
        var client = Pair.Client;

        var testMap = await CreateBreathableTestMap();
        await server.WaitPost(() => FloorRow(testMap));
        var tile = testMap.Tile.GridIndices;
        var controlTile = tile + Vector2i.Right;

        await server.WaitPost(() => FillBlanket(testMap.Tile));
        await Pair.RunTicksSync(10);

        var clientGridUid = ToClientUid(testMap.Grid.Owner);
        await client.WaitPost(() =>
        {
            var chemicalFireSystem = client.EntMan.System<SharedChemicalFireSystem>();

            Assert.That(chemicalFireSystem.SpawnChemicalFire(ChemicalFirePrototype, (clientGridUid, null), tile), Is.Null,
                "The client started a chemfire under the blanket.");

            var control = chemicalFireSystem.SpawnChemicalFire(ChemicalFirePrototype, (clientGridUid, null), controlTile);
            Assert.That(control, Is.Not.Null, "The client could not start a chemfire on a bare tile, so the veto proves nothing.");
            client.EntMan.DeleteEntity(control!.Value.Owner);
        });
    }

    /// <summary>
    ///     Something burning that walks onto a blanket is put out by the step trigger, not by the deploy's smother.
    /// </summary>
    [Test]
    public async Task TestWalkingOntoBlanketExtinguishes([Values(true, false)] bool blanketed)
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var testMap = await CreateBreathableTestMap();
        var gridUid = testMap.Grid.Owner;
        EntityUid mobUid = default;

        await server.WaitPost(() =>
        {
            FloorRow(testMap);
            if (blanketed)
                FillBlanket(testMap.Tile);

            // Lit after the blanket is laid, and away from it, so only walking on can put it out.
            mobUid = entityManager.SpawnEntity(_mobPrototype, new EntityCoordinates(gridUid, 4.5f, 0.5f));
            entityManager.System<FlammableSystem>().AdjustFireStacks(mobUid, 5f, ignite: true);
            Assert.That(entityManager.GetComponent<FlammableComponent>(mobUid).OnFire, "Test mob did not catch fire.");
        });

        await server.WaitRunTicks(5);

        await server.WaitPost(() =>
        {
            Assert.That(entityManager.GetComponent<FlammableComponent>(mobUid).OnFire, "The mob went out before it reached the blanket.");
            entityManager.System<SharedTransformSystem>().SetCoordinates(mobUid, new EntityCoordinates(gridUid, 0.5f, 0.5f));
        });

        await server.WaitRunTicks(TicksPerSecond);
        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.GetComponent<FlammableComponent>(mobUid).OnFire, Is.Not.EqualTo(blanketed),
                blanketed ? "Walking onto the blanket did not put the mob out." : "The mob went out without a blanket, so the blanket case proves nothing.");
        });
    }

    /// <summary>
    ///     The client fades a blanket with the foam it holds.
    /// </summary>
    [Test]
    public async Task TestClientBlanketFadesWithFoam()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var blanketSystem = server.EntMan.System<KsFoamBlanketSystem>();

        var testMap = await CreateBreathableTestMap();
        var tile = testMap.Tile.GridIndices;
        var clientGridUid = ToClientUid(testMap.Grid.Owner);
        var alphas = new float[2];

        await server.WaitPost(() => Assert.That(blanketSystem.Deploy(BlanketPrototype, testMap.Tile, 1f), Is.GreaterThan(0f)));
        await Pair.RunTicksSync(10);
        await client.WaitAssertion(() => alphas[0] = ClientBlanketAlpha(clientGridUid, tile));

        await server.WaitPost(() => FillBlanket(testMap.Tile));
        await Pair.RunTicksSync(10);
        await client.WaitAssertion(() => alphas[1] = ClientBlanketAlpha(clientGridUid, tile));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(alphas[0], Is.EqualTo(0.35f + 0.5f * (0.1f / 1.5f)).Within(0.02f), "A single dose is not faded to its foam level.");
            Assert.That(alphas[1], Is.EqualTo(0.85f).Within(0.02f), "A full blanket is not fully opaque.");
        }
    }

    /// <summary>
    ///     A blanket whose foam runs out dissolves instead of vanishing: it stays for its dissolve time, but stops
    ///         counting as the tile's blanket, and then goes.
    /// </summary>
    [Test]
    public async Task TestBlanketDissolvesWhenFoamRunsOut()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var blanketSystem = entityManager.System<KsFoamBlanketSystem>();
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        var testMap = await CreateBreathableTestMap();
        var gridUid = testMap.Grid.Owner;
        var tile = testMap.Tile.GridIndices;
        EntityUid blanketUid = default;
        EntityUid newBlanketUid = default;
        TimeSpan dissolveDuration = default;

        await server.WaitPost(() =>
        {
            // The least foam a blanket can be laid with, so it drains away within a few seconds.
            Assert.That(blanketSystem.Deploy(BlanketPrototype, testMap.Tile, 0.04f), Is.GreaterThan(0f));
            Assert.That(blanketSystem.TryGetBlanket(gridUid, tile, out var blanket));
            blanketUid = blanket.Owner;
            dissolveDuration = blanket.Comp.DissolveDuration;
            Assert.That(blanket.Comp.DissolveEndTime, Is.Null, "A new blanket was already dissolving.");
        });

        // Until it starts dissolving, a tick at a time, so the dissolve is caught while it lasts.
        for (var i = 0; i < TicksPerSecond * 10; i++)
        {
            await server.WaitRunTicks(1);
            if (!entityManager.TryGetComponent<KsFoamBlanketComponent>(blanketUid, out var drainingComponent) ||
                drainingComponent.DissolveEndTime != null)
                break;
        }

        await server.WaitAssertion(() =>
        {
            var blanketComponent = entityManager.GetComponentOrNull<KsFoamBlanketComponent>(blanketUid);
            Assert.That(blanketComponent, Is.Not.Null, "The blanket vanished as soon as its foam ran out instead of dissolving.");
            Assert.That(blanketComponent!.DissolveEndTime, Is.Not.Null, "The blanket whose foam ran out is not dissolving.");
            Assert.That(blanketSystem.IsDissolving((blanketUid, blanketComponent)));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(blanketSystem.TryGetBlanket(gridUid, tile, out _), Is.False, "A dissolving blanket is still the tile's blanket.");
                // TryGetBlanket skips a dissolving blanket by itself, so the cache is read as it is.
                Assert.That(blanketSystem.IsTileRegistered(gridUid, tile), Is.False,
                    "A dissolving blanket is still on the tile's cache.");
                var fire = chemicalFireSystem.SpawnChemicalFire(ChemicalFirePrototype, (gridUid, null), tile);
                Assert.That(fire, Is.Not.Null, "A dissolving blanket still keeps chemfires out.");
                if (fire != null)
                    entityManager.DeleteEntity(fire.Value.Owner);
            }

            // Spraying the tile while the old one dissolves lays a new blanket instead of topping it up.
            Assert.That(blanketSystem.Deploy(BlanketPrototype, testMap.Tile, 1f), Is.GreaterThan(0f), "Nothing was laid over a dissolving blanket.");
            Assert.That(blanketSystem.TryGetBlanket(gridUid, tile, out var newBlanket), "The new blanket is not the tile's blanket.");
            newBlanketUid = newBlanket.Owner;
            Assert.That(newBlanketUid, Is.Not.EqualTo(blanketUid), "A dissolving blanket was topped up instead of replaced.");
        });

        // Past the dissolve time, plus a few ticks for the queued deletion.
        await server.WaitRunTicks((int)Math.Ceiling(dissolveDuration.TotalSeconds * TicksPerSecond) + 5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.EntityExists(blanketUid), Is.False, "The blanket was not removed once it had dissolved.");
            // The old blanket's removal must not take the new one off the cache.
            Assert.That(blanketSystem.TryGetBlanket(gridUid, tile, out var newBlanket) && newBlanket.Owner == newBlanketUid,
                "The dissolved blanket's removal unregistered the blanket laid over it.");
        });
    }

    /// <summary>
    ///     A dissolving blanket gets the dissolve mask on its foam layer, before it; a holding one does not.
    /// </summary>
    [Test]
    public async Task TestClientDissolvingBlanketGetsDissolveMask()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var blanketSystem = server.EntMan.System<KsFoamBlanketSystem>();

        var testMap = await CreateBreathableTestMap();
        var tile = testMap.Tile.GridIndices;
        EntityUid blanketUid = default;

        await server.WaitPost(() =>
        {
            // A prototype that dissolves for 30s, so the blanket outlives however long the state takes to arrive.
            Assert.That(blanketSystem.Deploy(_slowBlanketPrototype, testMap.Tile, 1f), Is.GreaterThan(0f));
            Assert.That(blanketSystem.TryGetBlanket(testMap.Grid.Owner, tile, out var blanket));
            blanketUid = blanket.Owner;
        });
        await Pair.RunTicksSync(10);

        var clientBlanketUid = ToClientUid(blanketUid);
        await client.WaitAssertion(() => AssertDissolveMask(clientBlanketUid, expected: false));

        await server.WaitPost(() =>
        {
            var blanketComponent = server.EntMan.GetComponent<KsFoamBlanketComponent>(blanketUid);
            blanketSystem.StartDissolving((blanketUid, blanketComponent));
        });
        await Pair.RunTicksSync(30);

        await client.WaitAssertion(() =>
        {
            Assert.That(client.EntMan.GetComponent<KsFoamBlanketComponent>(clientBlanketUid).DissolveEndTime, Is.Not.Null,
                "The dissolve never reached the client.");
            AssertDissolveMask(clientBlanketUid, expected: true);

            var clientSystem = client.EntMan.System<ClientBlanketSystem>();
            Assert.That(clientSystem.TryGetBlanket(ToClientUid(testMap.Grid.Owner), tile, out _), Is.False,
                "The client still has the dissolving blanket in its tile cache.");
        });
    }

    private void AssertDissolveMask(EntityUid clientBlanketUid, bool expected)
    {
        var entityManager = Pair.Client.EntMan;
        var spriteSystem = entityManager.System<SpriteSystem>();
        var spriteComponent = entityManager.GetComponent<SpriteComponent>(clientBlanketUid);
        var sprite = (clientBlanketUid, spriteComponent);

        Assert.That(spriteSystem.LayerMapTryGet(sprite, "foam", out var foamIndex, logMissing: false), "The blanket has no foam layer.");
        var hasMask = spriteSystem.LayerMapTryGet(sprite, "foam-displacement", out var maskIndex, logMissing: false);
        var shader = spriteComponent[foamIndex] is SpriteComponent.Layer layer ? layer.ShaderPrototype?.ToString() : null;

        if (!expected)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(hasMask, Is.False, "A blanket that is not dissolving has the dissolve mask.");
                Assert.That(shader, Is.Null.Or.Empty, "A blanket that is not dissolving has a displacement shader.");
            }

            return;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hasMask, Is.True, "A dissolving blanket has no dissolve mask layer.");
            Assert.That(shader, Is.EqualTo("DisplacedDraw"), "The foam layer of a dissolving blanket has no displacement shader.");
            // The engine copies the mask in layer order, so a mask after its target shows a frame late.
            Assert.That(maskIndex, Is.LessThan(foamIndex), "The mask layer is not before the foam layer it displaces.");
            Assert.That(spriteComponent[maskIndex] is SpriteComponent.Layer { CopyToShaderParameters.LayerKey: "foam" },
                "The mask layer does not copy into the foam layer.");
        }
    }

    private float ClientBlanketAlpha(EntityUid clientGridUid, Vector2i tile)
    {
        Assert.That(Pair.Client.EntMan.System<ClientBlanketSystem>().TryGetBlanket(clientGridUid, tile, out var blanket),
            "The client has no blanket.");
        return Pair.Client.EntMan.GetComponent<SpriteComponent>(blanket.Owner).Color.A;
    }

    /// <summary>
    ///     A blanket taken off its tile (unanchored, which is also what a tile turning to space does to it) stops
    ///         counting as that tile's blanket at once, so chemfires start there again, and the server removes it.
    /// </summary>
    [Test]
    public async Task TestUnanchoredBlanketLeavesItsTile()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var blanketSystem = entityManager.System<KsFoamBlanketSystem>();
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        var testMap = await CreateBreathableTestMap();
        var gridUid = testMap.Grid.Owner;
        var tile = testMap.Tile.GridIndices;
        EntityUid blanketUid = default;

        await server.WaitAssertion(() =>
        {
            Assert.That(blanketSystem.Deploy(BlanketPrototype, testMap.Tile, 1f), Is.GreaterThan(0f));
            Assert.That(blanketSystem.TryGetBlanket(gridUid, tile, out var blanket));
            blanketUid = blanket.Owner;
            Assert.That(chemicalFireSystem.SpawnChemicalFire(ChemicalFirePrototype, (gridUid, null), tile), Is.Null,
                "The blanket did not keep a chemfire out to begin with.");

            entityManager.System<SharedTransformSystem>().Unanchor(blanketUid);

            // Before any tick: the old tile must already be free.
            Assert.That(blanketSystem.TryGetBlanket(gridUid, tile, out _), Is.False, "An unanchored blanket is still on its tile.");
            var fire = chemicalFireSystem.SpawnChemicalFire(ChemicalFirePrototype, (gridUid, null), tile);
            Assert.That(fire, Is.Not.Null, "An unanchored blanket still keeps chemfires out of its old tile.");
            if (fire != null)
                entityManager.DeleteEntity(fire.Value.Owner);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
            Assert.That(entityManager.EntityExists(blanketUid), Is.False, "The server kept a blanket that is no longer on a tile."));
    }

    /// <summary>
    ///     A blanket moved to another grid without being unanchored (as a grid split does) is registered on the grid
    ///         and tile it is on now, not the one it was laid on.
    /// </summary>
    [Test]
    public async Task TestReparentedBlanketIsRegisteredWhereItIs()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var blanketSystem = entityManager.System<KsFoamBlanketSystem>();
        var transformSystem = entityManager.System<SharedTransformSystem>();

        var oldMap = await CreateBreathableTestMap();
        var newMap = await Pair.CreateTestMap();
        var oldGridUid = oldMap.Grid.Owner;
        var newGridUid = newMap.Grid.Owner;
        var oldTile = oldMap.Tile.GridIndices;
        EntityUid blanketUid = default;

        await server.WaitAssertion(() =>
        {
            Assert.That(blanketSystem.Deploy(BlanketPrototype, oldMap.Tile, 1f), Is.GreaterThan(0f));
            Assert.That(blanketSystem.TryGetBlanket(oldGridUid, oldTile, out var blanket));
            blanketUid = blanket.Owner;

            // Not unanchoring, so only the parent changes.
            transformSystem.SetCoordinates(
                new Entity<TransformComponent, MetaDataComponent>(blanketUid, entityManager.GetComponent<TransformComponent>(blanketUid), entityManager.GetComponent<MetaDataComponent>(blanketUid)),
                new EntityCoordinates(newGridUid, 0.5f, 0.5f), unanchor: false);
            Assert.That(transformSystem.GetGrid(blanketUid), Is.EqualTo((EntityUid?)newGridUid), "The blanket did not move to the new grid.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(blanketSystem.TryGetBlanket(oldGridUid, oldTile, out _), Is.False, "The blanket is still registered on the grid it left.");
                Assert.That(blanketSystem.TryGetBlanket(newGridUid, newMap.Tile.GridIndices, out var moved) && moved.Owner == blanketUid,
                    "The blanket is not registered on the grid it moved to.");
            }
        });

        // The pair deletes only the last test map it made, so the first would outlive the test.
        await server.WaitPost(() => entityManager.DeleteEntity(oldMap.MapUid));
    }

    /// <summary>
    ///     The client reads a reagent's tile reactions from the same entries the server does, but keeps only those it
    ///         predicts, in the server's order: for every reagent, its list is the server's with the rest left out.
    /// </summary>
    [Test]
    public async Task TestClientReadsOnlyThePredictedTileReactions()
    {
        var predictedReactionNames = new HashSet<string>
        {
            "SpillTileReaction",
            "SpillIfPuddlePresentTileReaction",
            nameof(KsDeployFoamBlanketTileReaction),
        };

        var serverReactions = new Dictionary<string, List<string>>();
        await Pair.Server.WaitPost(() =>
        {
            foreach (var reagent in SProtoMan.EnumeratePrototypes<ReagentPrototype>())
                serverReactions[reagent.ID] = reagent.TileReactions.Select(reaction => reaction.GetType().Name).ToList();
        });

        var clientReactions = new Dictionary<string, List<string>>();
        await Pair.Client.WaitPost(() =>
        {
            foreach (var reagent in Pair.Client.ProtoMan.EnumeratePrototypes<ReagentPrototype>())
                clientReactions[reagent.ID] = reagent.TileReactions.Select(reaction => reaction.GetType().Name).ToList();
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(serverReactions[FirefightingFoamReagent.Id], Does.Contain(nameof(KsDeployFoamBlanketTileReaction)),
                "Foam lays no blankets, so this proves nothing about it.");
            Assert.That(serverReactions.Values.Any(names => names.Any(name => !predictedReactionNames.Contains(name))),
                "The server has no reactions the client leaves out, so this proves nothing about leaving them out.");

            foreach (var (reagentId, names) in serverReactions)
            {
                Assert.That(clientReactions[reagentId], Is.EqualTo(names.Where(predictedReactionNames.Contains).ToList()),
                    $"The client's tile reactions for {reagentId} are not the server's predicted ones.");
            }
        }
    }

    /// <summary>
    ///     Only the shooter's kept copy of a shot lays blankets on the client, through the shared vapor code, as it
    ///         crosses tiles, from what the gun filled it with; a re-predicted throwaway of the shot and the server's copy
    ///         lay none. Its blankets are the client's own placeholders rather than engine predicted spawns, so no
    ///         state reset deletes them. When the server's blanket for a tile arrives it takes over, and the placeholder
    ///         goes; one the server never replaces goes once its time is up.
    /// </summary>
    /// <remarks>
    ///     The shots are driven by hand inside one client call. The kept copy stands in for the one
    ///         <c>KsPredictedSpawnSystem</c> keeps for a real shot, which needs a player; see
    ///         <see cref="KsPredictedFoamShotTest"/> for one fired for real.
    /// </remarks>
    [Test]
    public async Task TestPredictedFoamShotLaysClientBlankets()
    {
        var server = Pair.Server;
        var client = Pair.Client;

        var testMap = await CreateBreathableTestMap();
        EntityUid extinguisherUid = default;

        await server.WaitPost(() =>
        {
            FloorRow(testMap);
            // A second row, for the throwaway.
            var mapSystem = server.EntMan.System<SharedMapSystem>();
            for (var x = 0; x < RowLength; x++)
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 1), testMap.Tile.Tile);

            extinguisherUid = server.EntMan.SpawnEntity(ExtinguisherPrototype, new EntityCoordinates(testMap.Grid.Owner, 0.5f, 0.5f));
        });
        await Pair.RunTicksSync(10);

        var clientGridUid = ToClientUid(testMap.Grid.Owner);
        var clientExtinguisherUid = ToClientUid(extinguisherUid);
        var placeholderUids = new EntityUid[3];

        await client.WaitAssertion(() =>
        {
            var entityManager = client.EntMan;
            var blanketSystem = entityManager.System<ClientBlanketSystem>();
            var vaporSystem = entityManager.System<SharedVaporSystem>();
            var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
            var transformSystem = entityManager.System<SharedTransformSystem>();

            // The shooter's kept copy: client-side, kept, and no longer an engine predicted spawn.
            var keptShot = entityManager.SpawnAtPosition(_extinguisherShotPrototype, new EntityCoordinates(clientGridUid, 0.5f, 0.5f));
            entityManager.AddComponent<KsKeptPredictedSpawnComponent>(keptShot);
            // A re-predicted throwaway of the same shot: an engine predicted spawn.
            var throwawayShot = entityManager.PredictedSpawnAtPosition(_extinguisherShotPrototype, new EntityCoordinates(clientGridUid, 0.5f, 1.5f));
            // The server's copy as the client holds it: neither.
            var serverShot = entityManager.SpawnAtPosition(_extinguisherShotPrototype, new EntityCoordinates(clientGridUid, 4.5f, 0.5f));

            // Each filled with a shot's worth of the gun's solution, as the gun fills its shots.
            var fireCost = entityManager.GetComponent<SolutionAmmoProviderComponent>(clientExtinguisherUid).FireCost;
            Assert.That(solutionContainerSystem.TryGetSolution(clientExtinguisherUid, "spray", out var spraySolution, out _));
            foreach (var shotUid in new[] { keptShot, throwawayShot, serverShot })
                Assert.That(vaporSystem.TryAddSolution(shotUid, spraySolution!.Value, fireCost), "The shot was not filled.");

            var shotSolution = entityManager.GetComponent<SolutionComponent>(keptShot).Solution;
            var carried = (float)shotSolution.GetTotalPrototypeQuantity(FirefightingFoamReagent);
            Assert.That(carried, Is.GreaterThan(0f), "The shot carries no foam.");

            var foams = new float[3];
            for (var x = 0; x < 3; x++)
            {
                transformSystem.SetCoordinates(keptShot, new EntityCoordinates(clientGridUid, x + 0.5f, 0.5f));
                transformSystem.SetCoordinates(throwawayShot, new EntityCoordinates(clientGridUid, x + 0.5f, 1.5f));
                transformSystem.SetCoordinates(serverShot, new EntityCoordinates(clientGridUid, x + 4.5f, 0.5f));
                vaporSystem.Update(0f);

                Assert.That(blanketSystem.TryGetBlanket(clientGridUid, new Vector2i(x, 0), out var blanket), $"No blanket on tile {x}.");
                foams[x] = blanket.Comp.Foam;
                placeholderUids[x] = blanket.Owner;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entityManager.HasComponent<KsTilePlaceholderComponent>(blanket.Owner), "The client's blanket is not a placeholder.");
                    Assert.That(entityManager.HasComponent<PredictedSpawnComponent>(blanket.Owner), Is.False,
                        "The client's blanket is an engine predicted spawn, which the next state reset deletes.");
                    Assert.That(entityManager.IsClientSide(blanket.Owner), "The client's blanket is not client-side.");
                }
            }

            // Staying on a tile lays nothing more.
            vaporSystem.Update(0f);
            vaporSystem.Update(0f);

            var vaporComponent = entityManager.GetComponent<VaporComponent>(keptShot);
            Assert.That(SProtoMan.Index(BlanketPrototype).TryComp<KsFoamBlanketComponent>(out var blanketPrototypeComponent, Pair.Server.EntMan.ComponentFactory));

            // Each tile takes what the vapor offers it of what is left, at most one tile's worth.
            using (Assert.EnterMultipleScope())
            {
                var left = carried;
                for (var x = 0; x < 3; x++)
                {
                    var offer = Math.Max(left * vaporComponent.TransferAmountPercentage, vaporComponent.MinimumTransferAmount);
                    Assert.That(foams[x], Is.EqualTo(MathF.Min(offer, blanketPrototypeComponent!.FoamPerTile)).Within(0.011f),
                        $"Tile {x} did not take what the vapor offered it.");
                    left -= foams[x];
                }

                Assert.That((float)shotSolution.GetTotalPrototypeQuantity(FirefightingFoamReagent), Is.EqualTo(left).Within(0.011f),
                    "The shot did not spend what it laid.");
                for (var x = 0; x < 3; x++)
                {
                    Assert.That(blanketSystem.TryGetBlanket(clientGridUid, new Vector2i(x, 1), out _), Is.False,
                        $"A re-predicted throwaway shot laid a blanket on tile ({x}, 1) on the client.");
                }

                for (var x = 4; x < 7; x++)
                {
                    Assert.That(blanketSystem.TryGetBlanket(clientGridUid, new Vector2i(x, 0), out _), Is.False,
                        $"The server's shot laid a blanket on tile {x} on the client.");
                }
            }

            entityManager.DeleteEntity(keptShot);
            entityManager.DeleteEntity(serverShot);
        });

        // The server lays its own on the first tile only.
        EntityUid serverBlanketUid = default;
        await server.WaitPost(() =>
        {
            var serverBlanketSystem = server.EntMan.System<KsFoamBlanketSystem>();
            Assert.That(serverBlanketSystem.Deploy(BlanketPrototype, testMap.Tile, 1f), Is.GreaterThan(0f));
            Assert.That(serverBlanketSystem.TryGetBlanket(testMap.Grid.Owner, testMap.Tile.GridIndices, out var serverBlanket));
            serverBlanketUid = serverBlanket.Owner;
        });
        await Pair.RunTicksSync(10);
        var clientServerBlanketUid = ToClientUid(serverBlanketUid);

        await client.WaitAssertion(() =>
        {
            var blanketSystem = client.EntMan.System<ClientBlanketSystem>();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(blanketSystem.TryGetBlanket(clientGridUid, new Vector2i(0, 0), out var blanket) && blanket.Owner == clientServerBlanketUid,
                    "The server's blanket did not take over the tile from the client's.");
                Assert.That(client.EntMan.EntityExists(placeholderUids[0]), Is.False, "The client's blanket stayed once the server's arrived.");
                Assert.That(client.EntMan.EntityExists(placeholderUids[1]), "The client's blanket did not wait for the server's.");
            }
        });

        // Past the placeholder's lifetime: the server never lays the other two.
        await Pair.RunTicksSync(TicksPerSecond * 2);

        await client.WaitAssertion(() =>
        {
            var blanketSystem = client.EntMan.System<ClientBlanketSystem>();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(client.EntMan.EntityExists(placeholderUids[1]), Is.False, "A client blanket the server never replaced did not time out.");
                Assert.That(client.EntMan.EntityExists(placeholderUids[2]), Is.False, "A client blanket the server never replaced did not time out.");
                Assert.That(blanketSystem.TryGetBlanket(clientGridUid, new Vector2i(1, 0), out _), Is.False, "A timed-out client blanket is still on its tile.");
                // The timed-out ones going must not take the server's off its tile.
                Assert.That(blanketSystem.TryGetBlanket(clientGridUid, new Vector2i(0, 0), out var blanket) && blanket.Owner == clientServerBlanketUid,
                    "The server's blanket left the tile.");
            }
        });
    }

    /// <summary>
    ///     Something flammable touching a blanket is in foam; touching two and leaving one, it still is; leaving both,
    ///         or the blanket going (deleted or dissolving), it is not.
    /// </summary>
    [Test]
    public async Task TestInFoamTracksTheBlanketsTouched()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var blanketSystem = entityManager.System<KsFoamBlanketSystem>();
        var transformSystem = entityManager.System<SharedTransformSystem>();

        var testMap = await CreateBreathableTestMap();
        var gridUid = testMap.Grid.Owner;
        EntityUid mobUid = default;
        EntityUid firstBlanketUid = default;
        EntityUid secondBlanketUid = default;

        await server.WaitPost(() =>
        {
            FloorRow(testMap);
            firstBlanketUid = LayBlanket(testMap, new Vector2i(0, 0));
            secondBlanketUid = LayBlanket(testMap, new Vector2i(1, 0));
            mobUid = entityManager.SpawnEntity(_mobPrototype, new EntityCoordinates(gridUid, 4.5f, 0.5f));
        });
        await server.WaitRunTicks(5);

        async Task MoveTo(float x)
        {
            await server.WaitPost(() =>
            {
                transformSystem.SetCoordinates(mobUid, new EntityCoordinates(gridUid, x, 0.5f));
                // As walking would: physics only updates the contacts of a body that is awake.
                entityManager.System<SharedPhysicsSystem>().WakeBody(mobUid);
            });
            await server.WaitRunTicks(3);
        }

        HashSet<EntityUid>? Touched() => entityManager.GetComponentOrNull<KsInFoamComponent>(mobUid)?.Blankets.ToHashSet();

        await server.WaitAssertion(() => Assert.That(Touched(), Is.Null, "The mob is in foam before it reached any."));

        // On the seam between the two blankets.
        await MoveTo(1f);
        await server.WaitAssertion(() => Assert.That(Touched() ?? [], Is.EquivalentTo(new[] { firstBlanketUid, secondBlanketUid }),
            "On the seam, the mob is not in both blankets."));

        // Its own fixture off the first blanket, but its wider flammable one still on it: still in both.
        await MoveTo(1.3f);
        await server.WaitAssertion(() => Assert.That(Touched() ?? [], Is.EquivalentTo(new[] { firstBlanketUid, secondBlanketUid }),
            "One of the mob's fixtures leaving a blanket took it out of that blanket while another still touched it."));

        // Only on the second, its own fixture and its flammable one both reaching off the far side.
        await MoveTo(1.75f);
        await server.WaitAssertion(() => Assert.That(Touched() ?? [], Is.EquivalentTo(new[] { secondBlanketUid }),
            "Leaving one of two blankets did not leave the mob in the other alone."));

        await MoveTo(4.5f);
        await server.WaitAssertion(() => Assert.That(Touched(), Is.Null, "Leaving every blanket did not take the mob out of foam."));

        // Deleted while touched.
        await MoveTo(0.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Touched() ?? [], Is.EquivalentTo(new[] { firstBlanketUid }), "The mob did not go back into foam.");
            entityManager.DeleteEntity(firstBlanketUid);
            Assert.That(Touched(), Is.Null, "Deleting the blanket the mob was in did not take it out of foam.");
        });
        await server.WaitRunTicks(3);
        await server.WaitAssertion(() => Assert.That(Touched(), Is.Null, "The mob went back into a deleted blanket."));

        // Dissolving while touched.
        await MoveTo(1.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Touched() ?? [], Is.EquivalentTo(new[] { secondBlanketUid }), "The mob did not go into the second blanket.");
            blanketSystem.StartDissolving((secondBlanketUid, entityManager.GetComponent<KsFoamBlanketComponent>(secondBlanketUid)));
            Assert.That(Touched(), Is.Null, "The blanket the mob was in dissolving did not take it out of foam.");
        });
    }

    /// <summary>
    ///     A burning mob that walks into foam goes out, and cannot be set alight there, neither directly nor by a fire on
    ///         its tile. Once it walks out again it can.
    /// </summary>
    [Test]
    public async Task TestBurningMobInFoamGoesOutAndStaysOut()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var flammableSystem = entityManager.System<FlammableSystem>();
        var tileFireSystem = entityManager.System<KsTileFireSystem>();
        var transformSystem = entityManager.System<SharedTransformSystem>();

        var testMap = await CreateBreathableTestMap();
        var gridUid = testMap.Grid.Owner;
        var foamTile = testMap.Tile.GridIndices;
        var bareTile = new Vector2i(4, 0);
        EntityUid mobUid = default;

        bool OnFire() => entityManager.GetComponent<FlammableComponent>(mobUid).OnFire;

        async Task BurnTile(Vector2i tile)
        {
            for (var i = 0; i < TicksPerSecond * 2; i++)
            {
                await server.WaitPost(() => tileFireSystem.RaiseTileFire(gridUid, tile, 1000f, Atmospherics.CellVolume));
                await server.WaitRunTicks(1);
            }
        }

        await server.WaitPost(() =>
        {
            FloorRow(testMap);
            FillBlanket(testMap.Tile);

            mobUid = entityManager.SpawnEntity(_mobPrototype, new EntityCoordinates(gridUid, bareTile.X + 0.5f, 0.5f));
            flammableSystem.AdjustFireStacks(mobUid, 5f, ignite: true);
            Assert.That(OnFire(), "Test mob did not catch fire.");
        });
        await server.WaitRunTicks(5);

        await server.WaitPost(() => MoveAwake(mobUid, new EntityCoordinates(gridUid, 0.5f, 0.5f)));
        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entityManager.HasComponent<KsInFoamComponent>(mobUid), "The mob on the blanket is not in foam.");
                Assert.That(OnFire(), Is.False, "Walking into foam did not put the mob out.");
            }

            flammableSystem.AdjustFireStacks(mobUid, 5f, ignite: true);
            Assert.That(OnFire(), Is.False, "Adding fire stacks set the mob alight in foam.");
            flammableSystem.Ignite(mobUid, gridUid);
            Assert.That(OnFire(), Is.False, "Igniting the mob set it alight in foam.");
        });

        await BurnTile(foamTile);
        await server.WaitAssertion(() => Assert.That(OnFire(), Is.False, "A fire on its tile set the mob alight in foam."));

        // Out of the foam again.
        await server.WaitPost(() => MoveAwake(mobUid, new EntityCoordinates(gridUid, bareTile.X + 0.5f, 0.5f)));
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.HasComponent<KsInFoamComponent>(mobUid), Is.False, "The mob is still in foam after leaving it.");
            flammableSystem.Ignite(mobUid, mobUid);
            Assert.That(OnFire(), "The mob could not be set alight once out of the foam.");
            flammableSystem.Extinguish(mobUid);
        });

        await BurnTile(bareTile);
        await server.WaitAssertion(() => Assert.That(OnFire(), "A fire on its tile did not set the mob alight out of the foam."));
    }

    /// <summary>
    ///     Something burning that lands in foam without setting off the blanket's step trigger (a light item, touching
    ///         it only with a non-hard fixture) still goes out, because it cannot keep burning there.
    /// </summary>
    [Test]
    public async Task TestBurningItemInFoamGoesOut([Values(true, false)] bool blanketed)
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var flammableSystem = entityManager.System<FlammableSystem>();

        var testMap = await CreateBreathableTestMap();
        var gridUid = testMap.Grid.Owner;
        EntityUid flammableUid = default;

        await server.WaitPost(() =>
        {
            FloorRow(testMap);
            if (blanketed)
                FillBlanket(testMap.Tile);

            flammableUid = entityManager.SpawnEntity(_flammablePrototype, new EntityCoordinates(gridUid, 4.5f, 0.5f));
            flammableSystem.AdjustFireStacks(flammableUid, 5f, ignite: true);
            Assert.That(entityManager.GetComponent<FlammableComponent>(flammableUid).OnFire, "Test flammable did not catch fire.");
        });
        await server.WaitRunTicks(5);

        await server.WaitPost(() => entityManager.System<SharedTransformSystem>().SetCoordinates(flammableUid, new EntityCoordinates(gridUid, 0.5f, 0.5f)));
        await server.WaitRunTicks(TicksPerSecond * 2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.HasComponent<KsInFoamComponent>(flammableUid), Is.EqualTo(blanketed),
                blanketed ? "The item on the blanket is not in foam." : "The item is in foam with no blanket.");
            Assert.That(entityManager.GetComponent<FlammableComponent>(flammableUid).OnFire, Is.Not.EqualTo(blanketed),
                blanketed ? "The item kept burning in foam." : "The item went out without a blanket, so the blanket case proves nothing.");
        });
    }

    /// <summary>
    ///     Foam doesn't put out something burning in it while any chemfire burns on the same tile: here one foam can't
    ///         touch. Once the tile's fires are all out (an ordinary one, which the foam smothers), it does.
    /// </summary>
    [Test]
    public async Task TestBurningItemInFoamOverAChemicalFireKeepsBurning([Values(true, false)] bool untouchableFire)
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var flammableSystem = entityManager.System<FlammableSystem>();
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        var testMap = await CreateBreathableTestMap();
        var gridUid = testMap.Grid.Owner;
        var tile = testMap.Tile.GridIndices;
        EntityUid flammableUid = default;

        await server.WaitPost(() =>
        {
            FloorRow(testMap);
            FillBlanket(testMap.Tile);

            chemicalFireSystem.SpawnChemicalFire(ChemicalFirePrototype, (gridUid, null), tile, duration: TimeSpan.FromSeconds(30d));
            if (untouchableFire)
                chemicalFireSystem.SpawnChemicalFire(KlovnideFirePrototype, (gridUid, null), tile, duration: TimeSpan.FromSeconds(30d));

            flammableUid = entityManager.SpawnEntity(_flammablePrototype, new EntityCoordinates(gridUid, 4.5f, 0.5f));
            flammableSystem.AdjustFireStacks(flammableUid, 5f, ignite: true);
            Assert.That(entityManager.GetComponent<FlammableComponent>(flammableUid).OnFire, "Test flammable did not catch fire.");
        });
        await server.WaitRunTicks(5);

        await server.WaitPost(() => MoveAwake(flammableUid, new EntityCoordinates(gridUid, 0.5f, 0.5f)));
        await server.WaitRunTicks(TicksPerSecond * 2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.HasComponent<KsInFoamComponent>(flammableUid), "The item on the blanket is not in foam.");
            Assert.That(entityManager.GetComponent<FlammableComponent>(flammableUid).OnFire, Is.EqualTo(untouchableFire),
                untouchableFire
                    ? "The foam put the item out while a chemfire burned on its tile."
                    : "The item kept burning in foam once the tile's fire was smothered, so the other case proves nothing.");
        });
    }

    /// <summary>
    ///     Moves something and wakes it, as walking would: physics only updates the contacts of a body that is awake.
    /// </summary>
    private void MoveAwake(EntityUid uid, EntityCoordinates coordinates)
    {
        Pair.Server.EntMan.System<SharedTransformSystem>().SetCoordinates(uid, coordinates);
        Pair.Server.EntMan.System<SharedPhysicsSystem>().WakeBody(uid);
    }

    private EntityUid LayBlanket(TestMapData testMap, Vector2i tile)
    {
        var blanketSystem = Pair.Server.EntMan.System<KsFoamBlanketSystem>();
        var tileRef = Pair.Server.EntMan.System<SharedMapSystem>().GetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, tile);

        Assert.That(blanketSystem.Deploy(BlanketPrototype, tileRef, 1f), Is.GreaterThan(0f), $"No blanket was laid on {tile}.");
        Assert.That(blanketSystem.TryGetBlanket(testMap.Grid.Owner, tile, out var blanket));
        return blanket.Owner;
    }

    /// <summary>
    ///     Nothing puts a suspended klovnide-trioxide fire out: not an extinguisher's water, nor a blanket laid over it,
    ///         and a blanket doesn't keep a new one from starting. An ordinary chemfire on the same tile goes out, which
    ///         shows each of these reached the tile.
    /// </summary>
    [Test]
    public async Task TestKlovnideTrioxideFireCannotBePutOut()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        var testMap = await CreateBreathableTestMap();
        var gridUid = testMap.Grid.Owner;
        var tile = testMap.Tile.GridIndices;

        EntityUid klovnideFireUid = default;
        EntityUid ordinaryFireUid = default;

        await server.WaitAssertion(() =>
        {
            klovnideFireUid = chemicalFireSystem.SpawnChemicalFire(KlovnideFirePrototype, (gridUid, null), tile, duration: TimeSpan.FromSeconds(30d))!.Value.Owner;
            ordinaryFireUid = chemicalFireSystem.SpawnChemicalFire(ChemicalFirePrototype, (gridUid, null), tile, duration: TimeSpan.FromSeconds(30d))!.Value.Owner;
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            entityManager.System<PuddleSystem>().DoTileReactions(testMap.Tile, new Solution(WaterReagent.Id, FixedPoint2.New(100)));
        });

        // Putting a fire out queues its deletion.
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entityManager.EntityExists(ordinaryFireUid), Is.False, "The water did not put out the ordinary chemfire.");
                Assert.That(entityManager.EntityExists(klovnideFireUid), "The water put out the klovnide-trioxide fire.");
            }

            ordinaryFireUid = chemicalFireSystem.SpawnChemicalFire(ChemicalFirePrototype, (gridUid, null), tile, duration: TimeSpan.FromSeconds(30d))!.Value.Owner;
            FillBlanket(testMap.Tile);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entityManager.EntityExists(ordinaryFireUid), Is.False, "The blanket did not put out the ordinary chemfire.");
                Assert.That(entityManager.EntityExists(klovnideFireUid), "The blanket put out the klovnide-trioxide fire.");
            }

            entityManager.DeleteEntity(klovnideFireUid);
            Assert.That(chemicalFireSystem.SpawnChemicalFire(KlovnideFirePrototype, (gridUid, null), tile), Is.Not.Null,
                "The blanket kept a klovnide-trioxide fire from starting.");
        });
    }

    /// <summary>
    ///     A suspended klovnide-trioxide puddle's fire can't be put out, and it does not burn its puddle away either: both
    ///         are still there long after it lit.
    /// </summary>
    [Test]
    public async Task TestKlovnideTrioxidePuddleBurnsOn()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        var testMap = await CreateBreathableTestMap();
        var gridUid = testMap.Grid.Owner;
        var tile = testMap.Tile.GridIndices;

        EntityUid puddleUid = default;
        await server.WaitAssertion(() => SpillPuddle(testMap, KlovnideReagent, 3f, Atmospherics.T20C, out puddleUid));

        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
            Assert.That(chemicalFireSystem.TryGetChemicalFire((gridUid, null), tile, KlovnideFirePrototype.Id, out _),
                "The puddle never lit, so its burning on proves nothing."));

        await server.WaitRunTicks(TicksPerSecond * 10);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entityManager.EntityExists(puddleUid) && !entityManager.IsQueuedForDeletion(puddleUid), Is.True,
                    "The puddle burnt away.");
                Assert.That(chemicalFireSystem.TryGetChemicalFire((gridUid, null), tile, KlovnideFirePrototype.Id, out _), Is.True,
                    "The fire went out.");
            }
        });
    }

    /// <summary>
    ///     The partial adding boiling to Water applied, and left upstream's tile reactions in place.
    /// </summary>
    [Test]
    public async Task TestWaterPartialApplied()
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            var water = SProtoMan.Index(WaterReagent);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(water.KsTileEffects.OfType<BoilTileEffect>().Count(), Is.EqualTo(1), "Water does not boil.");
                Assert.That(water.TileReactions.OfType<ExtinguishTileReaction>().Any(), "Water lost upstream's extinguish reaction.");
                Assert.That(water.TileReactions.OfType<SpillIfPuddlePresentTileReaction>().Any(), "Water lost upstream's spill reaction.");
            }
        });
    }
}
