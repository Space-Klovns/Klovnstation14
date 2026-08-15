#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Sensors;
using Content.Shared._KS14.Sensors;
using Content.Shared._KS14.Sensors.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Sensors;

/// <summary>
///     RWR (radar warning receiver): hears only emissions that actually illuminate
///         its own grid (a foreign radar cone with line of sight onto the hull, a
///         jam slice covering the centre of mass), always as a bare Bearing that
///         never triangulates. Unlike ELINT it is never self-blinded and never
///         reports the own grid's emissions; its warnings ride the datalink and
///         drive the same designation/emission-log/EmitterLive machinery.
/// </summary>
public sealed class KsRwrSystemTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
# The emitter the receiver hears: cone reach 200 (maxRange 100 x factor 2), MID band.
- type: entity
  id: KsRwrTestRadar
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
    band: KsBandMid

# Mirrors the shipped receiver: bare bearings, crude accuracy, NEVER triangulates.
- type: entity
  id: KsRwrTestRwr
  name: test radar warning receiver
  components:
  - type: KsSensor
    sensorType: Rwr
    maxRange: 64
    providesName: false
    requireExternalMount: false
    revealVelocity: false
    revealSilhouette: false
    showCoverage: false
    resolvesPosition: Bearing
    bearingAccuracy: 10
    triangulateMinBaseline: 0
  - type: KsRwr

# The shipped-style directional jammer: a 45-degree wedge, LOW band.
- type: entity
  id: KsRwrTestJammerNarrow
  name: test directional jammer array
  components:
  - type: KsJammer
    enabled: true
    jammingPower: 120
    halfAngle: 45
    requireExternalMount: false
    band: KsBandLow

# For the EmitterLive case: a position-tracking sensor that keeps the contact Live
# after its emitter falls silent.
- type: entity
  id: KsRwrTestVisual
  name: test visual search array
  components:
  - type: KsSensor
    sensorType: VisualSearch
    maxRange: 200
    providesName: false
    requireExternalMount: false
  - type: KsVisualSearch

- type: entity
  id: KsRwrTestDatalinkTx
  name: test datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000

- type: entity
  id: KsRwrTestDatalinkRx
  name: test datalink receiver
  components:
  - type: KsDatalinkReceiver

- type: entity
  id: KsRwrTestOccluder
  name: test occluder
  components:
  - type: Occluder
";

    /// <summary>
    ///     The core hearing rule: a foreign radar whose cone reach covers the RWR's
    ///         grid files a designated Rwr-tier Bearing contact (heard band/pattern,
    ///         quantized signal strength), feeds the emission log and reads
    ///         EmitterLive on the wire, while an identical radar past its cone reach
    ///         is never heard at all.
    /// </summary>
    [Test]
    public async Task TestRwrHearsIlluminatingRadar()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridR = default(Entity<MapGridComponent>);
        var gridE = default(Entity<MapGridComponent>);
        var gridFar = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridE = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridFar = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // 120m: well inside the 200 reach. 300m: past it, never illuminated.
            xformSystem.SetLocalPosition(gridE.Owner, new Vector2(120f, 0f));
            xformSystem.SetLocalPosition(gridFar.Owner, new Vector2(300f, 0f));

            entManager.SpawnEntity("KsRwrTestRwr", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRwrTestRadar", new EntityCoordinates(gridE.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRwrTestRadar", new EntityCoordinates(gridFar.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridR.Owner, out var pool),
                "the RWR grid never built a contact pool");
            Assert.That(pool!.Contacts.ContainsKey(gridE.Owner),
                "a radar whose cone covers the grid must be heard");
            Assert.That(pool.Contacts.ContainsKey(gridFar.Owner), Is.False,
                "a radar past its cone reach does not illuminate the grid and must never be heard");

            var record = pool.Contacts[gridE.Owner];
            var source = record.Sources.Values.First();

            Assert.Multiple(() =>
            {
                Assert.That(source.Type, Is.EqualTo(KsSensorType.Rwr),
                    "a heard search radar files as an Rwr-tier return");
                Assert.That(source.Quality, Is.EqualTo(KsPositionQuality.Bearing),
                    "the warning receiver resolves a bearing, never a fix");
                Assert.That(record.Designation, Is.Not.Null,
                    "an Rwr filing is emitter-class and must assign a designation");
                Assert.That(pool.EmissionLog.Any(e => e.Kind == KsEmissionLogKind.EmitterNew && e.Designation == record.Designation),
                    "acquiring the emitter must land on the emission log");
            });

            var contact = CollectContact(entManager, gridR.Owner, gridE.Owner);
            Assert.That(contact, Is.Not.Null, "the heard emitter must reach the console snapshot");
            Assert.Multiple(() =>
            {
                Assert.That(contact!.Quality, Is.EqualTo(KsPositionQuality.Bearing));
                Assert.That(contact.WorldPosition, Is.EqualTo(Vector2.Zero),
                    "a Bearing-quality state must carry no position block");
                Assert.That(contact.EmitterLive, "an actively heard emitter reads EmitterLive on the wire");
                Assert.That(contact.Band?.Id, Is.EqualTo("KsBandMid"),
                    "the RWR reads back the heard emission's band");
                Assert.That(contact.Pattern, Is.EqualTo(KsEmissionPattern.Continuous));
                Assert.That(contact.Bearing, Is.Not.Null, "an own-sensor bearing track must carry its strobe");
                // 120m into a 200m reach: raw 1 - 120/200 = 0.4, quantized UP to the
                // 0.5 quarter step (the raw ratio would invert into range).
                Assert.That(contact.Bearing!.Value.SignalStrength, Is.EqualTo(0.5f).Within(0.001f),
                    "the wire signal strength must be the quarter-step quantized measurement");
            });
        });
    }

    /// <summary>
    ///     The defining difference from ELINT: the warning receiver keeps warning
    ///         while its own grid emits (no self-blind), and it never files the own
    ///         grid's emissions as contacts.
    /// </summary>
    [Test]
    public async Task TestRwrNotSelfBlindedAndIgnoresOwnEmissions()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var sensors = entManager.System<KsSensorSystem>();

        var map = await Pair.CreateTestMap();

        var gridR = default(Entity<MapGridComponent>);
        var gridE = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridE = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridE.Owner, new Vector2(120f, 0f));

            // The RWR grid runs its OWN active radar: an ELINT here would be deaf.
            entManager.SpawnEntity("KsRwrTestRwr", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRwrTestRadar", new EntityCoordinates(gridR.Owner, new Vector2(2.5f, 0.5f)));
            entManager.SpawnEntity("KsRwrTestRadar", new EntityCoordinates(gridE.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(sensors.IsGridEmitting(gridR.Owner),
                "the setup requires the RWR grid to be actively emitting");

            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridR.Owner, out var pool),
                "the RWR grid never built a contact pool");
            Assert.Multiple(() =>
            {
                Assert.That(pool!.Contacts.ContainsKey(gridE.Owner)
                    && pool.Contacts[gridE.Owner].Sources.Values.Any(s => s.Type == KsSensorType.Rwr),
                    "the warning receiver must keep hearing foreign radars while the own radar is up");
                Assert.That(pool.Contacts.ContainsKey(gridR.Owner), Is.False,
                    "the own grid's emissions are never filed as a contact");
            });
        });
    }

    /// <summary>
    ///     Illumination is beam geometry: a hull in another grid's radar shadow is
    ///         not being painted, so the RWR (like ELINT) is deaf to the occluded
    ///         radar, while an unshadowed receiver in the same sky hears it fine.
    /// </summary>
    [Test]
    public async Task TestRwrDeafToOccludedRadar()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridE = default(Entity<MapGridComponent>);
        var gridShadowed = default(Entity<MapGridComponent>);
        var gridClear = default(Entity<MapGridComponent>);
        var blocker = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridShadowed = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridClear = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridE = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            blocker = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // An 8-tall occluder wall halfway between the emitter and the shadowed
            // receiver, grids kept well apart (overlapping grid bodies collide and
            // drift). The clear receiver sits far off the shadow line.
            xformSystem.SetLocalPosition(gridClear.Owner, new Vector2(0f, 40f));
            xformSystem.SetLocalPosition(gridE.Owner, new Vector2(60f, 0f));
            xformSystem.SetLocalPosition(blocker.Owner, new Vector2(30f, 0f));
            SpawnOccluderColumn(entManager, blocker.Owner, 4, 0, 7);

            entManager.SpawnEntity("KsRwrTestRadar", new EntityCoordinates(gridE.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRwrTestRwr", new EntityCoordinates(gridShadowed.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRwrTestRwr", new EntityCoordinates(gridClear.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            entManager.TryGetComponent<KsSensorContactPoolComponent>(gridShadowed.Owner, out var shadowedPool);
            Assert.That(shadowedPool == null || !shadowedPool.Contacts.ContainsKey(gridE.Owner),
                "a receiver in the emitter's occluder shadow is not illuminated and must hear nothing");

            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridClear.Owner, out var clearPool)
                && clearPool!.Contacts.ContainsKey(gridE.Owner),
                "the unshadowed receiver must hear the same radar (the emitter IS audible)");
        }));
    }

    /// <summary>
    ///     Jam slices are heard by pure arc geometry: through occluders (the same
    ///         loud-broadband asymmetry as ELINT), classified as a Jammer return so
    ///         the Rwr tier never strips the jammer classification, and not at all
    ///         from the wedge's blind rear.
    /// </summary>
    [Test]
    public async Task TestRwrHearsJammerSliceRegardlessOfOccluders()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridJ = default(Entity<MapGridComponent>);
        var gridFront = default(Entity<MapGridComponent>);
        var gridRear = default(Entity<MapGridComponent>);
        var blocker = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridJ = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridFront = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridRear = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            blocker = mapManager.CreateGridEntity(map.MapId);
            AddTiles(mapSystem, blocker, 2, 8);

            // Jammer at 90m facing west: the front receiver (at the origin) sits in
            // the wedge BEHIND an occluder wall; the rear receiver (at 180m) is in
            // range but in the blind rear, so only the arc separates the two.
            xformSystem.SetLocalPosition(gridJ.Owner, new Vector2(90f, 0f));
            xformSystem.SetLocalPosition(gridRear.Owner, new Vector2(180f, 0f));
            xformSystem.SetLocalPosition(blocker.Owner, new Vector2(40f, -2f));
            SpawnOccluderColumn(entManager, blocker.Owner, 0, 0, 7);

            var jammer = entManager.SpawnEntity("KsRwrTestJammerNarrow", new EntityCoordinates(gridJ.Owner, new Vector2(0.5f, 0.5f)));
            xformSystem.SetWorldRotation(jammer, new Angle(Math.PI));

            entManager.SpawnEntity("KsRwrTestRwr", new EntityCoordinates(gridFront.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRwrTestRwr", new EntityCoordinates(gridRear.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridFront.Owner, out var frontPool)
                && frontPool!.Contacts.ContainsKey(gridJ.Owner),
                "a jam slice covering the grid is heard straight through the occluder");

            var record = frontPool!.Contacts[gridJ.Owner];
            var source = record.Sources.Values.First();
            Assert.Multiple(() =>
            {
                Assert.That(source.Type, Is.EqualTo(KsSensorType.Jammer),
                    "a heard jam slice files under the Jammer classification, not the Rwr tier");
                Assert.That(source.Quality, Is.EqualTo(KsPositionQuality.Bearing));
                Assert.That(source.Band?.Id, Is.EqualTo("KsBandLow"),
                    "the jammer's band rides the detection");

                entManager.TryGetComponent<KsSensorContactPoolComponent>(gridRear.Owner, out var rearPool);
                Assert.That(rearPool == null || !rearPool.Contacts.ContainsKey(gridJ.Owner),
                    "a receiver in the wedge's blind rear is not being jammed and must hear nothing");
            });
        });
    }

    /// <summary>
    ///     RWR warnings ride the datalink like any other detection: a sensor-less
    ///         ally receives the designated Bearing track (never upgraded to a fix)
    ///         and, because the relaying ship announces itself as an Exact ally, may
    ///         anchor the relayed strobe at that ally's position.
    /// </summary>
    [Test]
    public async Task TestRwrWarningRidesDatalinkStaysBearing()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridB = default(Entity<MapGridComponent>);
        var gridE = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridE = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(0f, 60f));
            xformSystem.SetLocalPosition(gridE.Owner, new Vector2(120f, 0f));

            // A hears and relays; B carries no sensors at all.
            entManager.SpawnEntity("KsRwrTestRwr", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRwrTestDatalinkTx", new EntityCoordinates(gridA.Owner, new Vector2(2.5f, 0.5f)));
            entManager.SpawnEntity("KsRwrTestDatalinkRx", new EntityCoordinates(gridB.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRwrTestRadar", new EntityCoordinates(gridE.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridB.Owner, out var pool),
                "the receiving grid never built a contact pool");
            Assert.That(pool!.Contacts.ContainsKey(gridE.Owner),
                "the RWR warning must reach the sensor-less ally over the datalink");

            var record = pool.Contacts[gridE.Owner];
            var source = record.Sources.Values.First(s => s.Type == KsSensorType.Rwr);
            Assert.Multiple(() =>
            {
                Assert.That(source.Hops, Is.EqualTo(1), "the relayed warning travelled one datalink hop");
                Assert.That(source.Quality, Is.EqualTo(KsPositionQuality.Bearing),
                    "a relayed bearing track never arrives as a fix");
                Assert.That(record.Designation, Is.Not.Null, "the designation rides the relay");
            });

            var contact = CollectContact(entManager, gridB.Owner, gridE.Owner);
            Assert.That(contact, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(contact!.Quality, Is.EqualTo(KsPositionQuality.Bearing),
                    "one relayed bearing cannot triangulate anything");
                Assert.That(contact.WorldPosition, Is.EqualTo(Vector2.Zero),
                    "the withheld position must not leak to the relayed viewer");
                Assert.That(contact.EmitterLive, "a relayed live warning reads EmitterLive");
                Assert.That(contact.Bearing, Is.Not.Null,
                    "the ally announced itself (an Exact self-report), so its strobe may anchor there");
                Assert.That(contact.Bearing!.Value.SourceGrid, Is.EqualTo(entManager.GetNetEntity(gridA.Owner)),
                    "the strobe's apex belongs to the measuring ally");
            });
        });
    }

    /// <summary>
    ///     The wire EmitterLive flag is emission state, not track state: a contact
    ///         held Live by visual search reads EmitterLive only while an
    ///         emitter-class source actually hears it emit, and drops the flag (with
    ///         the matching log line) once the radar goes dark, even though the
    ///         track itself stays Live.
    /// </summary>
    [Test]
    public async Task TestEmitterLiveFlagTracksEmission()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridV = default(Entity<MapGridComponent>);
        var gridT = default(Entity<MapGridComponent>);
        var radar = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridV = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridT = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridT.Owner, new Vector2(60f, 0f));

            entManager.SpawnEntity("KsRwrTestRwr", new EntityCoordinates(gridV.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRwrTestVisual", new EntityCoordinates(gridV.Owner, new Vector2(2.5f, 0.5f)));
            radar = entManager.SpawnEntity("KsRwrTestRadar", new EntityCoordinates(gridT.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var contact = CollectContact(entManager, gridV.Owner, gridT.Owner);
            Assert.That(contact, Is.Not.Null, "the target must be on the roster");
            Assert.Multiple(() =>
            {
                Assert.That(contact!.Live, "the visual track holds the contact live");
                Assert.That(contact.EmitterLive, "the RWR hears the radar emitting");
                Assert.That(contact.Type, Is.EqualTo(KsSensorType.VisualSearch),
                    "the visual track wins the tier while the RWR only adds emission knowledge");
            });
        });

        // Silence the emitter and let the emitter source's live window lapse; the
        // visual track keeps re-seeing the grid the whole time.
        await server.WaitPost(() => entManager.GetComponent<KsSensorComponent>(radar).Enabled = false);

        await Pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var contact = CollectContact(entManager, gridV.Owner, gridT.Owner);
            Assert.That(contact, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(contact!.Live, "the visual track must still hold the contact live");
                Assert.That(contact.EmitterLive, Is.False,
                    "a dark radar is no longer an emission, whatever else still tracks the hull");

                var pool = entManager.GetComponent<KsSensorContactPoolComponent>(gridV.Owner);
                Assert.That(pool.EmissionLog.Any(e => e.Kind == KsEmissionLogKind.EmitterSilent),
                    "the emitter going dark must be logged");
            });
        });
    }

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
        AddTiles(mapSystem, grid, 8, 8);
        return grid;
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

    private static void SpawnOccluderColumn(IEntityManager entManager, EntityUid grid, int x, int yFrom, int yTo)
    {
        for (var y = yFrom; y <= yTo; y++)
        {
            entManager.SpawnEntity("KsRwrTestOccluder", new EntityCoordinates(grid, new Vector2(x, y)));
        }
    }
}
