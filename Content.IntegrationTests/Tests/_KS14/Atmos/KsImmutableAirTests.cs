#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.TileEffects;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;
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

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     A map's air and vacuum take whatever is put into them and stay as they were: matter and energy are lost to them,
///         rather than nothing happening at all.
/// </summary>
[TestOf(typeof(KsTileEffectAtmosSystem))]
public sealed class KsImmutableAirTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: ChemicalFire
  id: KsTestImmutableAirFire
  components:
  - type: ChemicalFire
    duration: 120
";

    // Deliberately NOT static: it only exists in the test YAML above, and the YAML linter validates static ids.
    private readonly EntProtoId _firePrototype = "KsTestImmutableAirFire";

    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";

    private const int TicksPerSecond = 30;
    private const float Units = 5f;
    private const float MolesPerUnit = 0.1f;

    /// <summary>
    ///     A grid with no atmosphere of its own, under a map's air, or under vacuum when that is null.
    /// </summary>
    private async Task<TestMapData> CreateMap(GasMixture? air)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            if (air != null)
                server.EntMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, air);

            Assert.That(server.EntMan.HasComponent<GridAtmosphereComponent>(testMap.Grid.Owner), Is.False,
                "The grid has its own atmosphere, so this isn't testing the map's.");
        });

        return testMap;
    }

    private static GasMixture MakeAir(float temperature, bool oxygen = true)
    {
        var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = temperature };
        mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
        if (oxygen)
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);

        return mixture;
    }

    private GasMixture TileAir(TestMapData testMap)
    {
        var air = Pair.Server.System<KsTileEffectAtmosSystem>().GetTileAir(testMap.Grid.Owner, testMap.Tile.GridIndices);
        Assert.That(air, Is.Not.Null.And.Property(nameof(GasMixture.Immutable)).True, "The tile's air isn't immutable, so this proves nothing.");
        return air!;
    }

    [Test]
    public async Task PhaseChangeIsLostToImmutableAir([Values(true, false)] bool vacuum)
    {
        var server = Pair.Server;
        var testMap = await CreateMap(vacuum ? null : MakeAir(Atmospherics.T20C));

        await server.WaitAssertion(() =>
        {
            var air = TileAir(testMap);
            var (moles, temperature) = (air.TotalMoles, air.Temperature);

            var units = server.System<KsTileEffectAtmosSystem>().TryChangePhase(testMap.Grid.Owner, testMap.Tile.GridIndices, Gas.WaterVapor,
                Units, MolesPerUnit, sensibleEnergyPerUnit: 100f, latentEnergyPerUnit: 0f, minimumTemperature: 0f, displace: false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(units, Is.EqualTo(Units), "Immutable air did not take what was boiled into it.");
                Assert.That(air.TotalMoles, Is.EqualTo(moles), "The gas stayed in immutable air.");
                Assert.That(air.Temperature, Is.EqualTo(temperature), "Immutable air changed temperature.");
            }
        });
    }

    /// <summary>
    ///     Immutable air pays latent heat without cooling, but only if it's warm enough to pay at all: it must not sublime
    ///         something colder air couldn't, and vacuum has nothing to pay with.
    /// </summary>
    [Test]
    public async Task ImmutableAirOnlyPaysLatentHeatWhenWarmEnough([Values(true, false)] bool vacuum)
    {
        var server = Pair.Server;
        const float airTemperature = 250f;
        var testMap = await CreateMap(vacuum ? null : MakeAir(airTemperature));

        await server.WaitAssertion(() =>
        {
            float Change(float minimumTemperature) => server.System<KsTileEffectAtmosSystem>().TryChangePhase(testMap.Grid.Owner, testMap.Tile.GridIndices,
                Gas.WaterVapor, Units, MolesPerUnit, sensibleEnergyPerUnit: 0f, latentEnergyPerUnit: 500f, minimumTemperature, displace: false);

            var air = TileAir(testMap);
            var temperature = air.Temperature;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Change(airTemperature + 50f), Is.Zero, "Air colder than the reagent's minimum paid its latent heat.");
                Assert.That(Change(airTemperature - 50f), Is.EqualTo(vacuum ? 0f : Units), vacuum ? "Vacuum paid latent heat." : "A map's air wouldn't pay latent heat.");
                Assert.That(air.Temperature, Is.EqualTo(temperature), "Immutable air cooled.");
            }
        });
    }

    /// <summary>
    ///     On a tile with air of its own, the air still takes the gas and the heat, as it always did.
    /// </summary>
    [Test]
    public async Task MutableAirStillKeepsWhatItIsGiven()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitPost(() => server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner));
        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            var atmosphereTileSystem = server.System<KsTileEffectAtmosSystem>();
            var air = atmosphereTileSystem.GetTileAir(testMap.Grid.Owner, testMap.Tile.GridIndices);
            Assert.That(air, Is.Not.Null.And.Property(nameof(GasMixture.Immutable)).False, "The tile has no air of its own, so this proves nothing.");

            var moles = air!.TotalMoles;
            Assert.That(atmosphereTileSystem.TryAddGas(testMap.Grid.Owner, testMap.Tile.GridIndices, Gas.WaterVapor, Units, sensibleEnergy: 0f));
            Assert.That(air.TotalMoles, Is.EqualTo(moles + Units).Within(0.001f), "The tile's air did not take the gas.");
        });
    }

    /// <summary>
    ///     A puddle hotter than it boils under a map's air, or in vacuum, boils away rather than sitting there.
    /// </summary>
    [Test]
    public async Task HotPuddleBoilsAwayIntoImmutableAir([Values(true, false)] bool vacuum)
    {
        var server = Pair.Server;
        var testMap = await CreateMap(vacuum ? null : MakeAir(Atmospherics.T20C));
        const float volume = 20f;
        var moles = 0f;
        EntityUid puddleUid = default;

        await server.WaitPost(() =>
        {
            var boilingPoint = server.ProtoMan.Index(WaterReagent).BoilingPoint!.Value;
            moles = TileAir(testMap).TotalMoles;
            Assert.That(server.System<PuddleSystem>().TrySpillAt(testMap.Tile, new Solution(WaterReagent.Id, FixedPoint2.New(volume)) { Temperature = boilingPoint + 100f },
                out puddleUid, sound: false, tileReact: false));
        });

        await server.WaitRunTicks(TicksPerSecond * 3);

        await server.WaitAssertion(() =>
        {
            var puddleComponent = server.EntMan.GetComponent<PuddleComponent>(puddleUid);
            Assert.That(server.System<SharedSolutionContainerSystem>().ResolveSolution(puddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution, out var solution));

            using (Assert.EnterMultipleScope())
            {
                Assert.That((float)solution!.Volume, Is.LessThan(volume), "A hot puddle over immutable air did not boil.");
                Assert.That(TileAir(testMap).TotalMoles, Is.EqualTo(moles), "The gas stayed in immutable air.");
            }
        });
    }

    /// <summary>
    ///     A map's air with what a chemfire burns feeds it, and never runs out for it, so the fire burns on and the air stays
    ///         as it was.
    /// </summary>
    [Test]
    public async Task ChemfireBurnsUnderAMapsAirWithoutBurningThroughIt()
    {
        var server = Pair.Server;
        var testMap = await CreateMap(MakeAir(Atmospherics.T20C));
        var moles = 0f;
        Entity<ChemicalFireComponent> fire = default;

        await server.WaitAssertion(() =>
        {
            moles = TileAir(testMap).TotalMoles;
            var spawned = server.System<SharedChemicalFireSystem>().SpawnChemicalFire(_firePrototype, testMap.Tile);
            Assert.That(spawned, Is.Not.Null, "A chemfire couldn't start in a map's breathable air.");
            fire = spawned!.Value;
        });

        await server.WaitRunTicks(TicksPerSecond * 3);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(server.EntMan.EntityExists(fire.Owner), "A chemfire went out in a map's breathable air.");
                Assert.That(TileAir(testMap).TotalMoles, Is.EqualTo(moles), "A chemfire changed a map's air.");
            }
        });
    }

    /// <summary>
    ///     Immutable air is still real data: without what a chemfire burns, it can't start.
    /// </summary>
    [Test]
    public async Task ChemfireCannotStartWithoutWhatItBurns([Values(true, false)] bool vacuum)
    {
        var server = Pair.Server;
        var testMap = await CreateMap(vacuum ? null : MakeAir(Atmospherics.T20C, oxygen: false));

        await server.WaitAssertion(() => Assert.That(server.System<SharedChemicalFireSystem>().SpawnChemicalFire(_firePrototype, testMap.Tile), Is.Null,
            "A chemfire started with nothing to burn."));
    }
}
