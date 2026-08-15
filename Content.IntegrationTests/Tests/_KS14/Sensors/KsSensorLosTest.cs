#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Sensors;
using Content.Shared._KS14.Sensors;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Sensors;

/// <summary>
///     Line-of-sight coverage for the visual sensor: its own hull blocks it, other
///         grids cast shadows, a target counts as seen when ANY part of it is
///         exposed, and the coverage fan reflects those same engine occluders (the
///         ones that block a player's vision).
/// </summary>
public sealed class KsSensorLosTest : GameTest
{
    // No connected client needed: detection is read from the server pool, the fan
    // from a direct system call, and console delivery from the stored BUI state.
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Sensor = "KsLosTestVisualSensor";
    private const string Occluder = "KsLosTestOccluder";
    private const string ConsoleProto = "KsLosTestShuttleConsole";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsLosTestVisualSensor
  name: test optical array
  components:
  - type: KsSensor
    maxRange: 200
    providesName: true
    requireExternalMount: false
  - type: KsVisualSearch

- type: entity
  id: KsLosTestOccluder
  name: test occluder
  components:
  - type: Occluder

- type: entity
  id: KsLosTestShuttleConsole
  name: test shuttle console
  components:
  - type: ShuttleConsole
  - type: RadarConsole
  - type: KsSensorConsole
  - type: UserInterface
    interfaces:
      enum.ShuttleConsoleUiKey.Key:
        type: ShuttleConsoleBoundUserInterface
";

    /// <summary>
    ///     The sensor's own hull blocks it: a wall in front hides the grid directly
    ///         behind it, while a grid in an open direction is still seen.
    /// </summary>
    [Test]
    public async Task TestOwnHullBlocksDetection()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridBehind = default(Entity<MapGridComponent>);
        var gridOpen = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = mapManager.CreateGridEntity(map.MapId);
            AddShipTiles(mapSystem, gridA);
            gridBehind = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridOpen = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Move the targets FIRST: fresh grids all overlap at the origin, so
            // the sensor must be mounted only once grid A sits there alone or it
            // parents to the wrong grid.
            xformSystem.SetLocalPosition(gridBehind.Owner, new Vector2(40f, -4f)); // spans x[40,48] y[-4,4]
            xformSystem.SetLocalPosition(gridOpen.Owner, new Vector2(-4f, 40f));   // spans x[-4,4] y[40,48]

            entManager.SpawnEntity(Sensor, new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));

            // A wall on grid A blocking the +X direction (spans y -3..3 at x=3),
            // close enough to the sensor to shadow the whole target behind it.
            SpawnWall(entManager, gridA.Owner, x: 3, yFrom: -3, yTo: 3);
        });

        await Pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool),
                "sensor grid never built a contact pool");
            Assert.Multiple(() =>
            {
                Assert.That(pool!.Contacts.ContainsKey(gridOpen.Owner),
                    "a target in an open direction should be detected");
                Assert.That(pool.Contacts.ContainsKey(gridBehind.Owner), Is.False,
                    "a target behind the sensor's own wall must not be detected");
            });
        });
    }

    /// <summary>
    ///     Any-part-visible: a target whose centre is behind cover but whose edge
    ///         pokes into the clear is detected, while a target fully behind cover
    ///         is not. Both targets are at equal range, so only cover differs.
    /// </summary>
    [Test]
    public async Task TestAnyPartVisible()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridPartial = default(Entity<MapGridComponent>);
        var gridHidden = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = mapManager.CreateGridEntity(map.MapId);
            AddShipTiles(mapSystem, gridA);
            gridPartial = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridHidden = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Position the targets first so grid A sits alone at the origin when
            // the sensor is mounted (fresh grids all overlap at the origin).
            // gridPartial spans x[40,48] y[-4,4], centre ~(44,0); gridHidden
            // spans x[40,48] y[30,38].
            xformSystem.SetLocalPosition(gridPartial.Owner, new Vector2(40f, -4f));
            xformSystem.SetLocalPosition(gridHidden.Owner, new Vector2(40f, 30f));

            entManager.SpawnEntity(Sensor, new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));

            // A wall just in front of gridPartial covering only its lower half
            // (y -6..0 at x=37): the centre ray is blocked but the upper corners
            // stay visible.
            SpawnWall(entManager, gridA.Owner, x: 37, yFrom: -6, yTo: 0);

            // A wall covering gridHidden's whole subtended front (y 20..40 at
            // x=37) hides every sampled point.
            SpawnWall(entManager, gridA.Owner, x: 37, yFrom: 20, yTo: 40);
        });

        await Pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool),
                "sensor grid never built a contact pool");
            Assert.Multiple(() =>
            {
                Assert.That(pool!.Contacts.ContainsKey(gridPartial.Owner),
                    "a target with an exposed edge should be detected even though its centre is hidden");
                Assert.That(pool.Contacts.ContainsKey(gridHidden.Owner), Is.False,
                    "a target fully behind cover must not be detected");
            });
        });
    }

    /// <summary>
    ///     Everything shadows: a third grid's wall between the sensor and a target
    ///         hides that target, while a target in a clear direction is seen. The
    ///         blocker's occluders belong to neither grid, so only genuine
    ///         third-party cover is under test.
    /// </summary>
    [Test]
    public async Task TestOtherGridShadowsTarget()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridBlocker = default(Entity<MapGridComponent>);
        var gridBlocked = default(Entity<MapGridComponent>);
        var gridClear = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = mapManager.CreateGridEntity(map.MapId);
            AddShipTiles(mapSystem, gridA);
            gridBlocker = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridBlocked = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridClear = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Position every grid first so grid A sits alone at the origin for
            // the sensor and gridBlocker sits alone where its wall goes (fresh
            // grids all overlap at the origin).
            xformSystem.SetLocalPosition(gridBlocker.Owner, new Vector2(16f, -4f));
            xformSystem.SetLocalPosition(gridBlocked.Owner, new Vector2(40f, -4f)); // behind the blocker
            xformSystem.SetLocalPosition(gridClear.Owner, new Vector2(-4f, 40f));   // clear (+Y)

            entManager.SpawnEntity(Sensor, new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));

            // A wall of occluders on the blocker grid (world x20, y -3..3, on
            // real tiles) wide enough to shadow gridBlocked behind it. A
            // *different* grid => third-party cover, not the sensor's own hull.
            SpawnWall(entManager, gridBlocker.Owner, x: 4, yFrom: 1, yTo: 7);
        });

        await Pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool),
                "sensor grid never built a contact pool");
            Assert.Multiple(() =>
            {
                Assert.That(pool!.Contacts.ContainsKey(gridClear.Owner),
                    "a target in a clear direction should be detected");
                Assert.That(pool.Contacts.ContainsKey(gridBlocked.Owner), Is.False,
                    "a target shadowed by another grid must not be detected");
            });
        });
    }

    /// <summary>
    ///     The coverage fan reflects the walls: the ray straight into a wall is cut
    ///         short, while open directions reach out to the sensor's max range.
    ///         Exercises <see cref="KsLosSensorSystem.ComputeCoverage"/> directly.
    /// </summary>
    [Test]
    public async Task TestCoverageConeReflectsWalls()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var visualSearch = entManager.System<KsVisualSearchSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var sensor = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = mapManager.CreateGridEntity(map.MapId);
            sensor = entManager.SpawnEntity(Sensor, new EntityCoordinates(gridA.Owner, Vector2.Zero));

            // Wall straight ahead (+X) at x=3; every other direction is open.
            SpawnWall(entManager, gridA.Owner, x: 3, yFrom: -3, yTo: 3);
        });

        // Let the occluder tree settle.
        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var origin = xformSystem.GetWorldPosition(sensor);
            var comp = entManager.GetComponent<KsSensorComponent>(sensor);

            var points = visualSearch.ComputeCoverage((sensor, comp));
            Assert.That(points, Is.Not.Null, "coverage fan was not computed");
            Assert.That(points!.Count, Is.GreaterThan(3), "coverage fan has too few points");

            // points[0] is the apex; points[1] is the +X ray (theta = 0).
            var apex = points[0];
            Assert.That(Vector2.Distance(apex, origin), Is.LessThan(0.01f), "fan apex should sit at the sensor");

            var reachAhead = Vector2.Distance(origin, points[1]);
            Assert.That(reachAhead, Is.LessThan(5f),
                "the ray into the wall (~2.5 tiles away) should be cut short");

            var maxReach = points.Skip(1).Max(p => Vector2.Distance(origin, p));
            Assert.That(maxReach, Is.GreaterThan(100f),
                "open directions should reach out toward the sensor's max range");
        });
    }

    /// <summary>
    ///     End-to-end: an open console receives the sensor's coverage region in the
    ///         same nav state the client renders, attributed to the sensor and grid.
    /// </summary>
    [Test]
    public async Task TestCoverageDeliveredToConsole()
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

            gridA = mapManager.CreateGridEntity(map.MapId);
            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Grid A needs tiles under the console: anchoring requires a real tile.
            AddShipTiles(mapSystem, gridA);

            // A target in range keeps the pool changing so the console is pushed.
            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(50f, 0f));

            sensor = entManager.SpawnEntity(Sensor, new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            console = entManager.SpawnEntity(ConsoleProto, new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

            var actor = entManager.SpawnEntity(null, new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });

        Assert.That(uiSystem.IsUiOpen(console, ShuttleConsoleUiKey.Key), "console UI failed to open");

        await Pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var sensorNet = entManager.GetNetEntity(sensor);
            var gridNet = entManager.GetNetEntity(gridA.Owner);

            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state),
                "console has no replicated BUI state");

            var regions = state!.NavState.KsSensorNav?.Regions;
            Assert.That(regions, Is.Not.Null, "coverage regions never reached the console");

            var region = regions!.FirstOrDefault(r => r.Sensor == sensorNet);
            Assert.That(region, Is.Not.Null, "the sensor's own coverage region was not delivered");
            Assert.Multiple(() =>
            {
                Assert.That(region!.Grid, Is.EqualTo(gridNet), "region should be local to the console's grid");
                Assert.That(region.Points.Count, Is.GreaterThan(2), "a coverage region needs an apex plus a boundary");
                Assert.That(region.WorldOffsets, Is.True, "a sensor fan must ship world-oriented boundary offsets");
            });
        });
    }

    /// <summary>
    ///     A watched hull turning must force a console push even in an otherwise dead
    ///         sector, and the pushed fan must be unchanged: its boundary offsets ride
    ///         world-fixed ray angles, so yawing the mount grid reorients nothing.
    ///         Guards the WorldOffsets wire format's one weakness: unlike the old
    ///         grid-local points the client cannot rotate a stale fan with the hull,
    ///         so a quiet sector that never re-pushed would freeze the fan's world
    ///         bearing under a turning ship.
    /// </summary>
    [Test]
    public async Task TestYawForcesPushAndKeepsWorldOrientation()
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
        var sensor = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            // A lone grid: nothing to detect, no datalink, so no contact pool ever
            // changes and only the yaw hook can push after the picture settles.
            gridA = mapManager.CreateGridEntity(map.MapId);
            AddShipTiles(mapSystem, gridA);

            sensor = entManager.SpawnEntity(Sensor, new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            console = entManager.SpawnEntity(ConsoleProto, new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

            var actor = entManager.SpawnEntity(null, new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });

        await Pair.RunTicksSync(120);

        var stateBefore = default(ShuttleBoundUserInterfaceState);
        var pointsBefore = default(List<Vector2>);

        await server.WaitAssertion(() =>
        {
            var sensorNet = entManager.GetNetEntity(sensor);
            Assert.That(uiSystem.TryGetUiState(console, ShuttleConsoleUiKey.Key, out stateBefore),
                "console has no replicated BUI state");

            var region = stateBefore!.NavState.KsSensorNav?.Regions?.FirstOrDefault(r => r.Sensor == sensorNet);
            Assert.That(region, Is.Not.Null, "the sensor's coverage region was not delivered");
            pointsBefore = new List<Vector2>(region!.Points);
        });

        // The quiet-sector gate: with nothing changing, no fresh state may be pushed.
        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.That(uiSystem.TryGetUiState(console, ShuttleConsoleUiKey.Key, out ShuttleBoundUserInterfaceState? state));
            Assert.That(ReferenceEquals(state, stateBefore),
                "a dead sector with a motionless ship must generate no console traffic");
        });

        await server.WaitPost(() =>
        {
            xformSystem.SetWorldRotation(gridA.Owner, new Angle(Math.PI / 2));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var sensorNet = entManager.GetNetEntity(sensor);
            Assert.That(uiSystem.TryGetUiState(console, ShuttleConsoleUiKey.Key, out ShuttleBoundUserInterfaceState? state));
            Assert.That(ReferenceEquals(state, stateBefore), Is.False,
                "yawing the watched hull must force a push, or the client keeps drawing the fan at its old world bearing");

            var region = state!.NavState.KsSensorNav?.Regions?.FirstOrDefault(r => r.Sensor == sensorNet);
            Assert.That(region, Is.Not.Null);
            Assert.That(region!.Points, Has.Count.EqualTo(pointsBefore!.Count));

            // The apex is grid-local (the mount did not move on its grid) and every
            // boundary offset rides a world-fixed ray angle: a 90 degree yaw with no
            // occluders in play must reproduce the fan verbatim. The old grid-local
            // format would have rotated every boundary point by -90 degrees here.
            for (var i = 0; i < region.Points.Count; i++)
            {
                Assert.That(Vector2.Distance(region.Points[i], pointsBefore[i]), Is.LessThan(0.05f),
                    $"point {i} moved: the fan must be yaw-invariant in this geometry");
            }
        });
    }

    /// <summary>Big enough to clear the &lt;10 mass junk filter so it is a valid sensor target.</summary>
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

    /// <summary>
    ///     Big enough to clear the &lt;10 mass junk filter (so the grid is a valid target); entities need a
    ///         real tile to sit/anchor on, or a sensor over bare space parents to the map instead of the
    ///         grid and never sweeps.
    /// </summary>
    private static void AddShipTiles(SharedMapSystem mapSystem, Entity<MapGridComponent> grid)
    {
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < 8; x++)
        {
            for (var y = 0; y < 8; y++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            }
        }

        mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);
    }

    private static void SpawnWall(IEntityManager entManager, EntityUid grid, float x, int yFrom, int yTo)
    {
        for (var y = yFrom; y <= yTo; y++)
        {
            entManager.SpawnEntity(Occluder, new EntityCoordinates(grid, new Vector2(x, y)));
        }
    }
}
