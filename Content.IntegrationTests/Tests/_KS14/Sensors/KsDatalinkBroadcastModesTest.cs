using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Sensors;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Sensors;

/// <summary>
///     The mapping-only transmitter broadcast-mode flags. Each is proven with a
///         normal transmitter as a negative control: the same receiver that hears
///         the beacon must NOT hear the normal transmitter, so the flag is the only
///         thing that flipped the outcome.
/// </summary>
public sealed class KsDatalinkBroadcastModesTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsBcastNormalTx
  name: test datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000

- type: entity
  id: KsBcastAllFreqTx
  name: test all-frequency datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000
    broadcastAllFrequencies: true

- type: entity
  id: KsBcastUnlimitedTx
  name: test sector-wide datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 100
    unlimitedRange: true

- type: entity
  id: KsBcastRx1200
  name: test datalink receiver
  components:
  - type: KsDatalinkReceiver

- type: entity
  id: KsBcastRx2600
  name: mistuned test datalink receiver
  components:
  - type: KsDatalinkReceiver
    frequency: 2600

# An APC receiver with no power network spawns unpowered, so these two are dark.
- type: entity
  id: KsBcastUnpoweredTx
  name: unpowered test datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000
  - type: ApcPowerReceiver
    powerLoad: 100

- type: entity
  id: KsBcastUnpoweredIgnoreTx
  name: unpowered power-independent test datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000
    ignorePower: true
  - type: ApcPowerReceiver
    powerLoad: 100
";

    private static List<(Vector2i, Tile)> BuildTiles()
    {
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < 8; x++)
        for (var y = 0; y < 8; y++)
            tiles.Add((new Vector2i(x, y), new Tile(1)));

        return tiles;
    }

    /// <summary>
    ///     A transmitter with <c>broadcastAllFrequencies</c> reaches a receiver
    ///         tuned to a different channel; a normal transmitter beside it does
    ///         not. Frequency, not the flag, is the only difference between them.
    /// </summary>
    [Test]
    public async Task TestBroadcastAllFrequenciesIgnoresTuning()
    {
        var pair = Pair;
        var server = pair.Server;

        var map = await pair.CreateTestMap();
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var gridBeacon = default(Entity<Robust.Shared.Map.Components.MapGridComponent>);
        var gridNormal = default(Entity<Robust.Shared.Map.Components.MapGridComponent>);
        var gridRx = default(Entity<Robust.Shared.Map.Components.MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridBeacon = mapManager.CreateGridEntity(map.MapId);
            gridNormal = mapManager.CreateGridEntity(map.MapId);
            gridRx = mapManager.CreateGridEntity(map.MapId);

            mapSystem.SetTiles(gridBeacon.Owner, gridBeacon.Comp, BuildTiles());
            mapSystem.SetTiles(gridNormal.Owner, gridNormal.Comp, BuildTiles());
            mapSystem.SetTiles(gridRx.Owner, gridRx.Comp, BuildTiles());

            // Move every grid off the shared origin BEFORE spawning, so each
            // machine parents to its own grid. The receiver sits well within both
            // transmitters' 1000-unit range.
            xformSystem.SetLocalPosition(gridNormal.Owner, new Vector2(0f, 80f));
            xformSystem.SetLocalPosition(gridRx.Owner, new Vector2(300f, 0f));

            // Both transmitters default to frequency 1200; the receiver listens
            // on 2600, so it is off-tune to both.
            entManager.SpawnEntity("KsBcastAllFreqTx", new EntityCoordinates(gridBeacon.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsBcastNormalTx", new EntityCoordinates(gridNormal.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsBcastRx2600", new EntityCoordinates(gridRx.Owner, new Vector2(0.5f, 0.5f)));
        });

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridRx.Owner, out var poolRx),
                "off-tune receiver never built a pool - it heard nothing at all");

            Assert.Multiple(() =>
            {
                Assert.That(poolRx!.Contacts.ContainsKey(gridBeacon.Owner),
                    "all-frequency beacon was not heard by an off-tune receiver");
                Assert.That(poolRx.Contacts.ContainsKey(gridNormal.Owner), Is.False,
                    "off-tune receiver heard a normal (single-frequency) transmitter");
            });
        });
    }

    /// <summary>
    ///     A transmitter with <c>unlimitedRange</c> reaches a receiver far beyond
    ///         its <c>maxRange</c>; a normal transmitter on the same frequency does
    ///         not. Distance, not the flag, is the only difference between them.
    /// </summary>
    [Test]
    public async Task TestUnlimitedRangeIgnoresDistance()
    {
        var pair = Pair;
        var server = pair.Server;

        var map = await pair.CreateTestMap();
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var gridBeacon = default(Entity<Robust.Shared.Map.Components.MapGridComponent>);
        var gridNormal = default(Entity<Robust.Shared.Map.Components.MapGridComponent>);
        var gridRx = default(Entity<Robust.Shared.Map.Components.MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridBeacon = mapManager.CreateGridEntity(map.MapId);
            gridNormal = mapManager.CreateGridEntity(map.MapId);
            gridRx = mapManager.CreateGridEntity(map.MapId);

            mapSystem.SetTiles(gridBeacon.Owner, gridBeacon.Comp, BuildTiles());
            mapSystem.SetTiles(gridNormal.Owner, gridNormal.Comp, BuildTiles());
            mapSystem.SetTiles(gridRx.Owner, gridRx.Comp, BuildTiles());

            // The receiver sits 5000 units out, far beyond the beacon's tiny
            // 100-unit maxRange AND the normal transmitter's 1000-unit range.
            xformSystem.SetLocalPosition(gridNormal.Owner, new Vector2(0f, 80f));
            xformSystem.SetLocalPosition(gridRx.Owner, new Vector2(5000f, 0f));

            // Both transmitters and the receiver share frequency 1200, so only
            // range can distinguish who is heard.
            entManager.SpawnEntity("KsBcastUnlimitedTx", new EntityCoordinates(gridBeacon.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsBcastNormalTx", new EntityCoordinates(gridNormal.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsBcastRx1200", new EntityCoordinates(gridRx.Owner, new Vector2(0.5f, 0.5f)));
        });

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridRx.Owner, out var poolRx),
                "distant receiver never built a pool - it heard nothing at all");

            Assert.Multiple(() =>
            {
                Assert.That(poolRx!.Contacts.ContainsKey(gridBeacon.Owner),
                    "sector-wide beacon was not heard beyond its maxRange");
                Assert.That(poolRx.Contacts.ContainsKey(gridNormal.Owner), Is.False,
                    "range-limited transmitter was heard far beyond its maxRange");
            });
        });
    }

    /// <summary>
    ///     A transmitter with <c>ignorePower</c> broadcasts while unpowered; an
    ///         otherwise identical unpowered transmitter beside it does not. Both
    ///         carry an APC receiver with no power network, so only the flag
    ///         separates the one that is heard from the one that stays dark.
    /// </summary>
    [Test]
    public async Task TestIgnorePowerBroadcastsWhileUnpowered()
    {
        var pair = Pair;
        var server = pair.Server;

        var map = await pair.CreateTestMap();
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var gridBeacon = default(Entity<Robust.Shared.Map.Components.MapGridComponent>);
        var gridNormal = default(Entity<Robust.Shared.Map.Components.MapGridComponent>);
        var gridRx = default(Entity<Robust.Shared.Map.Components.MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridBeacon = mapManager.CreateGridEntity(map.MapId);
            gridNormal = mapManager.CreateGridEntity(map.MapId);
            gridRx = mapManager.CreateGridEntity(map.MapId);

            mapSystem.SetTiles(gridBeacon.Owner, gridBeacon.Comp, BuildTiles());
            mapSystem.SetTiles(gridNormal.Owner, gridNormal.Comp, BuildTiles());
            mapSystem.SetTiles(gridRx.Owner, gridRx.Comp, BuildTiles());

            // Both transmitters sit well within their 1000-unit range of the
            // receiver; both are unpowered (APC receiver, no power network).
            xformSystem.SetLocalPosition(gridNormal.Owner, new Vector2(0f, 80f));
            xformSystem.SetLocalPosition(gridRx.Owner, new Vector2(300f, 0f));

            entManager.SpawnEntity("KsBcastUnpoweredIgnoreTx", new EntityCoordinates(gridBeacon.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsBcastUnpoweredTx", new EntityCoordinates(gridNormal.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsBcastRx1200", new EntityCoordinates(gridRx.Owner, new Vector2(0.5f, 0.5f)));
        });

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridRx.Owner, out var poolRx),
                "receiver never built a pool - it heard nothing at all");

            Assert.Multiple(() =>
            {
                Assert.That(poolRx!.Contacts.ContainsKey(gridBeacon.Owner),
                    "power-independent transmitter was not heard while unpowered");
                Assert.That(poolRx.Contacts.ContainsKey(gridNormal.Owner), Is.False,
                    "a plain unpowered transmitter was heard - power was not required");
            });
        });
    }
}
