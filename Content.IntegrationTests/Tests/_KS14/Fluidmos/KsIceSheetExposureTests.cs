#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.Fluidmos;
using Content.Server._KS14.Fluidmos.Ice;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Fluidmos;
using Content.Shared._KS14.Fluidmos.Ice;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     What becomes of a tile's puddle and ice: supercooled liquid freezes onto the ice already on its tile, and both
///         dissipate in vacuum and where a map's own air reaches them when that is turned on.
/// </summary>
[TestOf(typeof(KsFluidDissipationSystem))]
public sealed class KsIceSheetExposureTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";

    private const float ColdTemperature = 253.15f;

    /// <summary>
    ///     The test's own dissipation rate, in units per second.
    /// </summary>
    private const float TestDissipationRate = 5f;

    private const float TestVolume = 10f;

    // Deliberately NOT static: it only exists in this test's YAML, and the YAML linter validates static ids.
    private readonly ProtoId<ReagentPrototype> _inertReagent = "KsTestIceExposureInert";

    // No boiling or melting point and no gas: nothing but dissipation can take it off a tile.
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestIceExposureInert
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
";

    [SetUp]
    public async Task TurnOffRadiationAndDissipation()
    {
        await OverrideCVar(Side.Server, KsCCVars.AtmosPuddleEmissivity, 0f);
        await OverrideCVar(Side.Server, KsCCVars.AtmosPuddleVacuumDissipationRate, 0f);
        await OverrideCVar(Side.Server, KsCCVars.AtmosPuddleExposedDissipationRate, 0f);
    }

    private static GasMixture MakeAir(float temperature)
    {
        var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = temperature };
        mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
        mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
        return mixture;
    }

    /// <param name="mapAir">Whether the map has air of its own, rather than being space.</param>
    /// <param name="gridAir">Whether the grid has an atmosphere, so its tile has air of its own rather than the map's.</param>
    private async Task<TestMapData> CreateTestMap(bool mapAir, bool gridAir, float temperature = Atmospherics.T20C)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            if (mapAir)
                server.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, MakeAir(temperature));

            if (gridAir)
                server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
        });

        await server.WaitRunTicks(30);
        return testMap;
    }

    private Entity<SolutionComponent> SpillPuddle(TestMapData testMap, ProtoId<ReagentPrototype> reagent, float volume, float temperature)
    {
        var entityManager = Pair.Server.EntMan;
        var spilled = new Solution(reagent, FixedPoint2.New(volume)) { Temperature = temperature };
        Assert.That(entityManager.System<PuddleSystem>().TrySpillAt(testMap.Tile, spilled, out var puddleUid, sound: false, tileReact: false),
            "The puddle failed to spill.");

        var puddleComponent = entityManager.GetComponent<PuddleComponent>(puddleUid);
        Assert.That(entityManager.System<SharedSolutionContainerSystem>()
            .ResolveSolution(puddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution, out _));
        var solutionEntity = puddleComponent.Solution!.Value;
        solutionEntity.Comp.Solution.Temperature = temperature;
        return solutionEntity;
    }

    private FixedPoint2 PuddleVolume(TestMapData testMap)
    {
        var server = Pair.Server;
        if (!server.System<PuddleSystem>().TryGetCachedPuddle(testMap.Grid.Owner, testMap.Tile.GridIndices, out var puddle) ||
            server.EntMan.IsQueuedForDeletion(puddle.Owner) ||
            !server.System<SharedSolutionContainerSystem>().ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out var solution))
            return FixedPoint2.Zero;

        return solution.Volume;
    }

    private FixedPoint2 IceVolume(TestMapData testMap)
    {
        var server = Pair.Server;
        if (!server.System<KsIceSheetSystem>().TryGetIceSheet(testMap.Grid.Owner, testMap.Tile.GridIndices, out var sheet) ||
            server.EntMan.IsQueuedForDeletion(sheet.Owner) ||
            !server.System<SharedSolutionContainerSystem>().TryGetSolution(sheet.Owner, KsIceSheetComponent.SolutionName, out _, out var solution))
            return FixedPoint2.Zero;

        return solution.Volume;
    }

    private Solution IceSolution(TestMapData testMap)
    {
        var server = Pair.Server;
        Assert.That(server.System<KsIceSheetSystem>().TryGetIceSheet(testMap.Grid.Owner, testMap.Tile.GridIndices, out var sheet),
            "The tile has no ice sheet.");
        Assert.That(server.System<SharedSolutionContainerSystem>().TryGetSolution(sheet.Owner, KsIceSheetComponent.SolutionName, out _, out var solution));
        return solution!;
    }

    /// <summary>
    ///     Puts a puddle and a sheet of the inert reagent on the tile, then runs for long enough that both would be gone
    ///         three times over at the test's rate, stopping early once they are.
    /// </summary>
    /// <returns>Whether both went.</returns>
    private async Task<bool> RunUntilDissipated(TestMapData testMap)
    {
        var server = Pair.Server;

        await server.WaitPost(() =>
        {
            SpillPuddle(testMap, _inertReagent, TestVolume, Atmospherics.T20C);
            server.System<KsIceSheetSystem>().AddIce(testMap.GridCoords,
                new Solution(_inertReagent, FixedPoint2.New(TestVolume)) { Temperature = Atmospherics.T20C });
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(PuddleVolume(testMap), Is.GreaterThan(FixedPoint2.Zero), "The puddle never spilled, so this proves nothing.");
            Assert.That(IceVolume(testMap), Is.GreaterThan(FixedPoint2.Zero), "The ice never formed, so this proves nothing.");
        });

        var ticksPerSecond = (int)server.ResolveDependency<IGameTiming>().TickRate;
        var seconds = (int)MathF.Ceiling(3f * TestVolume / TestDissipationRate) + 2;
        for (var second = 0; second < seconds; second++)
        {
            await server.WaitRunTicks(ticksPerSecond);

            var gone = false;
            await server.WaitPost(() => gone = PuddleVolume(testMap) == FixedPoint2.Zero && IceVolume(testMap) == FixedPoint2.Zero);
            if (gone)
                return true;
        }

        return false;
    }

    [Test]
    public async Task PuddleAndIceDissipateInVacuumOnlyWhenTurnedOn([Values] bool turnedOn)
    {
        if (turnedOn)
            await OverrideCVar(Side.Server, KsCCVars.AtmosPuddleVacuumDissipationRate, TestDissipationRate);

        // Space, on a grid with no air of its own: the tile reads as vacuum.
        var testMap = await CreateTestMap(mapAir: false, gridAir: false);
        var gone = await RunUntilDissipated(testMap);

        Assert.That(gone, Is.EqualTo(turnedOn), turnedOn
            ? "A puddle and ice in vacuum did not dissipate."
            : "A puddle and ice in vacuum dissipated with dissipation turned off.");
    }

    /// <summary>
    ///     A grid with no air of its own on a planet is open to the planet's air, and its puddles and ice dissipate. The
    ///         same grid with its own air is a sealed room under the planet, and nothing goes.
    /// </summary>
    [Test]
    public async Task PuddleAndIceDissipateOnlyWhereAMapsAirReachesThem(
        [Values] bool gridAir,
        [Values] bool turnedOn)
    {
        if (turnedOn)
            await OverrideCVar(Side.Server, KsCCVars.AtmosPuddleExposedDissipationRate, TestDissipationRate);

        // Vacuum dissipation on as well, so a sealed room is shown not to count as vacuum either.
        await OverrideCVar(Side.Server, KsCCVars.AtmosPuddleVacuumDissipationRate, TestDissipationRate);

        var testMap = await CreateTestMap(mapAir: true, gridAir: gridAir);
        var gone = await RunUntilDissipated(testMap);

        var expected = turnedOn && !gridAir;
        Assert.That(gone, Is.EqualTo(expected), expected
            ? "A puddle and ice open to a planet's air did not dissipate."
            : $"A puddle and ice dissipated though {(gridAir ? "their tile had air of its own" : "dissipation was turned off")}.");
    }

    /// <summary>
    ///     A puddle held below its melting point (in air too thin for it to freeze on its own, which boils it instead)
    ///         freezes onto the ice of the same reagent on its tile, as far as its heat deficit pays for, and warms to its
    ///         melting point. Without this, a spaced room keeps a supercooled puddle beside its ice for ever.
    /// </summary>
    [Test]
    public async Task SupercooledPuddleFreezesOntoTheIceOnItsTile()
    {
        var server = Pair.Server;
        var testMap = await CreateTestMap(mapAir: true, gridAir: true, ColdTemperature);
        Solution puddle = null!;

        await server.WaitPost(() =>
        {
            puddle = SpillPuddle(testMap, WaterReagent, TestVolume, ColdTemperature).Comp.Solution;
            server.System<KsIceSheetSystem>().AddIce(testMap.GridCoords,
                new Solution(WaterReagent, FixedPoint2.New(TestVolume / 2f)) { Temperature = ColdTemperature });
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(WaterReagent, out var water));
            var ice = IceSolution(testMap);

            // Both below the melting point and at one temperature, so no heat flows between them and only freezing moves.
            var temperature = water!.MeltingPoint - 5f;
            puddle.Temperature = temperature;
            ice.Temperature = temperature;

            // Saturated past what the ice's vapor pressure reaches, so none of it sublimes in the same pass.
            var air = server.System<AtmosphereSystem>().GetTileMixture(testMap.Grid.Owner, null, testMap.Tile.GridIndices, excite: true)!;
            var saturationPressure = KsPhaseMath.SaturationPressure(water.VaporizationEntropy, water.BoilingPoint, water.MeltingPoint);
            air.SetMoles(Gas.WaterVapor, saturationPressure * air.Volume / (Atmospherics.R * air.Temperature));

            var puddleBefore = (float)puddle.GetTotalPrototypeQuantity(WaterReagent);
            var iceBefore = (float)ice.GetTotalPrototypeQuantity(WaterReagent);
            var expectedFrozen = MathF.Min(puddleBefore, puddle.GetHeatCapacity(server.ProtoMan) * (water.MeltingPoint - temperature) / water.FusionHeat);
            Assert.That(expectedFrozen, Is.GreaterThan(0.1f), "Too little would freeze to tell, so this proves nothing.");

            // One phase pass with no heat exchange to the air.
            server.System<KsIceSheetSystem>().UpdateHeatExchange(1f, conductance: 0f, referenceVolume: KsFluidmos.PuddleReferenceVolume);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(puddleBefore - (float)puddle.GetTotalPrototypeQuantity(WaterReagent), Is.EqualTo(expectedFrozen).Within(0.02f),
                    "The supercooled puddle did not freeze onto the ice on its tile by its heat deficit.");
                Assert.That((float)ice.GetTotalPrototypeQuantity(WaterReagent) - iceBefore, Is.EqualTo(expectedFrozen).Within(0.02f),
                    "What froze out of the puddle did not go into the ice.");
                Assert.That(puddle.Temperature, Is.EqualTo(water.MeltingPoint).Within(0.05f),
                    "The puddle did not warm to its melting point with the heat its freezing released.");
            }
        });
    }
}
