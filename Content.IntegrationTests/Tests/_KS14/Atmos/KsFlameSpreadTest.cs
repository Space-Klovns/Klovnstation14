#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared._KS14.CCVar;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Content.IntegrationTests.Fixtures.Attributes;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     Flame runs across a cold fuel spill from one lit tile (<c>PuddleSystem.Klovn.FlameSpread.cs</c>), quickly but not
///         all at once, and stops at anything a flame can't light.
/// </summary>
public sealed class KsFlameSpreadTest : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WeldingFuelReagent = "WeldingFuel";
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly EntProtoId FirePrototype = "ChemicalFire";
    private const int TicksPerSecond = 30;

    /// <summary>
    ///     The length of the fuel line, in tiles. Each tile holds its own small puddle at room temperature.
    /// </summary>
    private const int LineLength = 20;

    private const float FuelPerTile = 3f;

    [Test]
    public async Task TestFlameRunsAcrossAColdSpill()
    {
        var gridUid = await SetUpLine(waterGapAt: null);

        await LightFirstTile(gridUid);
        await Pair.Server.WaitRunTicks(TicksPerSecond / 2 + 2);

        var afterOneUpdate = 0;
        await Pair.Server.WaitPost(() => afterOneUpdate = BurningTiles(gridUid));
        Assert.That(afterOneUpdate, Is.GreaterThan(2), "The flame should reach several tiles in its first update.");
        Assert.That(afterOneUpdate, Is.LessThan(LineLength), "The flame should travel, not light the whole spill at once.");

        var caught = await WatchTilesCatch(gridUid, seconds: 5);
        Assert.That(caught, Has.Count.EqualTo(LineLength), "The whole cold spill should catch within a few seconds.");
    }

    [Test]
    public async Task TestWaterStopsTheFlame()
    {
        const int gap = 8;
        var gridUid = await SetUpLine(waterGapAt: gap);

        await LightFirstTile(gridUid);
        var caught = await WatchTilesCatch(gridUid, seconds: 5);

        Assert.That(caught, Does.Contain(new Vector2i(gap - 1, 0)), "The fuel before the water should catch.");
        Assert.That(caught, Does.Not.Contain(new Vector2i(gap + 1, 0)), "The flame should not cross the water.");
    }

    /// <summary>
    ///     Every burning tile spreads its flame as far as it would alone, however many others burn next to it: a fire
    ///         behind it reaching the same tiles first doesn't cut its reach short.
    /// </summary>
    [Test]
    public async Task TestAFireBehindAnotherDoesNotShortenItsReach()
    {
        // Several tiles per update: with one, the fire behind has nowhere to get in the way
        await OverrideCVar(Side.Server, KsCCVars.AtmosFlameSpreadRate, 4f / TileEffectInterval());

        var aloneGridUid = await SetUpLine(waterGapAt: null);
        var pairedGridUid = await SetUpLine(waterGapAt: null);

        // In the same tick, so both lines spread on the same updates. The paired line's fire behind is lit first.
        await Pair.Server.WaitPost(() =>
        {
            LightTile(aloneGridUid, 1);
            LightTile(pairedGridUid, 0);
            LightTile(pairedGridUid, 1);
        });

        // Up to the first update that spreads anything, and no further.
        var aloneReach = 1;
        var pairedReach = 1;
        for (var tick = 0; tick < TicksPerSecond * 2 && aloneReach <= 1; tick++)
        {
            await Pair.Server.WaitRunTicks(1);
            await Pair.Server.WaitPost(() =>
            {
                aloneReach = FarthestBurningTile(aloneGridUid);
                pairedReach = FarthestBurningTile(pairedGridUid);
            });
        }

        Assert.That(aloneReach, Is.GreaterThan(1), "precondition: the lone fire should spread within a couple of seconds");
        Assert.That(pairedReach, Is.GreaterThanOrEqualTo(aloneReach),
            "A fire with another behind it should spread as far as the same fire alone.");
    }

    /// <summary>
    ///     The flame moves at the spread rate however often puddles update: each update moves it as many whole tiles as
    ///         the time since the last one earned, and carries the fraction over.
    /// </summary>
    [TestCase(0.5f)]
    [TestCase(0.25f)]
    [TestCase(0.1f)]
    public async Task TestSpreadSpeedDoesNotDependOnTheUpdateInterval(float interval)
    {
        await OverrideCVar(Side.Server, KsCCVars.AtmosPuddleTileEffectInterval, interval);
        var gridUid = await SetUpLine(waterGapAt: null);
        var rate = Pair.Server.ResolveDependency<IConfigurationManager>().GetCVar(KsCCVars.AtmosFlameSpreadRate);

        const float seconds = 1.5f;
        await LightFirstTile(gridUid);
        var caught = await WatchTilesCatch(gridUid, seconds);

        var reach = 0;
        foreach (var tile in caught)
            reach = Math.Max(reach, tile.X);

        // Within what one update moves, either way: the first update can come up to an interval after the lighting.
        var expected = rate * seconds;
        Assert.That(reach, Is.EqualTo(expected).Within(rate * interval + 1f),
            $"In {seconds} s at {rate} tiles/s the flame should reach about tile {expected}, updating every {interval} s.");
    }

    /// <summary>
    ///     Only the scheduled pass sees the flame spreading from nearby fires. A puddle that runs its effects at any other
    ///         time, as a new one does when it is spilled, isn't lit by where the flame reached on the last pass, from
    ///         fires that may have gone out since.
    /// </summary>
    [Test]
    public async Task TestANewSpillIsNotLitByFlameFromFiresThatWentOut()
    {
        // Far in one update, so the tiles past the last burning one are marked too
        await OverrideCVar(Side.Server, KsCCVars.AtmosFlameSpreadRate, 8f / TileEffectInterval());

        var gridUid = await SetUpLine(waterGapAt: null);
        await LightFirstTile(gridUid);

        const int respilledX = 3;
        var respilled = false;
        var litByOldFlame = false;
        for (var tick = 0; tick < TicksPerSecond * 2 && !respilled; tick++)
        {
            await Pair.Server.WaitRunTicks(1);
            await Pair.Server.WaitPost(() =>
            {
                if (FarthestBurningTile(gridUid) < respilledX)
                    return;

                // The flame has just passed over the tile. Put every fire out, and replace the tile's puddle with fresh
                //      cold fuel, which runs its effects as it is spilled.
                var entityManager = Pair.Server.EntMan;
                var fireEnumerator = entityManager.AllEntityQueryEnumerator<ChemicalFireComponent>();
                while (fireEnumerator.MoveNext(out var fireUid, out _))
                    entityManager.DeleteEntity(fireUid);

                var mapSystem = entityManager.System<SharedMapSystem>();
                var puddleSystem = entityManager.System<PuddleSystem>();
                var tileRef = mapSystem.GetTileRef(gridUid, entityManager.GetComponent<MapGridComponent>(gridUid), new Vector2i(respilledX, 0));
                Assert.That(puddleSystem.TryGetPuddle(tileRef, out var oldPuddleUid), "precondition: the tile has a puddle");
                entityManager.DeleteEntity(oldPuddleUid);

                puddleSystem.TrySpillAt(tileRef, new Solution(WeldingFuelReagent.Id, FixedPoint2.New(FuelPerTile)) { Temperature = Atmospherics.T20C },
                    out _, sound: false);

                litByOldFlame = entityManager.TryGetComponent<ChemicalFireGridComponent>(gridUid, out var chemicalFireGridComponent) &&
                    chemicalFireGridComponent.Tiles.ContainsKey(new Vector2i(respilledX, 0));
                respilled = true;
            });
        }

        Assert.That(respilled, "precondition: the flame should reach the tile");
        Assert.That(litByOldFlame, Is.False, "Fresh cold fuel should not be lit by the flame of fires that are out.");
    }

    [Test]
    public async Task TestSpreadingCanBeTurnedOff()
    {
        await OverrideCVar(Side.Server, KsCCVars.AtmosFlameSpreadRate, 0f);

        var gridUid = await SetUpLine(waterGapAt: null);
        await LightFirstTile(gridUid);
        var caught = await WatchTilesCatch(gridUid, seconds: 5);
        Assert.That(caught, Has.Count.LessThan(LineLength / 2), "Without spreading, a cold spill should catch slowly.");
    }

    /// <summary>
    ///     A one-tile-wide corridor of room-temperature fuel puddles in breathable air, with water on one tile if asked.
    /// </summary>
    private async Task<EntityUid> SetUpLine(int? waterGapAt)
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            entityManager.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            entityManager.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);

            var mapSystem = entityManager.System<SharedMapSystem>();
            for (var x = 0; x < LineLength; x++)
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), testMap.Tile.Tile);
        });
        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitPost(() =>
        {
            var mapSystem = entityManager.System<SharedMapSystem>();
            var puddleSystem = entityManager.System<PuddleSystem>();
            for (var x = 0; x < LineLength; x++)
            {
                var reagent = x == waterGapAt ? WaterReagent : WeldingFuelReagent;
                var tileRef = mapSystem.GetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0));
                puddleSystem.TrySpillAt(tileRef, new Solution(reagent.Id, FixedPoint2.New(FuelPerTile)) { Temperature = Atmospherics.T20C },
                    out _, sound: false);
            }
        });
        await server.WaitRunTicks(TicksPerSecond);

        return testMap.Grid.Owner;
    }

    private async Task LightFirstTile(EntityUid gridUid)
    {
        await Pair.Server.WaitPost(() => LightTile(gridUid, 0));
    }

    private void LightTile(EntityUid gridUid, int x)
    {
        var entityManager = Pair.Server.EntMan;
        var tileRef = entityManager.System<SharedMapSystem>().GetTileRef(gridUid, entityManager.GetComponent<MapGridComponent>(gridUid), new Vector2i(x, 0));
        entityManager.System<SharedChemicalFireSystem>().SpawnChemicalFire(FirePrototype, tileRef);
    }

    private float TileEffectInterval()
    {
        return Pair.Server.ResolveDependency<IConfigurationManager>().GetCVar(KsCCVars.AtmosPuddleTileEffectInterval);
    }

    private int FarthestBurningTile(EntityUid gridUid)
    {
        var farthest = 0;
        if (Pair.Server.EntMan.TryGetComponent<ChemicalFireGridComponent>(gridUid, out var chemicalFireGridComponent))
        {
            foreach (var tile in chemicalFireGridComponent.Tiles.Keys)
                farthest = Math.Max(farthest, tile.X);
        }

        return farthest;
    }

    /// <summary>
    ///     Every tile seen burning over the next few seconds. A small puddle burns away soon after it catches, so the
    ///         fires burning at the end say little.
    /// </summary>
    private async Task<HashSet<Vector2i>> WatchTilesCatch(EntityUid gridUid, float seconds)
    {
        var caught = new HashSet<Vector2i>();
        for (var tick = 0; tick < TicksPerSecond * seconds; tick += 5)
        {
            await Pair.Server.WaitRunTicks(5);
            await Pair.Server.WaitPost(() =>
            {
                if (Pair.Server.EntMan.TryGetComponent<ChemicalFireGridComponent>(gridUid, out var chemicalFireGridComponent))
                    caught.UnionWith(chemicalFireGridComponent.Tiles.Keys);
            });
        }

        return caught;
    }

    private int BurningTiles(EntityUid gridUid)
    {
        return Pair.Server.EntMan.TryGetComponent<ChemicalFireGridComponent>(gridUid, out var chemicalFireGridComponent)
            ? chemicalFireGridComponent.Tiles.Count
            : 0;
    }
}
