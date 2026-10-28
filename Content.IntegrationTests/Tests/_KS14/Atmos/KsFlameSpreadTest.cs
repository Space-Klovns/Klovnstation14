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
        var entityManager = Pair.Server.EntMan;
        await Pair.Server.WaitPost(() =>
        {
            var tileRef = entityManager.System<SharedMapSystem>().GetTileRef(gridUid, entityManager.GetComponent<MapGridComponent>(gridUid), new Vector2i(0, 0));
            entityManager.System<SharedChemicalFireSystem>().SpawnChemicalFire(FirePrototype, tileRef);
        });
    }

    /// <summary>
    ///     Every tile seen burning over the next few seconds. A small puddle burns away soon after it catches, so the
    ///         fires burning at the end say little.
    /// </summary>
    private async Task<HashSet<Vector2i>> WatchTilesCatch(EntityUid gridUid, int seconds)
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
