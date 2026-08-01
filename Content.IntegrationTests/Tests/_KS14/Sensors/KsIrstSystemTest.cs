#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Sensors;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Sensors;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Sensors;

/// <summary>
///     Infrared search and track: a grid is detected by the summed heat of its exterior
///         walls rather than by sight, at a range that scales with that signature (Model B).
/// </summary>
public sealed class KsIrstSystemTest : GameTest
{
    // Some cases read the replicated console BUI state, which needs the console
    // opened server-side on a disconnected pair.
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsIrstTestSensor
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

# Deliberately shorter-ranged than the IRST sensor (100), so the IRST can keep
# tracking a target the visual sensor has lost.
- type: entity
  id: KsIrstTestVisualSensor
  name: test visual array
  components:
  - type: KsSensor
    sensorType: VisualSearch
    maxRange: 50
    providesName: true
    requireExternalMount: false
    intel:
    - KsIntelSize
  - type: KsVisualSearch

- type: entity
  id: KsIrstTestWall
  name: test wall
  components:
  - type: KsThermalSource
    signature: 10

- type: entity
  id: KsIrstTestWallLow
  name: test cold wall
  components:
  - type: KsThermalSource
    signature: 2

# These are placed at a grid corner (5 sides/corners exposed), so the grid
# signature is 5x the per-wall value: 20 -> 100, 8 -> 40, 1 -> 5.
- type: entity
  id: KsIrstTestWallHot
  name: test hot wall
  components:
  - type: KsThermalSource
    signature: 20

- type: entity
  id: KsIrstTestWallMid
  name: test warm wall
  components:
  - type: KsThermalSource
    signature: 8

- type: entity
  id: KsIrstTestWallCold
  name: test faint wall
  components:
  - type: KsThermalSource
    signature: 1

- type: entity
  id: KsIrstTestOccluder
  name: test occluder
  components:
  - type: Occluder

- type: entity
  id: KsIrstTestShuttleConsole
  name: test shuttle console
  components:
  - type: ShuttleConsole
  - type: RadarConsole
  - type: KsSensorConsole
  - type: UserInterface
    interfaces:
      enum.ShuttleConsoleUiKey.Key:
        type: ShuttleConsoleBoundUserInterface

# A readout that is only meaningful live: sticky false, so it must NOT persist
# once the sensor that read it loses track (the negative control for sticky intel).
- type: ksSensorIntel
  id: KsStickyTestVolatile
  label: ks-sensor-intel-mass-label
  metric: Mass
  valueFormat: ks-sensor-intel-mass-value
  order: 40
  sticky: false

# A SHALLOW taper, unlike KsIrstTestSensor: at factor 1.2 over 200 range the curve bottoms
# out at signature 80 - 200/1.2, i.e. below zero, so it never binds and minDetectable is the
# only gate left, letting a test isolate the sensitivity floor from the taper clamp; every
# other fixture here has the clamp swallow the floor.
- type: entity
  id: KsIrstTestFloorSensor
  name: test shallow-taper irst array
  components:
  - type: KsSensor
    sensorType: IRST
    maxRange: 200
    providesName: false
    requireExternalMount: false
    intel:
    - KsIntelHeat
  - type: KsIrst
    minDetectable: 10
    minDetectableAtMaxRange: 80
    factor: 1.2

# Shorter-ranged than the IRST so the IRST keeps tracking after visual loses it.
- type: entity
  id: KsStickyTestVisualSensor
  name: test visual array
  components:
  - type: KsSensor
    sensorType: VisualSearch
    maxRange: 50
    providesName: true
    requireExternalMount: false
    intel:
    - KsIntelSize
    - KsStickyTestVolatile
  - type: KsVisualSearch
";

    /// <summary>
    ///     A grid's thermal signature sums its exterior walls, each scaled by how many of
    ///         its eight surrounding tiles are open to space, so a boxed-in wall adds
    ///         nothing. Here: 10*5 (corner) + 0 (interior) + 2*5 (cold corner) = 60.
    /// </summary>
    [Test]
    public async Task TestThermalSignatureCrawler()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var intel = entManager.System<KsSensorIntelSystem>();

        var map = await Pair.CreateTestMap();
        var grid = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            grid = mapManager.CreateGridEntity(map.MapId);
            AddShipTiles(mapSystem, grid);

            // (0,0) is a corner tile: 5 of its 8 surrounding tiles are open space.
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWall", grid.Owner, 0, 0);
            // (3,3) sits deep inside the 8x8 floor: all eight neighbors are tiled -> interior.
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWall", grid.Owner, 3, 3);
            // (7,7) is the far corner (5 exposed), but a cold wall radiating only 2/side.
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallLow", grid.Owner, 7, 7);
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var signature = intel.GetThermalSignature(grid.Owner);
            Assert.That(signature, Is.EqualTo(60f).Within(0.01f),
                "signature should sum exposure-scaled exterior walls (10*5 + 2*5) and ignore the shielded interior one");
        });
    }

    /// <summary>
    ///     Model B: a fainter target is only seen once close enough that its degraded
    ///         effective range reaches it, and one below the sensitivity floor never at all.
    /// </summary>
    [Test]
    public async Task TestDetectionCurveAndHeatIntel()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridHot = default(Entity<MapGridComponent>);
        var gridMidNear = default(Entity<MapGridComponent>);
        var gridMidFar = default(Entity<MapGridComponent>);
        var gridCold = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridHot = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridMidNear = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridMidFar = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridCold = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Move every target off the shared origin BEFORE mounting anything, or
            // fresh grids overlapping at (0,0) parent entities to the wrong grid.
            // effRange(S) = clamp(100 - 2*(80 - S), 0, 100): hot(100)->100, mid(40)->20.
            xformSystem.SetLocalPosition(gridHot.Owner, new Vector2(90f, 0f));    // dist ~90 < 100 -> seen
            xformSystem.SetLocalPosition(gridMidNear.Owner, new Vector2(14f, 0f)); // dist ~13 < 20 -> seen
            xformSystem.SetLocalPosition(gridMidFar.Owner, new Vector2(50f, 0f));  // dist ~49 > 20 -> not seen
            // In range, but its taper bottoms out: effRange needs signature > 30 here, so
            // this leg is decided by the effRange clamp, NOT by the minDetectable gate.
            xformSystem.SetLocalPosition(gridCold.Owner, new Vector2(14f, 14f));

            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallHot", gridHot.Owner, 0, 0);
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallMid", gridMidNear.Owner, 0, 0);
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallMid", gridMidFar.Owner, 0, 0);
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallCold", gridCold.Owner, 0, 0);

            entManager.SpawnEntity("KsIrstTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool),
                "the IRST grid never built a contact pool");

            Assert.Multiple(() =>
            {
                Assert.That(pool!.Contacts.ContainsKey(gridHot.Owner),
                    "a strong signature well within range should be detected");
                Assert.That(pool.Contacts.ContainsKey(gridMidNear.Owner),
                    "a moderate signature inside its degraded effective range should be detected");
                Assert.That(pool.Contacts.ContainsKey(gridMidFar.Owner), Is.False,
                    "the same moderate signature past its degraded effective range must not be detected");
                Assert.That(pool.Contacts.ContainsKey(gridCold.Owner), Is.False,
                    "a signature whose taper leaves no effective range must never be detected, even in range");

                // HEAT is the grid's thermal signature: 20/side * 5 exposed corner tiles = 100.
                var hotSource = pool.Contacts[gridHot.Owner].Sources.Values
                    .First(s => s.Type == KsSensorType.IRST);
                Assert.That(hotSource.Intel, Is.Not.Null, "the IRST detection carried no intel");
                Assert.That(hotSource.Intel!.ContainsKey("KsIntelHeat"),
                    "the IRST detection is missing its HEAT readout");
                Assert.That(hotSource.Intel!["KsIntelHeat"], Is.EqualTo("100"),
                    "the HEAT readout must be the grid's numeric thermal signature");
            });
        });
    }

    /// <summary>
    ///     A target seen by both sensors resolves to the higher (visual) tier, while both
    ///         sources stay attributed on the contact.
    /// </summary>
    [Test]
    public async Task TestVisualSupersedesIrst()
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

            gridA = mapManager.CreateGridEntity(map.MapId);
            AddShipTiles(mapSystem, gridA);
            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Close enough for both the IRST (effRange 100 at signature 100) and the
            // visual sensor (range 50) to see it.
            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(30f, 0f));
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallHot", gridB.Owner, 0, 0);

            entManager.SpawnEntity("KsIrstTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsIrstTestVisualSensor", new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));

            console = entManager.SpawnEntity("KsIrstTestShuttleConsole", new EntityCoordinates(gridA.Owner, new Vector2(2.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

            var actor = entManager.SpawnEntity(null, new EntityCoordinates(gridA.Owner, new Vector2(2.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });

        Assert.That(uiSystem.IsUiOpen(console, ShuttleConsoleUiKey.Key), "console UI failed to open");

        await Pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var gridBNet = entManager.GetNetEntity(gridB.Owner);

            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state),
                "console has no replicated BUI state");

            var contacts = state!.NavState.KsSensorNav?.Contacts;
            Assert.That(contacts, Is.Not.Null, "no contacts reached the console");

            var contact = contacts!.FirstOrDefault(c => c.Grid == gridBNet);
            Assert.That(contact, Is.Not.Null, "the shared target never reached the console");

            Assert.Multiple(() =>
            {
                Assert.That(contact!.Type, Is.EqualTo(KsSensorType.VisualSearch),
                    "a target seen by both sensors should resolve to the higher visual tier");
                Assert.That(contact.Sources.Any(s => s.Type == KsSensorType.VisualSearch),
                    "the visual source should be attributed on the contact");
                Assert.That(contact.Sources.Any(s => s.Type == KsSensorType.IRST),
                    "the IRST source should still be attributed on the contact");
                // Winning the display tier must not gate the readout merge: the visual
                // sensor reads no heat, so a lost HEAT line here would mean the higher
                // tier had suppressed what a co-tracking sensor legitimately resolved.
                Assert.That(contact.Intel.Any(i => i.Intel == "KsIntelHeat"),
                    "the HEAT readout resolved by the IRST must survive the visual sensor winning the tier");
            });
        });
    }

    /// <summary>
    ///     A stale visual source lingers in the record, but a live IRST source outranks it,
    ///         so the displayed tier follows whatever is actually tracking.
    /// </summary>
    [Test]
    public async Task TestContactFallsBackToIrstWhenVisualLost()
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

            gridA = mapManager.CreateGridEntity(map.MapId);
            AddShipTiles(mapSystem, gridA);
            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // In reach of both the visual sensor (50) and the IRST (100 at signature 100).
            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(30f, 0f));
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallHot", gridB.Owner, 0, 0);

            entManager.SpawnEntity("KsIrstTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsIrstTestVisualSensor", new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));

            console = entManager.SpawnEntity("KsIrstTestShuttleConsole", new EntityCoordinates(gridA.Owner, new Vector2(2.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

            var actor = entManager.SpawnEntity(null, new EntityCoordinates(gridA.Owner, new Vector2(2.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });

        await Pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var gridBNet = entManager.GetNetEntity(gridB.Owner);
            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state));
            var contact = state!.NavState.KsSensorNav?.Contacts?.FirstOrDefault(c => c.Grid == gridBNet);
            Assert.That(contact, Is.Not.Null, "both sensors should see the target");
            Assert.That(contact!.Type, Is.EqualTo(KsSensorType.VisualSearch),
                "while visual search still sees it, the contact is the crisp visual tier");
        });

        // Push the target past the visual sensor's 50 m reach but inside the IRST's.
        await server.WaitPost(() =>
        {
            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(70f, 0f));
        });

        await Pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            var gridBNet = entManager.GetNetEntity(gridB.Owner);
            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state));
            var contact = state!.NavState.KsSensorNav?.Contacts?.FirstOrDefault(c => c.Grid == gridBNet);
            Assert.That(contact, Is.Not.Null, "the IRST should still be tracking the target");
            Assert.That(contact!.Type, Is.EqualTo(KsSensorType.IRST),
                "once only IRST still tracks it, the contact must fall back to the IRST tier, not cling to the stale visual source");
        });
    }

    /// <summary>
    ///     Sticky intel (size, name) survives the visual source ageing out entirely, while a
    ///         readout flagged <c>sticky: false</c> clears once no live source reports it.
    /// </summary>
    [Test]
    public async Task TestStickyIntelPersistsAfterVisualLost()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
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

            gridA = mapManager.CreateGridEntity(map.MapId);
            AddShipTiles(mapSystem, gridA);
            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // In reach of both the visual sensor (50) and the IRST (100 at signature 100).
            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(30f, 0f));
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallHot", gridB.Owner, 0, 0);

            entManager.SpawnEntity("KsIrstTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsStickyTestVisualSensor", new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));

            console = entManager.SpawnEntity("KsIrstTestShuttleConsole", new EntityCoordinates(gridA.Owner, new Vector2(2.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

            var actor = entManager.SpawnEntity(null, new EntityCoordinates(gridA.Owner, new Vector2(2.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });

        await Pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var contact = GetContact();
            Assert.That(contact, Is.Not.Null, "both sensors should see the target");
            Assert.That(contact!.Name, Is.Not.Null, "the visual sensor names the target");
            Assert.That(HasIntel(contact, "KsIntelSize"), "visual size readout present while tracked");
            Assert.That(HasIntel(contact, "KsStickyTestVolatile"), "volatile readout present while tracked");
            Assert.That(HasIntel(contact, "KsIntelHeat"), "IRST heat readout present");
        });

        // Push the target past the visual sensor's 50 m reach but inside the IRST's,
        // then let the stale visual source age out entirely (past SourceRetention).
        await server.WaitPost(() =>
        {
            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(70f, 0f));
        });

        await Pair.RunTicksSync((int)(timing.TickRate * (cfg.GetCVar(KsCCVars.SensorsSourceRetention) + 5)));

        await server.WaitAssertion(() =>
        {
            var contact = GetContact();
            Assert.That(contact, Is.Not.Null, "the IRST should still be tracking the target");
            Assert.That(contact!.Type, Is.EqualTo(KsSensorType.IRST), "only the IRST still tracks the target");

            Assert.That(HasIntel(contact, "KsIntelSize"), "sticky size readout persists after the visual sensor lost the target");
            Assert.That(contact.Name, Is.Not.Null, "the learned name persists after the visual sensor lost the target");
            Assert.That(HasIntel(contact, "KsIntelHeat"), "IRST heat readout stays live");
            Assert.That(HasIntel(contact, "KsStickyTestVolatile"), Is.False, "a non-sticky readout must clear once its source ages out");
        });

        KsSensorContactState? GetContact()
        {
            var gridBNet = entManager.GetNetEntity(gridB.Owner);
            uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state);
            return state?.NavState.KsSensorNav?.Contacts?.FirstOrDefault(c => c.Grid == gridBNet);
        }

        static bool HasIntel(KsSensorContactState contact, string id)
            => contact.Intel.Any(i => i.Intel.Id == id);
    }

    /// <summary>
    ///     IRST cannot prove a spot is thermally empty (a cold ship there is invisible to
    ///         it), so it never answers KsSensorPointVisibleEvent: a lost target lingers as
    ///         a ghost that is never confirmed gone, unlike a visual sensor's.
    /// </summary>
    [Test]
    public async Task TestIrstGhostPersistsUnconfirmed()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridB = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = mapManager.CreateGridEntity(map.MapId);
            AddShipTiles(mapSystem, gridA);
            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Well within the IRST's reach at signature 100 (effRange 100).
            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(30f, 0f));
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallHot", gridB.Owner, 0, 0);

            entManager.SpawnEntity("KsIrstTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool)
                && pool!.Contacts.ContainsKey(gridB.Owner),
                "IRST should have detected the hot target first");
        });

        // The target flies out of IRST reach while its last-known spot stays in clear
        // view (a visual sensor there would confirm it empty and prune the ghost).
        await server.WaitPost(() =>
        {
            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(2000f, 0f));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool),
                "the sensor pool vanished");
            Assert.Multiple(() =>
            {
                Assert.That(pool!.Contacts.ContainsKey(gridB.Owner),
                    "the IRST memory ghost must persist after the target is lost");
                Assert.That(pool.Contacts[gridB.Owner].ConfirmedGoneAt, Is.EqualTo(TimeSpan.MinValue),
                    "IRST must never confirm a ghost's spot empty (it cannot see cold), so the ghost is never tombstoned");
            });
        });
    }

    /// <summary>
    ///     A grid too cold for IRST to perceive casts no IR shadow: the ray bleeds through
    ///         it, while one above the sensitivity floor still blocks. Both blockers sit in
    ///         identical shadowing geometry, so heat is the only difference.
    /// </summary>
    [Test]
    public async Task TestIrstBleedsThroughColdGridNotWarm()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var coldBlocker = default(Entity<MapGridComponent>);
        var warmBlocker = default(Entity<MapGridComponent>);
        var hotThroughCold = default(Entity<MapGridComponent>);
        var hotBehindWarm = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            // Small sensor grid so the close-in blockers don't overlap it.
            gridA = mapManager.CreateGridEntity(map.MapId);
            AddTiles(mapSystem, gridA, 3, 3);

            coldBlocker = mapManager.CreateGridEntity(map.MapId);
            AddTiles(mapSystem, coldBlocker, 2, 8);
            warmBlocker = mapManager.CreateGridEntity(map.MapId);
            AddTiles(mapSystem, warmBlocker, 2, 8);
            hotThroughCold = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            hotBehindWarm = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Blockers hug the sensor (wide shadow); targets sit far behind them.
            xformSystem.SetLocalPosition(coldBlocker.Owner, new Vector2(4f, -4f));
            xformSystem.SetLocalPosition(warmBlocker.Owner, new Vector2(-6f, -4f));
            xformSystem.SetLocalPosition(hotThroughCold.Owner, new Vector2(40f, 0f));
            xformSystem.SetLocalPosition(hotBehindWarm.Owner, new Vector2(-40f, 0f));

            // Occluder walls that fully shadow each target from the sensor.
            SpawnOccluderColumn(entManager, coldBlocker.Owner, 0, 1, 7); // world x=4,  y=-3..3
            SpawnOccluderColumn(entManager, warmBlocker.Owner, 1, 1, 7); // world x=-5, y=-3..3

            // The warm blocker also radiates (signature 100), so it occludes; the cold
            // blocker carries no heat (signature 0) and is transparent to IRST.
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallHot", warmBlocker.Owner, 0, 0);

            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallHot", hotThroughCold.Owner, 0, 0);
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallHot", hotBehindWarm.Owner, 0, 0);

            entManager.SpawnEntity("KsIrstTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool),
                "the IRST grid never built a contact pool");
            Assert.Multiple(() =>
            {
                Assert.That(pool!.Contacts.ContainsKey(hotThroughCold.Owner),
                    "a hot ship behind a cold (undetectable) grid should be seen: the ray bleeds through");
                Assert.That(pool.Contacts.ContainsKey(hotBehindWarm.Owner), Is.False,
                    "a hot ship behind a warm (detectable) grid must stay hidden: that hull still blocks");
            });
        });
    }

    /// <summary>
    ///     Bleed-through follows what the sensor can actually perceive, not the declared
    ///         sensitivity floor. A grid above <see cref="KsIrstComponent.MinDetectable"/>
    ///         but under the signature where the range taper bottoms out is undetectable at
    ///         any range, so it must not shadow the target behind it either.
    /// </summary>
    [Test]
    public async Task TestIrstBleedsThroughGridBelowItsTaperFloor()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var dimBlocker = default(Entity<MapGridComponent>);
        var hotTarget = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = mapManager.CreateGridEntity(map.MapId);
            AddTiles(mapSystem, gridA, 3, 3);

            dimBlocker = mapManager.CreateGridEntity(map.MapId);
            AddTiles(mapSystem, dimBlocker, 2, 8);
            hotTarget = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(dimBlocker.Owner, new Vector2(4f, -4f));
            xformSystem.SetLocalPosition(hotTarget.Owner, new Vector2(40f, 0f));

            SpawnOccluderColumn(entManager, dimBlocker.Owner, 0, 1, 7);

            // Signature 2 on a corner wall (5 exposed sides) sums to 10: exactly the
            // sensor's minDetectable, yet effRange(10) = 100 - 2*(80 - 10) clamps to 0,
            // so the sweep can never resolve it however close it sits.
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallLow", dimBlocker.Owner, 0, 0);
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallHot", hotTarget.Owner, 0, 0);

            entManager.SpawnEntity("KsIrstTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool),
                "the IRST grid never built a contact pool");
            Assert.Multiple(() =>
            {
                Assert.That(pool!.Contacts.ContainsKey(dimBlocker.Owner), Is.False,
                    "the blocker sits below the taper floor, so it must not be detected at all");
                Assert.That(pool.Contacts.ContainsKey(hotTarget.Owner),
                    "a hot ship behind a grid the IRST cannot perceive should be seen: the ray bleeds through");
            });
        });
    }

    /// <summary>
    ///     The bleed-through must never extend to the sensor's own hull: the sensor grid is
    ///         itself cold (signature 0), yet its walls must still block.
    /// </summary>
    [Test]
    public async Task TestIrstStillBlockedByOwnHull()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var hotBehindOwn = default(Entity<MapGridComponent>);
        var hotOpen = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = mapManager.CreateGridEntity(map.MapId);
            AddShipTiles(mapSystem, gridA);
            hotBehindOwn = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            hotOpen = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(hotBehindOwn.Owner, new Vector2(40f, 0f)); // behind the own wall (+X)
            xformSystem.SetLocalPosition(hotOpen.Owner, new Vector2(0f, 40f));      // open direction (+Y)

            entManager.SpawnEntity("KsIrstTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));

            // An own-hull wall shadowing the +X target. The sensor grid has no heat, so
            // this also checks the own grid is never treated as thermally transparent.
            SpawnOccluderColumn(entManager, gridA.Owner, 3, 0, 7);

            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallHot", hotBehindOwn.Owner, 0, 0);
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallHot", hotOpen.Owner, 0, 0);
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool),
                "the IRST grid never built a contact pool");
            Assert.Multiple(() =>
            {
                Assert.That(pool!.Contacts.ContainsKey(hotOpen.Owner),
                    "a hot ship in an open direction should be seen");
                Assert.That(pool.Contacts.ContainsKey(hotBehindOwn.Owner), Is.False,
                    "the sensor's own hull must still block, even though the sensor grid is cold");
            });
        });
    }

    /// <summary>
    ///     The <see cref="KsIrstComponent.MinDetectable"/> gate in isolation. Every other
    ///         fixture here uses a taper steep enough to bottom out above the declared floor,
    ///         so the floor check can be deleted without a test noticing; this sensor's taper
    ///         never binds (its effective range for ANY signature exceeds the target
    ///         distances), leaving the floor as the only thing that can reject a target.
    ///     The pair straddles the boundary exactly: the gate is a strict less-than, so a
    ///         signature equal to the floor must be detected and one under it must not.
    /// </summary>
    [Test]
    public async Task TestMinDetectableFloorRejectsBelowItAlone()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridAtFloor = default(Entity<MapGridComponent>);
        var gridBelowFloor = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridAtFloor = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridBelowFloor = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Off the shared origin before anything mounts, or walls parent to the wrong grid.
            // Both sit far inside the shallow taper's effective range (>= 104 for any
            // signature), so distance cannot be what separates them.
            xformSystem.SetLocalPosition(gridAtFloor.Owner, new Vector2(40f, 0f));
            xformSystem.SetLocalPosition(gridBelowFloor.Owner, new Vector2(-40f, 0f));

            // Corner walls, so the grid signature is 5x the per-wall value: 2 -> 10 (exactly
            // minDetectable) and 1 -> 5 (under it).
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallLow", gridAtFloor.Owner, 0, 0);
            SpawnThermalWall(entManager, xformSystem, "KsIrstTestWallCold", gridBelowFloor.Owner, 0, 0);

            entManager.SpawnEntity("KsIrstTestFloorSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool),
                "the IRST grid never built a contact pool");
            Assert.Multiple(() =>
            {
                Assert.That(pool!.Contacts.ContainsKey(gridAtFloor.Owner),
                    "a signature exactly at minDetectable is not below it, so it must be detected");
                Assert.That(pool.Contacts.ContainsKey(gridBelowFloor.Owner), Is.False,
                    "a signature under minDetectable must be rejected by the floor gate, which is the only gate this fixture can trip");
            });
        });
    }

    /// <summary>8x8, big enough to clear the &lt;10 mass junk filter.</summary>
    private static Entity<MapGridComponent> MakeShipGrid(
        IEntityManager entManager,
        IMapManager mapManager,
        SharedMapSystem mapSystem,
        MapId mapId)
    {
        var grid = mapManager.CreateGridEntity(mapId);
        AddShipTiles(mapSystem, grid);
        return grid;
    }

    private static void AddShipTiles(SharedMapSystem mapSystem, Entity<MapGridComponent> grid)
    {
        AddTiles(mapSystem, grid, 8, 8);
    }

    private static void AddTiles(SharedMapSystem mapSystem, Entity<MapGridComponent> grid, int width, int height)
    {
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            }
        }

        mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);
    }

    /// <summary>Occluders need no anchoring to register in the occluder tree.</summary>
    private static void SpawnOccluderColumn(IEntityManager entManager, EntityUid grid, int x, int yFrom, int yTo)
    {
        for (var y = yFrom; y <= yTo; y++)
        {
            entManager.SpawnEntity("KsIrstTestOccluder", new EntityCoordinates(grid, new Vector2(x, y)));
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
