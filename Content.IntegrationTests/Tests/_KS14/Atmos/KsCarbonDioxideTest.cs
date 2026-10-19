#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.TileEffects;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.TileEffects.Effects;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     Carbon dioxide from a CO2 extinguisher: it flashes to gas on the tiles it is sprayed on, cooling them and
///         shoving their air into the next tiles, and a puddle of it always boils off as gas in the end.
/// </summary>
[TestOf(typeof(KsVaporizeTileReaction))]
public sealed class KsCarbonDioxideTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestCarbonDioxideInertLiquid
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
";

    // Deliberately NOT static: it only exists in the test YAML above, and the YAML linter validates static ids.
    private readonly ProtoId<ReagentPrototype> _inertLiquidReagent = "KsTestCarbonDioxideInertLiquid";

    private static readonly ProtoId<ReagentPrototype> CarbonDioxideReagent = "CarbonDioxide";
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly EntProtoId ExtinguisherPrototype = "KsFireExtinguisherCarbonDioxide";
    private static readonly EntProtoId WallPrototype = "WallSolid";

    private const float SprayTemperature = 194f;
    /// <summary>
    ///     Where carbon dioxide sublimes, in kelvin: its reagent's boiling point, which is in Celsius.
    /// </summary>
    private float SublimationPoint()
    {
        var boilingPoint = SProtoMan.Index(CarbonDioxideReagent).BoilingPoint;
        Assert.That(boilingPoint, Is.Not.Null, "Carbon dioxide has no boiling point, so it never sublimes.");
        return boilingPoint!.Value + Atmospherics.T0C;
    }
    private const int TicksPerSecond = 30;

    private static readonly Vector2i Centre = new(0, 0);

    /// <summary>
    ///     A sealed 3x3 room of standard air, walled in so nothing leaks out, so the centre tile has four neighbours to
    ///         push air into and every mole stays countable.
    /// </summary>
    private async Task<TestMapData> CreateRoom()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var mapSystem = server.EntMan.System<SharedMapSystem>();
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -2; y <= 2; y++)
                {
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
                    if (Math.Abs(x) == 2 || Math.Abs(y) == 2)
                        server.EntMan.SpawnEntity(WallPrototype, new EntityCoordinates(testMap.Grid.Owner, x + 0.5f, y + 0.5f));
                }
            }

            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
        });

        await server.WaitRunTicks(5);

        await server.WaitPost(() =>
        {
            foreach (var tile in RoomTiles())
            {
                var mixture = TileMixture(testMap, tile);
                mixture.Clear();
                mixture.Temperature = Atmospherics.T20C;
                mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
                mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            }
        });

        await server.WaitRunTicks(5);
        return testMap;
    }

    private static IEnumerable<Vector2i> RoomTiles()
    {
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
                yield return new Vector2i(x, y);
        }
    }

    private GasMixture TileMixture(TestMapData testMap, Vector2i tile)
    {
        var mixture = Pair.Server.EntMan.System<AtmosphereSystem>().GetTileMixture((testMap.Grid.Owner, null, null), null, tile, excite: false);
        Assert.That(mixture, Is.Not.Null.And.Property(nameof(GasMixture.Immutable)).False, $"Tile {tile} has no air.");
        return mixture!;
    }

    private List<GasMixture> RoomMixtures(TestMapData testMap)
    {
        return new[] { Centre, new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) }
            .Select(tile => TileMixture(testMap, tile))
            .ToList();
    }

    private TileRef CentreTileRef(TestMapData testMap)
        => Pair.Server.EntMan.System<SharedMapSystem>().GetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, Centre);

    /// <summary>
    ///     Carbon dioxide's specific heat is what its gas holds per unit, so the gas comes out at the reagent's
    ///         temperature rather than far colder or hotter.
    /// </summary>
    [Test]
    public async Task SpecificHeatMatchesTheGasItBecomes()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var atmosphereSystem = Pair.Server.EntMan.System<AtmosphereSystem>();
            var reagent = SProtoMan.Index(CarbonDioxideReagent);
            var reaction = reagent.TileReactions.OfType<KsVaporizeTileReaction>().Single();
            var boil = reagent.KsTileEffects.OfType<BoilTileEffect>().Single();
            var gasHeatCapacity = atmosphereSystem.GasMolarHeatCapacities[(int)Gas.CarbonDioxide];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(reaction.MolesPerUnit, Is.EqualTo(boil.MolesPerUnit), "The spray and the puddle make different amounts of gas.");
                Assert.That(reagent.SpecificHeat, Is.EqualTo(reaction.MolesPerUnit!.Value * gasHeatCapacity).Within(0.01f),
                    "Carbon dioxide's specific heat does not match the gas a unit becomes.");
                Assert.That(SublimationPoint(), Is.LessThan(Atmospherics.T0C), "Carbon dioxide does not sublime below freezing.");
                Assert.That(boil.Sublimate && boil.Displace && reaction.Displace, "Carbon dioxide does not always turn to gas, or does not displace air.");
            }
        });
    }

    /// <summary>
    ///     Sprayed onto a hot tile, carbon dioxide turns to gas there and cools it, never below where it sublimes. Energy
    ///         is conserved across the tile and its neighbours, counting the latent heat that leaves with the gas.
    /// </summary>
    [Test]
    public async Task SprayCoolsTheAirAndConservesEnergy()
    {
        var testMap = await CreateRoom();

        await Pair.Server.WaitAssertion(() =>
        {
            var entityManager = Pair.Server.EntMan;
            var atmosphereSystem = entityManager.System<AtmosphereSystem>();
            var reagent = SProtoMan.Index(CarbonDioxideReagent);
            var reaction = reagent.TileReactions.OfType<KsVaporizeTileReaction>().Single();

            TileMixture(testMap, Centre).Temperature = 600f;
            var mixtures = RoomMixtures(testMap);
            var energyBefore = mixtures.Sum(mixture => atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true) * mixture.Temperature);

            const float offered = 20f;
            FixedPoint2 used;
            using (entityManager.System<PuddleSystem>().KsBeginTileReactionTemperature(SprayTemperature))
                used = reaction.TileReact(CentreTileRef(testMap), reagent, FixedPoint2.New(offered), entityManager, null);

            var energyAfter = mixtures.Sum(mixture => atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true) * mixture.Temperature);
            var expected = energyBefore + (float)used * reagent.SpecificHeat * SprayTemperature - (float)used * reaction.EnergyPerUnit;

            using (Assert.EnterMultipleScope())
            {
                Assert.That((float)used, Is.GreaterThan(0f).And.LessThan(offered), "The spray did not stop at what the air could pay for.");
                Assert.That(mixtures[0].Temperature, Is.LessThan(Atmospherics.FireMinimumTemperatureToExist),
                    "The spray did not cool the tile below what fire needs.");
                Assert.That(mixtures[0].Temperature, Is.GreaterThanOrEqualTo(SublimationPoint() - 0.5f), "The spray cooled the tile below where it sublimes.");
                Assert.That(energyAfter, Is.EqualTo(expected).Within(MathF.Abs(expected) * 1e-4f), "The spray did not conserve energy.");
            }
        });
    }

    /// <summary>
    ///     The gas pushes as much of the tile's air out into its neighbours as it adds: the oxygen on the tile drops, the
    ///         tile holds as many moles as before, and every mole is accounted for.
    /// </summary>
    [Test]
    public async Task SprayDisplacesTheAir()
    {
        var testMap = await CreateRoom();

        await Pair.Server.WaitAssertion(() =>
        {
            var entityManager = Pair.Server.EntMan;
            var reagent = SProtoMan.Index(CarbonDioxideReagent);
            var reaction = reagent.TileReactions.OfType<KsVaporizeTileReaction>().Single();

            var mixtures = RoomMixtures(testMap);
            var centre = mixtures[0];
            var oxygenBefore = centre.GetMoles(Gas.Oxygen);
            var centreMolesBefore = centre.TotalMoles;
            var molesBefore = mixtures.Sum(mixture => mixture.TotalMoles);

            FixedPoint2 used;
            using (entityManager.System<PuddleSystem>().KsBeginTileReactionTemperature(SprayTemperature))
                used = reaction.TileReact(CentreTileRef(testMap), reagent, FixedPoint2.New(3), entityManager, null);

            var added = (float)used * reaction.MolesPerUnit!.Value;
            using (Assert.EnterMultipleScope())
            {
                Assert.That((float)used, Is.GreaterThan(0f), "Nothing turned to gas.");
                // Its share of what was displaced: as many moles as the gas added, taken evenly from the air.
                Assert.That(centre.GetMoles(Gas.Oxygen), Is.EqualTo(oxygenBefore * (1f - added / centreMolesBefore)).Within(0.05f),
                    "The gas did not push the oxygen out.");
                Assert.That(centre.GetMoles(Gas.CarbonDioxide), Is.EqualTo(added).Within(0.01f), "The carbon dioxide was pushed out as well.");
                Assert.That(mixtures.Sum(mixture => mixture.TotalMoles), Is.EqualTo(molesBefore + added).Within(0.01f), "Moles went missing.");
                Assert.That(centre.TotalMoles, Is.EqualTo(centreMolesBefore).Within(0.01f), "The gas added to the tile's air instead of displacing it.");
            }
        });
    }

    /// <summary>
    ///     A puddle of carbon dioxide colder than it sublimes at, in room air, still all turns to gas, taking the heat
    ///         from the air.
    /// </summary>
    [Test]
    public async Task ColdPuddleAlwaysTurnsToGas()
    {
        var testMap = await CreateRoom();
        var server = Pair.Server;
        var entityManager = server.EntMan;
        EntityUid puddleUid = default;

        await server.WaitAssertion(() =>
        {
            var spilled = new Solution(CarbonDioxideReagent.Id, FixedPoint2.New(5)) { Temperature = SprayTemperature };
            Assert.That(entityManager.System<PuddleSystem>().TrySpillAt(CentreTileRef(testMap), spilled, out puddleUid, sound: false, tileReact: false));
        });

        await server.WaitRunTicks(TicksPerSecond * 30); // as fast as atmos brings the heat back in

        await server.WaitAssertion(() =>
        {
            var carbonDioxide = RoomTiles().Sum(tile => TileMixture(testMap, tile).GetMoles(Gas.CarbonDioxide));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entityManager.EntityExists(puddleUid) && !entityManager.IsQueuedForDeletion(puddleUid), Is.False,
                    "The carbon dioxide puddle did not all turn to gas.");
                Assert.That(carbonDioxide, Is.GreaterThan(5f * 4f * 0.9f), "The puddle did not become as much gas as it should.");
                Assert.That(TileMixture(testMap, Centre).Temperature, Is.LessThan(Atmospherics.T20C - 1f), "Turning to gas did not cool the air.");
            }
        });
    }

    /// <summary>
    ///     Carbon dioxide landing in a hot puddle cools it far more than as much water does.
    /// </summary>
    [Test]
    public async Task CoolsAHotPuddleFarMoreThanWater()
    {
        var testMap = await CreateRoom();
        var server = Pair.Server;
        var entityManager = server.EntMan;

        float PuddleTemperatureAfter(ProtoId<ReagentPrototype> coolant, float coolantTemperature)
        {
            var temperature = 0f;
            var puddleSystem = entityManager.System<PuddleSystem>();
            var tileRef = CentreTileRef(testMap);

            var hot = new Solution(_inertLiquidReagent.Id, FixedPoint2.New(50)) { Temperature = 450f };
            Assert.That(puddleSystem.TrySpillAt(tileRef, hot, out var puddleUid, sound: false, tileReact: false));

            var sprayed = new Solution(coolant.Id, FixedPoint2.New(5)) { Temperature = coolantTemperature };
            puddleSystem.DoTileReactions(tileRef, sprayed);

            // Its tile effects, as the puddle's update would run them.
            puddleSystem.TryUpdateTileEffects((puddleUid, entityManager.GetComponent<PuddleComponent>(puddleUid)), TimeSpan.Zero, careAboutTime: false);

            var puddleComponent = entityManager.GetComponent<PuddleComponent>(puddleUid);
            Assert.That(entityManager.System<SharedSolutionContainerSystem>()
                .ResolveSolution(puddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution, out var solution));
            temperature = solution!.Temperature;

            entityManager.DeleteEntity(puddleUid);
            return temperature;
        }

        await server.WaitAssertion(() =>
        {
            var afterWater = PuddleTemperatureAfter(WaterReagent, Atmospherics.T20C);
            var afterCarbonDioxide = PuddleTemperatureAfter(CarbonDioxideReagent, SprayTemperature);

            Assert.That(450f - afterCarbonDioxide, Is.GreaterThan((450f - afterWater) * 3f),
                $"Carbon dioxide cooled the puddle to {afterCarbonDioxide} K, against water's {afterWater} K.");
        });
    }

    /// <summary>
    ///     The extinguisher holds nothing but carbon dioxide, colder than it sublimes at.
    /// </summary>
    [Test]
    public async Task ExtinguisherHoldsColdCarbonDioxide()
    {
        var testMap = await Pair.CreateTestMap();

        await Pair.Server.WaitAssertion(() =>
        {
            var entityManager = Pair.Server.EntMan;
            var extinguisherUid = entityManager.SpawnEntity(ExtinguisherPrototype, testMap.GridCoords);
            Assert.That(entityManager.System<SharedSolutionContainerSystem>().TryGetSolution(extinguisherUid, "spray", out _, out var solution));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(solution!.Contents.Select(reagent => reagent.Reagent.Prototype), Is.EquivalentTo(new[] { CarbonDioxideReagent.Id }));
                Assert.That(solution.Volume, Is.GreaterThan(FixedPoint2.Zero));
                Assert.That(solution.Temperature, Is.LessThan(SublimationPoint()), "The extinguisher's carbon dioxide is not cold enough to be solid.");
            }

            entityManager.DeleteEntity(extinguisherUid);
        });
    }
}
