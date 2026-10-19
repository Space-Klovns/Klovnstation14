using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Atmos.FoamBlanket;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Atmos.FoamBlanket;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     Foam blankets and gas fires. A blanket keeps a gas fire from starting while the air over it is cooler than the
///         foam's boiling point, and a gas fire boils it away. A fire that has heated its air burns on under foam,
///         visibly: foam only mitigates such a fire, it does not put it out.
/// </summary>
[TestOf(typeof(KsFoamBlanketSystem))]
public sealed class KsFoamGasFireTest : GameTest
{
    private static readonly EntProtoId BlanketPrototype = "KsFoamBlanket";
    private static readonly EntProtoId WallPrototype = "WallSolid";

    // Deliberately NOT static: it only exists in KsFirefightingTest's test YAML, and the YAML linter validates static ids.
    private readonly EntProtoId _flammablePrototype = "KsTestFirefightingFlammable";

    private const int TicksPerSecond = 30;

    private static readonly Vector2i FireTile = new(0, 0);
    private static readonly Vector2i BlanketTile = new(1, 0);

    /// <summary>
    ///     Two floor tiles, <see cref="FireTile"/> and <see cref="BlanketTile"/>, walled in and open to each other.
    /// </summary>
    /// <param name="plasma">Plasma and oxygen to burn, rather than plain air.</param>
    /// <param name="separated">
    ///     A wall between the two, so neither one's air reaches the other; the blanket's tile is then one further east.
    /// </param>
    private async Task<TestMapData> CreateWalledRow(bool plasma, float temperature = Atmospherics.T20C, bool separated = false)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var blanketTile = separated ? BlanketTile + new Vector2i(1, 0) : BlanketTile;

        await server.WaitPost(() =>
        {
            // Walled in: the map's own air would otherwise soak up a fire's heat at the edges and put it out.
            var mapSystem = server.EntMan.System<SharedMapSystem>();
            for (var x = -1; x <= blanketTile.X + 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    var tile = new Vector2i(x, y);
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, tile, testMap.Tile.Tile);
                    if (tile != FireTile && tile != blanketTile)
                        server.EntMan.SpawnEntity(WallPrototype, mapSystem.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, tile));
                }
            }

            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
        });

        await server.WaitRunTicks(TicksPerSecond);

        // Walled-in tiles start with no air, so it is put in by hand.
        await server.WaitPost(() =>
        {
            var atmosphereSystem = server.EntMan.System<AtmosphereSystem>();
            foreach (var tile in new[] { FireTile, blanketTile })
            {
                var mixture = atmosphereSystem.GetTileMixture((testMap.Grid.Owner, null, null), null, tile, excite: true);
                Assert.That(mixture, Is.Not.Null, $"Tile {tile} has no air.");
                mixture!.Clear();
                mixture.Temperature = temperature;
                if (plasma)
                {
                    mixture.SetMoles(Gas.Oxygen, 500f);
                    mixture.SetMoles(Gas.Plasma, 500f);
                }
                else
                {
                    mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
                    mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
                }
            }
        });

        await server.WaitRunTicks(1);
        return testMap;
    }

    private void LayFullBlanket(TestMapData testMap, Vector2i tile)
    {
        var entityManager = Pair.Server.EntMan;
        var tileRef = entityManager.System<SharedMapSystem>().GetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, tile);
        var blanketSystem = entityManager.System<KsFoamBlanketSystem>();

        for (var i = 0; i < 1000; i++)
        {
            if (blanketSystem.Deploy(BlanketPrototype, tileRef, 1f, playSound: false) <= 0f)
                return;
        }

        Assert.Fail("The blanket never filled up.");
    }

    private float GetFoam(TestMapData testMap, Vector2i tile)
        => Pair.Server.EntMan.System<KsFoamBlanketSystem>().TryGetBlanket(testMap.Grid.Owner, tile, out var blanket) ? blanket.Comp.Foam : 0f;

    /// <summary>
    ///     Nothing lights the cool gas under a blanket, however hot the spark; the tile next to it, with the same gas,
    ///         lights.
    /// </summary>
    [Test]
    public async Task TestBlanketKeepsGasFireFromStarting()
    {
        var testMap = await CreateWalledRow(plasma: true);
        var server = Pair.Server;
        var atmosphereSystem = server.EntMan.System<AtmosphereSystem>();
        var gridUid = testMap.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            LayFullBlanket(testMap, BlanketTile);
            atmosphereSystem.HotspotExpose(gridUid, BlanketTile, 5000f, 50f);
            atmosphereSystem.HotspotExpose(gridUid, FireTile, 5000f, 50f);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(atmosphereSystem.HasGasHotspot((gridUid, null), BlanketTile), Is.False, "The gas under the blanket caught fire.");
                Assert.That(atmosphereSystem.HasGasHotspot((gridUid, null), FireTile), "The gas next to the blanket did not catch fire.");
            }
        });
    }

    /// <summary>
    ///     A gas fire next to a blanket gets through it well before the blanket would drain by itself: once its heat has
    ///         reached the air over the blanket, that air burns by itself, with foam still on the floor under it.
    /// </summary>
    [Test]
    public async Task TestGasFireGetsThroughBlanket()
    {
        var testMap = await CreateWalledRow(plasma: true);
        var server = Pair.Server;
        var atmosphereSystem = server.EntMan.System<AtmosphereSystem>();
        var gridUid = testMap.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            LayFullBlanket(testMap, BlanketTile);
            atmosphereSystem.HotspotExpose(gridUid, FireTile, 5000f, 50f, soh: true);
        });

        // A full blanket left alone lasts well over half a minute: 1.5u, at most 0.04u/s from draining and the air.
        const int seconds = 15;
        var burning = false;
        var foamWhenLit = 0f;
        for (var tick = 0; tick < TicksPerSecond * seconds && !burning; tick++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() =>
            {
                burning = atmosphereSystem.HasGasHotspot((gridUid, null), BlanketTile);
                foamWhenLit = GetFoam(testMap, BlanketTile);
            });
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(burning, $"The fire did not get through the blanket within {seconds} s.");
            Assert.That(foamWhenLit, Is.GreaterThan(0f), "The fire only got through once the foam was gone, though the air over it was hot.");
        }
    }

    /// <summary>
    ///     Foaming every tile of a gas fire that has heated its air does not hide it: the flames stay, since the gas
    ///         burns in the air itself, and the hot air boils the foam off far faster than it would drain.
    /// </summary>
    [Test]
    public async Task TestFoamDoesNotHideAnEstablishedFire()
    {
        var testMap = await CreateWalledRow(plasma: true);
        var server = Pair.Server;
        var atmosphereSystem = server.EntMan.System<AtmosphereSystem>();
        var gridUid = testMap.Grid.Owner;

        await server.WaitAssertion(() => atmosphereSystem.HotspotExpose(gridUid, FireTile, 5000f, 50f, soh: true));
        await server.WaitRunTicks(TicksPerSecond * 3);

        await server.WaitAssertion(() =>
        {
            foreach (var tile in new[] { FireTile, BlanketTile })
            {
                Assert.That(atmosphereSystem.HasGasHotspot((gridUid, null), tile), $"Tile {tile} did not catch fire, so there is nothing to foam.");
                Assert.That(atmosphereSystem.GetTileMixture((gridUid, null, null), null, tile, excite: false)!.Temperature, Is.GreaterThan(600f),
                    $"Tile {tile}'s air is not hot yet.");
                LayFullBlanket(testMap, tile);
                // Not put out even for a moment: it would only light again from the air, with a sizzle for nothing.
                Assert.That(atmosphereSystem.HasGasHotspot((gridUid, null), tile), $"Foam put out the fire burning in tile {tile}'s hot air.");
            }
        });

        // Long enough for the air to light the gas again many times over, and for the blanket's first two once-a-second
        //     updates, but short enough that the foam is not yet gone.
        await server.WaitRunTicks(TicksPerSecond * 5 / 2);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var tile in new[] { FireTile, BlanketTile })
                {
                    Assert.That(atmosphereSystem.HasGasHotspot((gridUid, null), tile), $"Foam hid the fire burning in tile {tile}'s hot air.");
                    // 1.5u at the start; two seconds of draining and the old flat rate from the air would take 0.08u of it.
                    Assert.That(GetFoam(testMap, tile), Is.LessThan(1.3f), $"The fire's hot air on {tile} barely boiled the foam.");
                }
            }
        });
    }

    /// <summary>
    ///     Something burning lands in foam: it goes out while the air over the foam is cool, and keeps burning if the air
    ///         is hotter than the foam's boiling point.
    /// </summary>
    [Test]
    public async Task TestFoamProtectsOnlyInCoolAir([Values(true, false)] bool hotAir)
    {
        var testMap = await CreateWalledRow(plasma: false, temperature: hotAir ? 600f : Atmospherics.T20C);
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var flammableSystem = entityManager.System<Content.Server.Atmos.EntitySystems.FlammableSystem>();
        var gridUid = testMap.Grid.Owner;
        EntityUid flammableUid = default;

        await server.WaitPost(() =>
        {
            LayFullBlanket(testMap, BlanketTile);
            flammableUid = entityManager.SpawnEntity(_flammablePrototype, new EntityCoordinates(gridUid, FireTile.X + 0.5f, 0.5f));
            flammableSystem.AdjustFireStacks(flammableUid, 5f, ignite: true);
            Assert.That(entityManager.GetComponent<FlammableComponent>(flammableUid).OnFire, "Test flammable did not catch fire.");
        });
        await server.WaitRunTicks(5);

        await server.WaitPost(() =>
            entityManager.System<SharedTransformSystem>().SetCoordinates(flammableUid, new EntityCoordinates(gridUid, BlanketTile.X + 0.5f, 0.5f)));
        await server.WaitRunTicks(TicksPerSecond * 2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.HasComponent<KsInFoamComponent>(flammableUid), "The item on the blanket is not in foam.");
            Assert.That(entityManager.GetComponent<FlammableComponent>(flammableUid).OnFire, Is.EqualTo(hotAir),
                hotAir ? "Foam put the item out in air hotter than the foam's boiling point." : "Foam did not put the item out in cool air.");
        });
    }

    /// <summary>
    ///     A gas fire radiating into a blanket boils it with heat from the fire's own air, faster the hotter that is,
    ///         and the energy the fire's air loses is what boiling the foam took. The two tiles are walled off from each
    ///         other, so the blanket's own air stays cool and only the radiated heat can boil it.
    /// </summary>
    [Test]
    public async Task TestRadiatedFireBoilsBlanketFromTheFiresAir()
    {
        var testMap = await CreateWalledRow(plasma: false, separated: true);
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var atmosphereSystem = entityManager.System<AtmosphereSystem>();
        var blanketSystem = entityManager.System<KsFoamBlanketSystem>();
        var gridUid = testMap.Grid.Owner;
        var blanketTile = BlanketTile + new Vector2i(1, 0);
        // Below where heat starts conducting into the walls, so the fire's air loses heat only to the foam.
        const float fireTemperature = Atmospherics.MinimumTemperatureStartSuperConduction - 5f;
        var energyBefore = 0f;
        var foamBefore = 0f;
        var energyPerUnit = 0f;
        var drained = 0f;

        GasMixture FireAir() => atmosphereSystem.GetTileMixture((gridUid, null, null), null, FireTile, excite: false)!;
        float ThermalEnergy(GasMixture mixture) => mixture.Temperature * atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);

        await server.WaitAssertion(() =>
        {
            LayFullBlanket(testMap, blanketTile);
            Assert.That(blanketSystem.TryGetBlanket(gridUid, blanketTile, out var blanket));
            var component = blanket.Comp;
            energyPerUnit = component.SpecificHeat * (component.BoilingPoint - component.FoamTemperature) + component.LatentHeat;
            foamBefore = component.Foam;
            drained = component.DecayRate * 2f;

            FireAir().Temperature = fireTemperature;
            energyBefore = ThermalEnergy(FireAir());
        });

        // Radiated into every tick, as a fire next door would be, over exactly two of the blanket's once-a-second
        //     updates: the first is a second after it was laid.
        for (var tick = 0; tick < TicksPerSecond * 5 / 2; tick++)
        {
            await server.WaitPost(() => blanketSystem.ExposeToRadiatedFire(gridUid, blanketTile, FireTile, fireTemperature));
            await server.WaitRunTicks(1);
        }

        await server.WaitAssertion(() =>
        {
            var boiled = foamBefore - GetFoam(testMap, blanketTile);
            var energyLost = energyBefore - ThermalEnergy(FireAir());

            using (Assert.EnterMultipleScope())
            {
                // About 0.095u/s at some 315 K above the boiling point, against 0.01u/s from draining.
                Assert.That(boiled, Is.GreaterThan(0.15f), "The radiated fire barely boiled the blanket.");
                // Draining takes 0.01u a second without heat; the fire's air paid for everything else.
                Assert.That(energyLost, Is.EqualTo((boiled - drained) * energyPerUnit).Within(0.02f * energyPerUnit),
                    "The fire's air did not pay what boiling the foam took.");
            }
        });
    }
}
