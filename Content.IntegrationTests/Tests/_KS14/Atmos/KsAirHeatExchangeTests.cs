using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.Atmos;
using Content.Server._KS14.Fluidmos.Ice;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Fluidmos.Ice;
using Content.Shared._KS14.Temperature;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     Containers, puddles and ice exchange heat with whatever is around them: tile air, a map's immutable air, vacuum,
///         or nothing.
/// </summary>
[TestOf(typeof(KsAirHeatExchangeSystem))]
public sealed class KsAirHeatExchangeTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestAirExchangeBeaker
  components:
  - type: Solution
    id: beaker
    solution:
      maxVol: 1000
  - type: KsCookOff
    solution: beaker
    radiatingArea: 0.5

- type: reagent
  id: KsTestAirExchangeInert
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
  specificHeat: 1
";

    private readonly EntProtoId _beakerPrototype = "KsTestAirExchangeBeaker";
    private static readonly EntProtoId WallPrototype = "WallSolid";
    private readonly ProtoId<ReagentPrototype> _inertReagent = "KsTestAirExchangeInert";
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> CarbonDioxideReagent = "CarbonDioxide";

    private const string BeakerSolution = "beaker";
    private const float StartTemperature = 300f;
    private const float MapAirTemperature = 200f;
    private const float CarbonDioxideTemperature = 194f;
    private const float Seconds = 2f;
    private const int Units = 100;
    private const int TicksPerSecond = 30;

    private Entity<SolutionComponent> SpawnBeaker(EntityCoordinates coordinates)
    {
        return FillBeaker(Pair.Server.EntMan.SpawnEntity(_beakerPrototype, coordinates));
    }

    private Entity<SolutionComponent> SpawnBeaker(MapCoordinates coordinates)
    {
        return FillBeaker(Pair.Server.EntMan.SpawnEntity(_beakerPrototype, coordinates));
    }

    private Entity<SolutionComponent> FillBeaker(EntityUid beakerUid)
    {
        var solutionContainerSystem = Pair.Server.System<SharedSolutionContainerSystem>();
        Assert.That(solutionContainerSystem.TryGetSolution(beakerUid, BeakerSolution, out var solutionEntity, out _));
        solutionContainerSystem.TryAddSolution(solutionEntity!.Value, new Solution(_inertReagent.Id, FixedPoint2.New(Units)));
        solutionContainerSystem.SetTemperature(solutionEntity.Value, StartTemperature);
        return solutionEntity.Value;
    }

    private GasMixture MakeAir(float temperature)
    {
        var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = temperature };
        mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
        mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
        return mixture;
    }

    /// <summary>
    ///     The exchange the beaker's solution should get through <paramref name="conductance"/> against a bath, from the
    ///         same exact step the system takes.
    /// </summary>
    private static float ExpectedAgainstBath(float capacity, float bathTemperature, float conductance, float area, float emissivity)
    {
        var total = conductance + KsThermalTransfer.RadiativeConductance(StartTemperature, bathTemperature, area, emissivity);
        return StartTemperature + KsThermalTransfer.ExchangeWithReservoirQuery(capacity, StartTemperature, bathTemperature, total, Seconds) / capacity;
    }

    [Test]
    public async Task ABeakerLoosePastAnyGridTakesTheMapsAir([Values(true, false)] bool radiates)
    {
        var server = Pair.Server;
        if (!radiates)
            await OverrideCVar(Side.Server, KsCCVars.AtmosPuddleEmissivity, 0f);

        var testMap = await Pair.CreateTestMap();
        Entity<SolutionComponent> beaker = default;

        await server.WaitPost(() =>
        {
            server.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, MakeAir(MapAirTemperature));
            beaker = SpawnBeaker(new MapCoordinates(new Vector2(500f, 500f), testMap.MapId));
        });

        await server.WaitAssertion(() =>
        {
            var heat = server.System<KsAirHeatExchangeSystem>().ExchangeWithSurroundings(beaker.Owner,
                beaker.Comp.Solution.GetHeatCapacity(server.ProtoMan), StartTemperature, conductance: 10f, radiatingArea: 0.5f, Seconds);

            var capacity = beaker.Comp.Solution.GetHeatCapacity(server.ProtoMan);
            var emissivity = radiates ? server.CfgMan.GetCVar(KsCCVars.AtmosPuddleEmissivity) : 0f;
            var expected = ExpectedAgainstBath(capacity, MapAirTemperature, 10f, 0.5f, emissivity);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(server.EntMan.GetComponent<TransformComponent>(beaker.Owner).GridUid, Is.Null, "The beaker landed on a grid, so this proves nothing.");
                Assert.That(StartTemperature + heat / capacity, Is.EqualTo(expected).Within(0.01f), "The map's air didn't take the exchange a bath should.");
                Assert.That(heat, Is.LessThan(0f), "A warm beaker didn't lose heat to colder map air.");
            }
        });
    }

    /// <summary>
    ///     Lattice over a map's atmosphere has a tile, but that tile's air is the map's own, immutable.
    /// </summary>
    [Test]
    public async Task ABeakerOnLatticeTakesTheMapsAirAndLeavesItUnchanged()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        Entity<SolutionComponent> beaker = default;

        await server.WaitPost(() =>
        {
            var lattice = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Lattice"].TileId);
            server.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, MakeAir(MapAirTemperature));
            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
            server.System<SharedMapSystem>().SetTile(testMap.Grid.Owner, testMap.Grid.Comp, testMap.Tile.GridIndices, lattice);
        });
        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitPost(() => beaker = SpawnBeaker(testMap.GridCoords));
        await server.WaitRunTicks(TicksPerSecond * 4);

        await server.WaitAssertion(() =>
        {
            var air = server.System<AtmosphereSystem>().GetTileMixture(testMap.Grid.Owner, testMap.MapUid, testMap.Tile.GridIndices);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(air, Is.Not.Null);
                Assert.That(air!.Immutable, "The lattice's air isn't the map's, so this proves nothing.");
                Assert.That(air.Temperature, Is.EqualTo(MapAirTemperature), "A bath changed temperature.");
                Assert.That(beaker.Comp.Solution.Temperature, Is.LessThan(StartTemperature - 1f), "The beaker ignored the map's air.");
                Assert.That(beaker.Comp.Solution.Temperature, Is.GreaterThan(MapAirTemperature), "The beaker went past the air it cooled towards.");
            }
        });
    }

    [Test]
    public async Task AColdBeakerWarmsUnderHotMapAir()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        Entity<SolutionComponent> beaker = default;

        await server.WaitPost(() =>
        {
            server.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, MakeAir(StartTemperature + 200f));
            beaker = SpawnBeaker(testMap.GridCoords);
        });
        await server.WaitRunTicks(TicksPerSecond * 4);

        await server.WaitAssertion(() => Assert.That(beaker.Comp.Solution.Temperature, Is.GreaterThan(StartTemperature + 1f),
            "A beaker didn't warm under hotter map air."));
    }

    /// <summary>
    ///     With nothing to conduct to, only radiation moves heat, so turning radiation off stops it entirely.
    /// </summary>
    [Test]
    public async Task VacuumOnlyTakesRadiation([Values(true, false)] bool radiates)
    {
        var server = Pair.Server;
        if (!radiates)
            await OverrideCVar(Side.Server, KsCCVars.AtmosPuddleEmissivity, 0f);

        var testMap = await Pair.CreateTestMap();
        Entity<SolutionComponent> beaker = default;

        await server.WaitPost(() => beaker = SpawnBeaker(testMap.GridCoords));

        await server.WaitAssertion(() =>
        {
            var capacity = beaker.Comp.Solution.GetHeatCapacity(server.ProtoMan);
            var heat = server.System<KsAirHeatExchangeSystem>().ExchangeWithSurroundings(beaker.Owner, capacity, StartTemperature,
                conductance: 1000f, radiatingArea: 0.5f, Seconds);

            var emissivity = radiates ? server.CfgMan.GetCVar(KsCCVars.AtmosPuddleEmissivity) : 0f;
            var expected = ExpectedAgainstBath(capacity, Atmospherics.TCMB, 0f, 0.5f, emissivity);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(StartTemperature + heat / capacity, Is.EqualTo(expected).Within(0.01f), "Vacuum didn't take radiation alone.");
                if (radiates)
                    Assert.That(heat, Is.LessThan(0f), "Nothing radiated.");
                else
                    Assert.That(heat, Is.Zero, "Heat left through vacuum with no radiation.");
            }
        });
    }

    /// <summary>
    ///     Every joule the air gains the beaker loses, with radiation on top of conduction.
    /// </summary>
    [Test]
    public async Task TileAirTakesWhatTheBeakerGives()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var atmosphereSystem = server.System<AtmosphereSystem>();
        Entity<SolutionComponent> beaker = default;

        await server.WaitPost(() => server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner));
        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitPost(() => beaker = SpawnBeaker(testMap.GridCoords));

        await server.WaitAssertion(() =>
        {
            var air = atmosphereSystem.GetTileMixture(testMap.Grid.Owner, testMap.MapUid, testMap.Tile.GridIndices)!;
            Assert.That(air.Immutable, Is.False, "The tile has no air of its own, so this proves nothing.");
            air.Clear();
            air.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            air.Temperature = MapAirTemperature;

            var airCapacity = atmosphereSystem.GetHeatCapacity(air, applyScaling: true);
            var heat = server.System<KsAirHeatExchangeSystem>().ExchangeWithSurroundings(beaker.Owner,
                beaker.Comp.Solution.GetHeatCapacity(server.ProtoMan), StartTemperature, conductance: 10f, radiatingArea: 0.5f, Seconds);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(heat, Is.LessThan(0f), "A warm beaker lost nothing to colder air.");
                Assert.That((air.Temperature - MapAirTemperature) * airCapacity, Is.EqualTo(-heat).Within(MathF.Abs(heat) * 0.01f),
                    "The air didn't take what the beaker gave.");
            }
        });
    }

    /// <summary>
    ///     Air that isn't there isn't vacuum: a tile sealed by a wall has no mixture, and exchanges nothing.
    /// </summary>
    [Test]
    public async Task ASealedTileExchangesNothing()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        Entity<SolutionComponent> beaker = default;

        await server.WaitPost(() =>
        {
            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
            server.EntMan.SpawnEntity(WallPrototype, testMap.GridCoords);
        });
        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            beaker = SpawnBeaker(testMap.GridCoords);
            var air = server.System<AtmosphereSystem>().GetTileMixture(testMap.Grid.Owner, testMap.MapUid, testMap.Tile.GridIndices);
            Assert.That(air, Is.Null, "The wall's tile has air, so this proves nothing.");

            var heat = server.System<KsAirHeatExchangeSystem>().ExchangeWithSurroundings(beaker.Owner,
                beaker.Comp.Solution.GetHeatCapacity(server.ProtoMan), StartTemperature, conductance: 10f, radiatingArea: 0.5f, Seconds);

            Assert.That(heat, Is.Zero, "A sealed tile was treated as vacuum.");
        });
    }

    /// <summary>
    ///     Nullspace has no air either, though asking for some answers with space's.
    /// </summary>
    [Test]
    public async Task NullspaceExchangesNothing()
    {
        var server = Pair.Server;
        Entity<SolutionComponent> beaker = default;

        await server.WaitAssertion(() =>
        {
            beaker = SpawnBeaker(MapCoordinates.Nullspace);
            var heat = server.System<KsAirHeatExchangeSystem>().ExchangeWithSurroundings(beaker.Owner,
                beaker.Comp.Solution.GetHeatCapacity(server.ProtoMan), StartTemperature, conductance: 10f, radiatingArea: 0.5f, Seconds);

            Assert.That(heat, Is.Zero, "An entity in nullspace exchanged heat with something.");
        });
    }

    /// <summary>
    ///     A grid with no atmosphere of its own, under a map's, is what a planet's surface is.
    /// </summary>
    [Test]
    public async Task APuddleOnAnAirlessGridCoolsTowardsTheMapsAir()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        Entity<SolutionComponent> puddle = default;

        await server.WaitPost(() =>
        {
            server.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, MakeAir(MapAirTemperature));
            Assert.That(server.System<PuddleSystem>().TrySpillAt(testMap.Tile,
                new Solution(_inertReagent.Id, FixedPoint2.New(20)) { Temperature = StartTemperature }, out _, sound: false, tileReact: false));
            Assert.That(server.System<PuddleSystem>().TryGetCachedPuddle(testMap.Tile, out Entity<Content.Shared.Fluids.Components.PuddleComponent> cached));
            puddle = cached.Comp.Solution!.Value;
        });
        await server.WaitRunTicks(TicksPerSecond * 3);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(server.EntMan.HasComponent<GridAtmosphereComponent>(testMap.Grid.Owner), Is.False, "The grid has its own air, so this proves nothing.");
                Assert.That(puddle.Comp.Solution.Temperature, Is.LessThan(StartTemperature - 1f), "The puddle ignored the map's air.");
                Assert.That(puddle.Comp.Solution.Temperature, Is.GreaterThan(MapAirTemperature), "The puddle went past the air it cooled towards.");
            }
        });
    }

    /// <summary>
    ///     A puddle radiating in vacuum boils until it's too cold to, and then it can only freeze. It used to be held liquid,
    ///         at any temperature, for being below the triple point.
    /// </summary>
    [Test]
    public async Task ColdWaterInVacuumFreezes()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitPost(() => Assert.That(server.System<PuddleSystem>().TrySpillAt(testMap.Tile,
            new Solution(WaterReagent.Id, FixedPoint2.New(20)) { Temperature = 150f }, out _, sound: false, tileReact: false)));
        await server.WaitRunTicks(TicksPerSecond * 4);

        await server.WaitAssertion(() => Assert.That(server.System<KsIceSheetSystem>().TryGetIceSheet(testMap.Grid.Owner, testMap.Tile.GridIndices, out _),
            "Water far below its boiling floor in vacuum never froze."));
    }

    /// <summary>
    ///     Carbon dioxide at one atmosphere is below its triple point, with no liquid to freeze: it goes straight to gas,
    ///         however cold it lands. Only air that can't take the gas freezes what's below its boiling point.
    /// </summary>
    [Test]
    public async Task ColdCarbonDioxideInAirStillDoesNotFreeze()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var entityManager = server.EntMan;

        await server.WaitPost(() => entityManager.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner));
        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            var air = server.System<AtmosphereSystem>().GetTileMixture(testMap.Grid.Owner, testMap.MapUid, testMap.Tile.GridIndices)!;
            Assert.That(air.Immutable, Is.False, "The tile can't take gas, so this proves nothing.");
            air.Clear();
            air.Temperature = Atmospherics.T20C;
            air.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);

            Assert.That(server.System<PuddleSystem>().TrySpillAt(testMap.Tile,
                new Solution(CarbonDioxideReagent.Id, FixedPoint2.New(20)) { Temperature = CarbonDioxideTemperature },
                out _, sound: false, tileReact: false));
        });
        await server.WaitRunTicks(TicksPerSecond * 2);

        await server.WaitAssertion(() => Assert.That(server.System<KsIceSheetSystem>().TryGetIceSheet(testMap.Grid.Owner, testMap.Tile.GridIndices, out _),
            Is.False, "Carbon dioxide below its triple point formed ice."));
    }

    /// <summary>
    ///     A sealed tile isn't vacuum for ice either: nothing leaves it.
    /// </summary>
    [Test]
    public async Task IceOnASealedTileDoesNotSublime()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var testMap = await Pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            entityManager.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
            entityManager.SpawnEntity(WallPrototype, testMap.GridCoords);
        });
        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitPost(() => server.System<KsIceSheetSystem>().AddIce(testMap.GridCoords,
            new Solution(WaterReagent.Id, FixedPoint2.New(10)) { Temperature = 260f }));
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<AtmosphereSystem>().GetTileMixture(testMap.Grid.Owner, testMap.MapUid, testMap.Tile.GridIndices), Is.Null,
                "The wall's tile has air, so this proves nothing.");
            Assert.That(server.System<KsIceSheetSystem>().TryGetIceSheet(testMap.Grid.Owner, testMap.Tile.GridIndices, out var sheet));
            var solutionContainerSystem = server.System<SharedSolutionContainerSystem>();
            Assert.That(solutionContainerSystem.TryGetSolution(sheet.Owner, KsIceSheetComponent.SolutionName, out _, out var solution));
            var volumeBefore = solution.Volume;

            server.System<KsIceSheetSystem>().UpdateHeatExchange(1f, conductance: 0f, referenceVolume: 20f);
            Assert.That(solution.Volume, Is.EqualTo(volumeBefore), "Ice sublimed out of a sealed tile.");
        });
    }

    /// <summary>
    ///     A map's air takes no gas, so ice warmed by it melts rather than subliming into nothing.
    /// </summary>
    [Test]
    public async Task IceUnderImmutableAirDoesNotSublime()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        const float iceTemperature = 260f;
        FixedPoint2 volumeBefore = default;

        await server.WaitPost(() =>
        {
            server.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, MakeAir(iceTemperature));
            server.System<KsIceSheetSystem>().AddIce(testMap.GridCoords, new Solution(WaterReagent.Id, FixedPoint2.New(10)) { Temperature = iceTemperature });
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<KsIceSheetSystem>().TryGetIceSheet(testMap.Grid.Owner, testMap.Tile.GridIndices, out var sheet));
            var solutionContainerSystem = server.System<SharedSolutionContainerSystem>();
            Assert.That(solutionContainerSystem.TryGetSolution(sheet.Owner, KsIceSheetComponent.SolutionName, out _, out var solution));
            volumeBefore = solution!.Volume;

            server.System<KsIceSheetSystem>().UpdateHeatExchange(1f, conductance: 0f, referenceVolume: 20f);
            Assert.That(solution.Volume, Is.EqualTo(volumeBefore), "Ice sublimed into air that can't take the gas.");
        });
    }
}
