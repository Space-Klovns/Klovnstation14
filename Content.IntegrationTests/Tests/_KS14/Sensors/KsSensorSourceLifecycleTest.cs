#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Sensors;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Sensors;
using Content.IntegrationTests.Fixtures.Attributes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Sensors;

/// <summary>
///     The source lifecycle around a contact record that outlives its sources: a
///         designated emitter must keep its heard classification once only a
///         non-emitter co-tracker still refreshes the record, and a track kept alive
///         purely by a datalink relay must decay to memory when the link drops.
///
///     The bug the first test guards against: the source-retention prune kept only
///         the single overall-freshest source, so an ally's always-fresh relayed
///         track evicted the stale ELINT/RWR sources and stripped a designated
///         RADAR emitter down to an anonymous EMITTER roster row.
/// </summary>
public sealed class KsSensorSourceLifecycleTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
# The emitter the listener hears. Rides the TARGET grid.
- type: entity
  id: KsLifecycleTestRadar
  name: test radar array
  components:
  - type: KsSensor
    sensorType: Radar
    maxRange: 100
    providesName: false
    requireExternalMount: false
  - type: KsRadar
    minDetectable: 10
    minDetectableAtMaxRange: 80
    factor: 2
    coneRangeFactor: 2

- type: entity
  id: KsLifecycleTestElint
  name: test elint array
  components:
  - type: KsSensor
    sensorType: Elint
    maxRange: 64
    providesName: false
    requireExternalMount: false
    revealVelocity: false
    revealSilhouette: false
    showCoverage: false
    resolvesPosition: Bearing
    bearingAccuracy: 4
    triangulateMinBaseline: 10
  - type: KsElint
    ignoreFraction: 0

# An Exact co-tracker: visual search sees any grid in range with a clear line of sight.
- type: entity
  id: KsLifecycleTestVisual
  name: test visual search array
  components:
  - type: KsSensor
    sensorType: VisualSearch
    maxRange: 200
    providesName: false
    requireExternalMount: false
  - type: KsVisualSearch

# Stock datalink configuration: AnnounceSelf and RelayContacts on.
- type: entity
  id: KsLifecycleTestTx
  name: test datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000

- type: entity
  id: KsLifecycleTestRx
  name: test datalink receiver
  components:
  - type: KsDatalinkReceiver
";

    /// <summary>
    ///     A designated emitter co-tracked by a fresher non-emitter source keeps its
    ///         emitter-class source (and with it the heard classification and a
    ///         truthful MEM emission status) across the source-retention prune.
    /// </summary>
    [Test]
    public async Task TestEmitterClassificationSurvivesSourcePrune()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        // Source pruning normally takes a minute; shrink it so the prune actually
        // happens within the test. The fixture restores the CVar itself.
        await OverrideCVar(Side.Server, KsCCVars.SensorsSourceRetention, 2f);

        var map = await Pair.CreateTestMap();

        var gridV = default(Entity<MapGridComponent>);
        var gridT = default(Entity<MapGridComponent>);
        var radarUid = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridV = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridT = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Inside the ELINT's hearing reach AND the visual co-tracker's range.
            xformSystem.SetLocalPosition(gridT.Owner, new Vector2(40f, 0f));

            entManager.SpawnEntity("KsLifecycleTestElint", new EntityCoordinates(gridV.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsLifecycleTestVisual", new EntityCoordinates(gridV.Owner, new Vector2(1.5f, 0.5f)));
            radarUid = entManager.SpawnEntity("KsLifecycleTestRadar", new EntityCoordinates(gridT.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(80);

        await server.WaitAssertion(() =>
        {
            var contact = CollectContact(entManager, gridV.Owner, gridT.Owner);
            Assert.That(contact, Is.Not.Null, "the viewer never picked up the emitting target");
            Assert.Multiple(() =>
            {
                Assert.That(contact!.Designation, Is.Not.Null, "a heard emitter should have been designated");
                Assert.That(contact.EmitterLive, Is.True, "an emitting radar in ELINT reach should read as a live emission");
                Assert.That(contact.Sources.Any(s => s.Type == KsSensorType.Elint),
                    "scenario sanity: the ELINT bearing source must be on the snapshot");
                Assert.That(contact.Sources.Any(s => s.Type == KsSensorType.VisualSearch),
                    "scenario sanity: the visual co-tracker must be on the snapshot");
            });
        });

        // The emitter goes dark for good; the visual co-tracker keeps refreshing the
        // record every sweep, so it stays the overall-freshest source forever.
        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(radarUid);
        });

        // Comfortably past the shortened source retention, so the prune pass has
        // seen the stale ELINT source many times over.
        await Pair.RunTicksSync(300);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridV.Owner, out var pool),
                "the viewer grid lost its contact pool");
            Assert.That(pool!.Contacts.TryGetValue(gridT.Owner, out var record),
                "the co-tracked contact must not vanish");

            var contact = CollectContact(entManager, gridV.Owner, gridT.Owner);
            Assert.That(contact, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(record!.Sources.Values.Any(s => s.Type == KsSensorType.Elint),
                    "the freshest emitter-class source must survive the retention prune: it carries the heard classification");
                Assert.That(contact!.Live, Is.True, "the visual co-tracker should keep the hull track live");
                Assert.That(contact.EmitterLive, Is.False, "a long-silent emitter must not read as a live emission");
                Assert.That(contact.Sources.Any(s => s.Type == KsSensorType.Elint),
                    "the snapshot must keep the ELINT source so the roster still classifies the row as RADAR");
                Assert.That(contact.Designation, Is.Not.Null, "the designation must survive the emitter going dark");
            });
        });
    }

    /// <summary>
    ///     A contact known only through a datalink relay decays to a memory ghost once
    ///         the link goes out of range: nothing refreshes the record any more, and
    ///         a receiver-only grid can never confirm the spot empty, so the ghost
    ///         must linger dimmed rather than stay live or vanish.
    /// </summary>
    [Test]
    public async Task TestRelayLossDecaysContactToMemory()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridV = default(Entity<MapGridComponent>);
        var gridT = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridV = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridT = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // The ally sees the target; the viewer only listens to the ally.
            xformSystem.SetLocalPosition(gridT.Owner, new Vector2(50f, 0f));
            xformSystem.SetLocalPosition(gridV.Owner, new Vector2(0f, 100f));

            entManager.SpawnEntity("KsLifecycleTestVisual", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsLifecycleTestTx", new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsLifecycleTestRx", new EntityCoordinates(gridV.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(80);

        var allyNet = entManager.GetNetEntity(gridA.Owner);

        await server.WaitAssertion(() =>
        {
            var contact = CollectContact(entManager, gridV.Owner, gridT.Owner);
            Assert.That(contact, Is.Not.Null, "the relayed target never reached the viewer's pool");
            Assert.Multiple(() =>
            {
                Assert.That(contact!.Live, Is.True, "an in-link relayed track should be live");
                Assert.That(contact.Quality, Is.EqualTo(KsPositionQuality.Exact),
                    "the ally's visual fix should ride the relay as an exact position");
            });

            // The ally's self-report is what the chart's ally layer keys on.
            var ally = CollectContact(entManager, gridV.Owner, gridA.Owner);
            Assert.That(ally, Is.Not.Null, "the announcing ally should be charted");
            Assert.That(ally!.Sources.Any(s => s.SourceGrid == allyNet),
                "the self-report source must sit on the ally's own grid, that is the ally marker's whole signal");
        });

        // The viewer drifts far out of the transmitter's range; the link drops and
        // nothing refreshes the relayed records any more.
        await server.WaitPost(() =>
        {
            xformSystem.SetLocalPosition(gridV.Owner, new Vector2(0f, 5000f));
        });

        await Pair.RunTicksSync((int) (timing.TickRate * 5));

        await server.WaitAssertion(() =>
        {
            var contact = CollectContact(entManager, gridV.Owner, gridT.Owner);
            Assert.That(contact, Is.Not.Null,
                "a receiver-only grid can never confirm the spot empty, so the relayed track must linger as memory");
            Assert.That(contact!.Live, Is.False,
                "a relayed track must decay to a memory ghost once the link goes out of range");

            var ally = CollectContact(entManager, gridV.Owner, gridA.Owner);
            Assert.That(ally, Is.Not.Null, "the lost ally should linger as memory too");
            Assert.That(ally!.Live, Is.False, "the ally's self-report must not outlive the link");
        });
    }

    /// <summary>
    ///     The console snapshot of one contact, exactly as a console would receive it:
    ///         raised through <see cref="KsCollectNavContactsEvent"/> so the whole
    ///         quality gating runs.
    /// </summary>
    private static KsSensorContactState? CollectContact(IEntityManager entManager, EntityUid viewerGrid, EntityUid targetGrid)
    {
        var ev = new KsCollectNavContactsEvent(viewerGrid);
        entManager.EventBus.RaiseEvent(EventSource.Local, ref ev);

        var targetNet = entManager.GetNetEntity(targetGrid);
        return ev.Contacts?.FirstOrDefault(c => c.Grid == targetNet);
    }

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
}
