#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Sensors;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Sensors;

/// <summary>
///     Regression coverage for the sensor memory lifecycle as it reaches an open
///         console: once the track is lost the contact must stop being reported as
///         <see cref="KsSensorContactState.Live"/> (it becomes a dimmed memory
///         ghost) even though the underlying pool data did not otherwise change.
///
///     The bug this guards against: console pushes were gated purely on pool data
///         mutations, but liveness is time-derived. A lost contact produced no data
///         mutation, so no fresh state was ever pushed and the console kept
///         rendering a bright, live track indefinitely (until the 20s memory
///         expiry, or forever for a static grid).
///
///     Reads the console's replicated <see cref="ShuttleBoundUserInterfaceState"/>
///         on a disconnected pair: the bug is entirely in the server's push
///         scheduling, and the real client <c>ShuttleConsoleWindow</c> cannot be
///         built in-harness.
/// </summary>
public sealed class KsSensorMemoryTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Sensor = "KsMemTestVisualSensor";
    private const string Console = "KsMemTestShuttleConsole";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsMemTestVisualSensor
  name: test optical array
  components:
  - type: KsSensor
    maxRange: 200
    providesName: true
    requireExternalMount: false
  - type: KsVisualSearch

- type: entity
  id: KsMemTestShuttleConsole
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

    [Test]
    public async Task TestContactGoesMemoryWhenTrackLost()
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
        var sensorPos = Vector2.Zero;

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = mapManager.CreateGridEntity(map.MapId);
            gridB = mapManager.CreateGridEntity(map.MapId);

            // Both grids big enough to clear the <10 mass junk filter.
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 8; x++)
            {
                for (var y = 0; y < 8; y++)
                {
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                }
            }

            mapSystem.SetTiles(gridA.Owner, gridA.Comp, tiles);
            mapSystem.SetTiles(gridB.Owner, gridB.Comp, tiles);

            // Target starts well inside the sensor's 200-tile range.
            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(50f, 0f));

            // Sensor and console ride grid A, so the console reads the pool the
            // sensor writes into. The console must be anchored to attach contacts.
            var sensor = entManager.SpawnEntity(Sensor, new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            console = entManager.SpawnEntity(Console, new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

            sensorPos = xformSystem.GetWorldPosition(sensor);

            // A bare actor entity sitting on the console holds its UI open, so
            // the sensor tick pushes fresh pictures into the replicated state.
            var actor = entManager.SpawnEntity(null, new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });

        Assert.That(uiSystem.IsUiOpen(console, ShuttleConsoleUiKey.Key), "console UI failed to open");

        // Several 0.5s sensor ticks: the target is detected and pushed live.
        await Pair.RunTicksSync(120);

        var targetNet = entManager.GetNetEntity(gridB.Owner);

        await server.WaitAssertion(() =>
        {
            var contact = GetConsoleContact(entManager, uiSystem, console, targetNet);
            Assert.That(contact, Is.Not.Null, "console never received the in-range contact");
            Assert.That(contact!.Live, Is.True, "an in-range contact should be reported as live");
        });

        // Fly the sensor ship far off. The target stays put, so the contact is
        // still correct memory, but the sensor can no longer see it OR the spot
        // it was at, so nothing can confirm it gone.
        await server.WaitPost(() =>
        {
            xformSystem.SetLocalPosition(gridA.Owner, new Vector2(2000f, 0f));
        });

        // Advance ~25s of sim time, comfortably past the 20s that used to expire a
        // moving ghost. That timeout is gone, so the contact must STILL be present,
        // just no longer live. (Tickrate-independent so the margin holds.)
        var timing = server.ResolveDependency<IGameTiming>();
        await Pair.RunTicksSync((int)(timing.TickRate * 25));

        await server.WaitAssertion(() =>
        {
            var contact = GetConsoleContact(entManager, uiSystem, console, targetNet);
            Assert.That(contact, Is.Not.Null, "the lost contact should linger as a memory ghost, not vanish");
            Assert.That(contact!.Live, Is.False,
                "a contact whose track was lost must be reported as a memory ghost, not a live track");
        });
    }

    /// <summary>
    ///     A memory ghost is deleted the moment a sensor can prove it wrong: the
    ///         target leaves but the sensor keeps a clear, in-range line of sight to
    ///         the spot it was last seen at, sees that spot empty ("look and it's
    ///         gone") and prunes the ghost outright instead of keeping stale intel.
    ///         Counterpart to <see cref="TestContactGoesMemoryWhenTrackLost"/>, where
    ///         the sensor flees and so can never confirm the spot empty.
    /// </summary>
    [Test]
    public async Task TestGhostPrunedWhenLocationConfirmedEmpty()
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
            gridB = mapManager.CreateGridEntity(map.MapId);

            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 8; x++)
            {
                for (var y = 0; y < 8; y++)
                {
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                }
            }

            mapSystem.SetTiles(gridA.Owner, gridA.Comp, tiles);
            mapSystem.SetTiles(gridB.Owner, gridB.Comp, tiles);

            // Target well within the sensor's 200-tile range, clear open space
            // between them (no occluders on the flat test grids).
            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(50f, 0f));

            var sensor = entManager.SpawnEntity(Sensor, new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            console = entManager.SpawnEntity(Console, new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

            var actor = entManager.SpawnEntity(null, new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });

        Assert.That(uiSystem.IsUiOpen(console, ShuttleConsoleUiKey.Key), "console UI failed to open");

        await Pair.RunTicksSync(120);

        var targetNet = entManager.GetNetEntity(gridB.Owner);

        await server.WaitAssertion(() =>
        {
            var contact = GetConsoleContact(entManager, uiSystem, console, targetNet);
            Assert.That(contact, Is.Not.Null, "console never received the in-range contact");
            Assert.That(contact!.Live, Is.True, "an in-range contact should be reported as live");
        });

        // The target leaves; the sensor stays put and keeps a clear view of the
        // spot the target was just at.
        await server.WaitPost(() =>
        {
            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(600f, 0f));
        });

        // Once the live window lapses and the sensor confirms the old spot empty,
        // the ghost is deleted, not merely dimmed.
        await Pair.RunTicksSync(180);

        await server.WaitAssertion(() =>
        {
            var contact = GetConsoleContact(entManager, uiSystem, console, targetNet);
            Assert.That(contact, Is.Null,
                "a ghost whose last-known spot is now in clear sensor view and empty must be pruned");
        });
    }

    /// <summary>
    ///     A confirmed-gone contact must REVIVE if the target comes back: the prune
    ///         is a tombstone (kept internally to fend off stale datalink relays),
    ///         not a permanent blacklist that silently swallows a target which
    ///         reappears.
    /// </summary>
    [Test]
    public async Task TestConfirmedGoneContactRevivesWhenTargetReturns()
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
            gridB = mapManager.CreateGridEntity(map.MapId);

            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 8; x++)
            {
                for (var y = 0; y < 8; y++)
                {
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                }
            }

            mapSystem.SetTiles(gridA.Owner, gridA.Comp, tiles);
            mapSystem.SetTiles(gridB.Owner, gridB.Comp, tiles);

            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(50f, 0f));

            var sensor = entManager.SpawnEntity(Sensor, new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            console = entManager.SpawnEntity(Console, new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

            var actor = entManager.SpawnEntity(null, new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });

        Assert.That(uiSystem.IsUiOpen(console, ShuttleConsoleUiKey.Key), "console UI failed to open");

        await Pair.RunTicksSync(120);

        var targetNet = entManager.GetNetEntity(gridB.Owner);

        // Target leaves, sensor confirms its old spot empty -> tombstoned (hidden).
        await server.WaitPost(() =>
        {
            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(600f, 0f));
        });
        await Pair.RunTicksSync(180);

        await server.WaitAssertion(() =>
        {
            Assert.That(GetConsoleContact(entManager, uiSystem, console, targetNet), Is.Null,
                "target should be confirmed gone before it returns");
        });
        await server.WaitPost(() =>
        {
            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(50f, 0f));
        });
        await Pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var contact = GetConsoleContact(entManager, uiSystem, console, targetNet);
            Assert.That(contact, Is.Not.Null, "a returning target must be re-detected, not stay tombstoned");
            Assert.That(contact!.Live, Is.True, "the revived contact should be a live track again");
        });
    }

    /// <summary>The exact state a client would render.</summary>
    private static KsSensorContactState? GetConsoleContact(
        IEntityManager entManager,
        SharedUserInterfaceSystem uiSystem,
        EntityUid console,
        NetEntity target)
    {
        if (!uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state))
            return null;

        return state.NavState.KsSensorNav?.Contacts?.FirstOrDefault(c => c.Grid == target);
    }
}
