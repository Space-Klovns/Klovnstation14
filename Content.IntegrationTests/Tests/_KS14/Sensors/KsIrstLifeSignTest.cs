#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Sensors;
using Content.Server.Shuttles.Systems;
using Content.Shared._KS14.Sensors;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Sensors;

/// <summary>
///     IRST life signs: the heat of living bodies read on top of the hull picture.
///         A creature aboard a hull the sweep already tracks blips grid-locally (the
///         hull track is the resolution), a gridless one is a bare point source gated
///         by range and line of sight, and everything else - crew in a cold hull, own
///         crew, the dead, a relayed picture - stays dark.
/// </summary>
public sealed class KsIrstLifeSignTest : GameTest
{
    // Every case reads the replicated console BUI state, which needs the console
    // opened server-side on a disconnected pair.
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsLifeSignTestSensor
  name: test irst array
  components:
  - type: KsSensor
    sensorType: IRST
    maxRange: 100
    providesName: false
    requireExternalMount: false
    intel:
    - KsIntelHeat
  - type: KsIrst
    minDetectable: 10
    minDetectableAtMaxRange: 80
    factor: 2

# Corner-mounted, so the grid signature is 5x this: 100, far above the floor.
- type: entity
  id: KsLifeSignTestWallHot
  name: test hot wall
  components:
  - type: KsThermalSource
    signature: 20

- type: entity
  id: KsLifeSignTestOccluder
  name: test occluder
  components:
  - type: Occluder

# Bare creature: MobState defaults to Alive, which is all a life sign needs.
- type: entity
  id: KsLifeSignTestMob
  name: test creature
  components:
  - type: MobState

- type: entity
  id: KsLifeSignTestConsole
  name: test shuttle console
  components:
  - type: ShuttleConsole
  - type: RadarConsole
  - type: KsSensorConsole
  - type: UserInterface
    interfaces:
      enum.ShuttleConsoleUiKey.Key:
        type: ShuttleConsoleBoundUserInterface

# Stock datalink configuration: frequency 1200, AnnounceSelf and RelayContacts on.
- type: entity
  id: KsLifeSignTestTx
  name: test datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000

- type: entity
  id: KsLifeSignTestRx
  name: test datalink receiver
  components:
  - type: KsDatalinkReceiver
";

    /// <summary>
    ///     Crew aboard a tracked hull blip once each, framed centre-of-mass-relative in
    ///         that hull's local space so the dots ride its dead-reckoned contact. A
    ///         corpse radiates nothing the system cares about, so it contributes no dot.
    /// </summary>
    [Test]
    public async Task TestAboardCreaturesBlipWithGridLocalOffsets()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var uiSystem = entManager.System<SharedUserInterfaceSystem>();
        var mobState = entManager.System<MobStateSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridB = default(Entity<MapGridComponent>);
        var console = default(EntityUid);
        var mobOne = default(EntityUid);
        var mobTwo = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Off the shared origin BEFORE anything mounts, or the crew parents to the
            // wrong grid. 30 is well inside the IRST's 100 reach at signature 100.
            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(30f, 0f));

            SpawnThermalWall(entManager, xformSystem, "KsLifeSignTestWallHot", gridB.Owner, 0, 0);

            mobOne = entManager.SpawnEntity("KsLifeSignTestMob", new EntityCoordinates(gridB.Owner, new Vector2(1.5f, 1.5f)));
            mobTwo = entManager.SpawnEntity("KsLifeSignTestMob", new EntityCoordinates(gridB.Owner, new Vector2(6.5f, 2.5f)));

            var corpse = entManager.SpawnEntity("KsLifeSignTestMob", new EntityCoordinates(gridB.Owner, new Vector2(3.5f, 5.5f)));
            mobState.ChangeMobState(corpse, MobState.Dead);

            console = SpawnConsole(entManager, xformSystem, uiSystem, gridA.Owner);
            entManager.SpawnEntity("KsLifeSignTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var gridBNet = entManager.GetNetEntity(gridB.Owner);
            var signs = GetLifeSigns(entManager, uiSystem, console);

            Assert.That(signs, Is.Not.Null, "the tracked hull's crew never reached the console");
            Assert.That(signs!, Has.Count.EqualTo(2),
                "exactly the two living crew should blip: the corpse is not a life sign");

            var localCenter = entManager.GetComponent<PhysicsComponent>(gridB.Owner).LocalCenter;

            Assert.Multiple(() =>
            {
                Assert.That(signs.All(s => s.Grid == gridBNet),
                    "an aboard life sign must be anchored to the hull it rides");

                foreach (var mob in new[] { mobOne, mobTwo })
                {
                    var expected = entManager.GetComponent<TransformComponent>(mob).LocalPosition - localCenter;
                    Assert.That(signs.Any(s => (s.Position - expected).Length() < 0.1f),
                        $"no life sign sat at the crewman's centre-of-mass-relative offset {expected}");
                }
            });
        });
    }

    /// <summary>
    ///     Running cold conceals a crew: a hull under the sensitivity floor is never a
    ///         contact, and the bodies inside it are resolved through that contact, so
    ///         they vanish with it.
    /// </summary>
    [Test]
    public async Task TestColdGridConcealsCrew()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var uiSystem = entManager.System<SharedUserInterfaceSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridCold = default(Entity<MapGridComponent>);
        var console = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridCold = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // In range, but it carries no thermal source at all (signature 0, under the
            // sensor's floor of 10), so the sweep can never resolve the hull.
            xformSystem.SetLocalPosition(gridCold.Owner, new Vector2(30f, 0f));

            entManager.SpawnEntity("KsLifeSignTestMob", new EntityCoordinates(gridCold.Owner, new Vector2(1.5f, 1.5f)));

            console = SpawnConsole(entManager, xformSystem, uiSystem, gridA.Owner);
            entManager.SpawnEntity("KsLifeSignTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var coldNet = entManager.GetNetEntity(gridCold.Owner);

            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state),
                "console has no replicated BUI state");

            Assert.Multiple(() =>
            {
                Assert.That(state!.NavState.KsSensorNav?.Contacts?.Any(c => c.Grid == coldNet) ?? false, Is.False,
                    "a hull under the sensitivity floor must never be a contact");
                Assert.That(state.NavState.KsSensorNav?.LifeSigns, Is.Null,
                    "crew inside a hull the IRST cannot perceive must stay hidden");
            });
        });
    }

    /// <summary>
    ///     The sweep skips its own hull, so the ship's own crew is never charted: the
    ///         picture is what the array sees outside, not a crew monitor.
    /// </summary>
    [Test]
    public async Task TestOwnGridCrewNeverBlips()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var uiSystem = entManager.System<SharedUserInterfaceSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridB = default(Entity<MapGridComponent>);
        var console = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(30f, 0f));

            // A hot target with its own crew, so the push definitely carries a life-sign
            // list: without one the own-grid assertion would pass vacuously.
            SpawnThermalWall(entManager, xformSystem, "KsLifeSignTestWallHot", gridB.Owner, 0, 0);
            entManager.SpawnEntity("KsLifeSignTestMob", new EntityCoordinates(gridB.Owner, new Vector2(1.5f, 1.5f)));

            entManager.SpawnEntity("KsLifeSignTestMob", new EntityCoordinates(gridA.Owner, new Vector2(4.5f, 4.5f)));

            console = SpawnConsole(entManager, xformSystem, uiSystem, gridA.Owner);
            entManager.SpawnEntity("KsLifeSignTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var gridANet = entManager.GetNetEntity(gridA.Owner);
            var gridBNet = entManager.GetNetEntity(gridB.Owner);
            var signs = GetLifeSigns(entManager, uiSystem, console);

            Assert.That(signs, Is.Not.Null, "the target's crew should still have reached the console");

            Assert.Multiple(() =>
            {
                Assert.That(signs!.Any(s => s.Grid == gridBNet), "the tracked target's crew must blip");
                Assert.That(signs.Any(s => s.Grid == gridANet), Is.False,
                    "the sensor's own crew must never be charted");
                Assert.That(signs.Any(s => s.Grid == null), Is.False,
                    "own-grid crew must not leak out as a free-floater either");
            });
        });
    }

    /// <summary>
    ///     A gridless body is a bare point source: seen anywhere inside the array's full
    ///         range with a clear line of sight, and nowhere else. All three cases here
    ///         are identical creatures, so only geometry separates them.
    /// </summary>
    [Test]
    public async Task TestFloaterRangeAndLos()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var uiSystem = entManager.System<SharedUserInterfaceSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var console = default(EntityUid);
        var openPos = new Vector2(0.5f, 40f);
        var shadowedPos = new Vector2(40f, 2f);
        var distantPos = new Vector2(0.5f, 150f);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            entManager.SpawnEntity("KsLifeSignTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            console = SpawnConsole(entManager, xformSystem, uiSystem, gridA.Owner);

            // An own-hull wall shadowing everything out along +X; +Y stays open.
            SpawnOccluderColumn(entManager, gridA.Owner, 3, 0, 7);

            entManager.SpawnEntity("KsLifeSignTestMob", new MapCoordinates(openPos, map.MapId));
            entManager.SpawnEntity("KsLifeSignTestMob", new MapCoordinates(shadowedPos, map.MapId));
            entManager.SpawnEntity("KsLifeSignTestMob", new MapCoordinates(distantPos, map.MapId));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var signs = GetLifeSigns(entManager, uiSystem, console);
            Assert.That(signs, Is.Not.Null, "the floater in the open never reached the console");

            Assert.Multiple(() =>
            {
                Assert.That(signs!, Has.Count.EqualTo(1),
                    "only the floater in clear view and in range may blip");
                Assert.That(signs[0].Grid, Is.Null, "a gridless body carries no hull to ride");
                Assert.That((signs[0].Position - openPos).Length(), Is.LessThan(0.1f),
                    "a floater is charted at its world position");
            });
        });
    }

    /// <summary>
    ///     Life signs are sweep-fresh, never remembered: the moment the array stops
    ///         resolving a hull the crew dots go out, even though the hull itself
    ///         lingers as an unconfirmed IRST memory ghost.
    /// </summary>
    [Test]
    public async Task TestLifeSignsVanishWhenTrackGoesGhost()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var uiSystem = entManager.System<SharedUserInterfaceSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridB = default(Entity<MapGridComponent>);
        var console = default(EntityUid);
        var sensor = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(30f, 0f));

            SpawnThermalWall(entManager, xformSystem, "KsLifeSignTestWallHot", gridB.Owner, 0, 0);
            entManager.SpawnEntity("KsLifeSignTestMob", new EntityCoordinates(gridB.Owner, new Vector2(1.5f, 1.5f)));

            console = SpawnConsole(entManager, xformSystem, uiSystem, gridA.Owner);
            sensor = entManager.SpawnEntity("KsLifeSignTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var signs = GetLifeSigns(entManager, uiSystem, console);
            Assert.That(signs, Is.Not.Null, "the tracked hull's crew should blip while the array sweeps");
            Assert.That(signs!, Has.Count.EqualTo(1));
        });

        // Kill the array rather than the target, so the hull record is untouched: the
        // contact must survive as a ghost while its crew dots do not.
        await server.WaitPost(() =>
        {
            entManager.GetComponent<KsSensorComponent>(sensor).Enabled = false;
        });

        await Pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var gridBNet = entManager.GetNetEntity(gridB.Owner);

            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state),
                "console has no replicated BUI state");

            var contact = state!.NavState.KsSensorNav?.Contacts?.FirstOrDefault(c => c.Grid == gridBNet);

            Assert.Multiple(() =>
            {
                Assert.That(contact, Is.Not.Null, "the IRST memory ghost of the hull must persist");
                Assert.That(contact!.Live, Is.False, "the track should have decayed to a ghost");
                Assert.That(state.NavState.KsSensorNav?.LifeSigns, Is.Null,
                    "life signs must not outlive the sweep that resolved them");
            });
        });
    }

    /// <summary>
    ///     Life signs are strictly the console grid's own IRST picture. An ally's
    ///         datalink relays its contacts, and the receiver charts them, but the crew
    ///         dots that came with them on the transmitting side do not travel.
    /// </summary>
    [Test]
    public async Task TestNoDatalinkRelay()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var uiSystem = entManager.System<SharedUserInterfaceSystem>();

        var map = await Pair.CreateTestMap();

        var gridTx = default(Entity<MapGridComponent>);
        var gridTarget = default(Entity<MapGridComponent>);
        var gridRx = default(Entity<MapGridComponent>);
        var console = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridTx = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridTarget = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridRx = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // The target sits inside the transmitter's IRST reach; the receiver sits far
            // outside it (so it can only ever know the target second-hand) but well
            // inside the datalink's 1000-unit range.
            xformSystem.SetLocalPosition(gridTarget.Owner, new Vector2(30f, 0f));
            xformSystem.SetLocalPosition(gridRx.Owner, new Vector2(0f, 300f));

            SpawnThermalWall(entManager, xformSystem, "KsLifeSignTestWallHot", gridTarget.Owner, 0, 0);
            entManager.SpawnEntity("KsLifeSignTestMob", new EntityCoordinates(gridTarget.Owner, new Vector2(1.5f, 1.5f)));

            entManager.SpawnEntity("KsLifeSignTestSensor", new EntityCoordinates(gridTx.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsLifeSignTestTx", new EntityCoordinates(gridTx.Owner, new Vector2(1.5f, 0.5f)));

            entManager.SpawnEntity("KsLifeSignTestRx", new EntityCoordinates(gridRx.Owner, new Vector2(0.5f, 0.5f)));
            console = SpawnConsole(entManager, xformSystem, uiSystem, gridRx.Owner);
        });

        await Pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var targetNet = entManager.GetNetEntity(gridTarget.Owner);

            // The transmitting grid did resolve the crew, so there was something to relay.
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridTx.Owner, out var txPool),
                "the IRST grid never built a contact pool");
            Assert.That(txPool!.LifeSigns.ContainsKey(gridTarget.Owner),
                "the transmitting grid should have resolved the target's crew itself");

            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state),
                "receiver console has no replicated BUI state");

            Assert.Multiple(() =>
            {
                Assert.That(state!.NavState.KsSensorNav?.Contacts?.Any(c => c.Grid == targetNet) ?? false,
                    "the relayed hull contact should reach the receiver");
                Assert.That(state.NavState.KsSensorNav?.LifeSigns, Is.Null,
                    "life signs must never ride the datalink");
            });
        });
    }

    /// <summary>
    ///     A console refresh can land between a mid-tick map change (FTL refreshes
    ///         consoles the moment the grid moves) and the next sweep's wipe. The
    ///         scratch's coordinates mean nothing on the new map, so the snapshot must
    ///         drop them rather than chart departure-map phantoms.
    /// </summary>
    [Test]
    public async Task TestMapChangeDropsStaleLifeSigns()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var uiSystem = entManager.System<SharedUserInterfaceSystem>();
        var consoleSystem = entManager.System<ShuttleConsoleSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridB = default(Entity<MapGridComponent>);
        var console = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(30f, 0f));

            SpawnThermalWall(entManager, xformSystem, "KsLifeSignTestWallHot", gridB.Owner, 0, 0);
            entManager.SpawnEntity("KsLifeSignTestMob", new EntityCoordinates(gridB.Owner, new Vector2(1.5f, 1.5f)));

            // A floater too: floaters ship raw world positions, the exact payload that
            // must not survive the hop.
            entManager.SpawnEntity("KsLifeSignTestMob", new MapCoordinates(new Vector2(0.5f, 40f), map.MapId));

            console = SpawnConsole(entManager, xformSystem, uiSystem, gridA.Owner);
            entManager.SpawnEntity("KsLifeSignTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        // Move, refresh and read inside one server callback: a sensor tick in between
        // would wipe the scratch and let the assertion pass vacuously.
        await server.WaitAssertion(() =>
        {
            Assert.That(GetLifeSigns(entManager, uiSystem, console), Is.Not.Null,
                "life signs should be flowing before the hop");

            var newMap = mapSystem.CreateMap(out _);
            xformSystem.SetCoordinates(gridA.Owner, new EntityCoordinates(newMap, Vector2.Zero));
            consoleSystem.RefreshShuttleConsoles(gridA.Owner);

            Assert.That(GetLifeSigns(entManager, uiSystem, console), Is.Null,
                "a snapshot on the new map must not chart the old map's life signs");
        });
    }

    private static List<KsLifeSignState>? GetLifeSigns(
        IEntityManager entManager,
        SharedUserInterfaceSystem uiSystem,
        EntityUid console)
    {
        uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state);
        return state?.NavState.KsSensorNav?.LifeSigns;
    }

    /// <summary>Anchored and with its UI open: a closed or loose console carries no picture.</summary>
    private static EntityUid SpawnConsole(
        IEntityManager entManager,
        SharedTransformSystem xformSystem,
        SharedUserInterfaceSystem uiSystem,
        EntityUid grid)
    {
        var console = entManager.SpawnEntity("KsLifeSignTestConsole", new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));
        xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

        var actor = entManager.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));
        uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);

        return console;
    }

    /// <summary>8x8, big enough to clear the &lt;10 mass junk filter.</summary>
    private static Entity<MapGridComponent> MakeShipGrid(
        IEntityManager entManager,
        IMapManager mapManager,
        SharedMapSystem mapSystem,
        MapId mapId)
    {
        var grid = mapManager.CreateGridEntity(mapId);

        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < 8; x++)
        {
            for (var y = 0; y < 8; y++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            }
        }

        mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);
        return grid;
    }

    /// <summary>Occluders need no anchoring to register in the occluder tree.</summary>
    private static void SpawnOccluderColumn(IEntityManager entManager, EntityUid grid, int x, int yFrom, int yTo)
    {
        for (var y = yFrom; y <= yTo; y++)
        {
            entManager.SpawnEntity("KsLifeSignTestOccluder", new EntityCoordinates(grid, new Vector2(x, y)));
        }
    }

    /// <summary>Must be anchored: the signature crawler only counts anchored hull walls, not carried or loose ones.</summary>
    private static void SpawnThermalWall(
        IEntityManager entManager,
        SharedTransformSystem xformSystem,
        string proto,
        EntityUid grid,
        int x,
        int y)
    {
        var wall = entManager.SpawnEntity(proto, new EntityCoordinates(grid, new Vector2(x + 0.5f, y + 0.5f)));
        xformSystem.AnchorEntity((wall, entManager.GetComponent<TransformComponent>(wall)));
    }
}
