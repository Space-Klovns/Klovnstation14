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
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Sensors;

/// <summary>
///     The position-quality axis: a Bearing-quality contact's console snapshot must
///         carry NO position block (the client cannot render what it never receives),
///         only a single collapsed bearing strobe; an Exact source, or datalink
///         triangulation across a wide enough baseline, earns the fix back. The pool
///         record always keeps the true position server-side: quality gates only the
///         snapshot.
/// </summary>
public sealed class KsSensorQualityTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
# The emitter the listeners hear. Rides the TARGET grid in these tests.
- type: entity
  id: KsQualityTestRadar
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
  id: KsQualityTestElint
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

# 0 = this sensor's bearings never participate in triangulation.
- type: entity
  id: KsQualityTestElintNoTri
  name: test non-triangulating elint array
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
    triangulateMinBaseline: 0
  - type: KsElint
    ignoreFraction: 0

# An Exact co-tracker: visual search sees any grid in range with a clear line of sight.
- type: entity
  id: KsQualityTestVisual
  name: test visual search array
  components:
  - type: KsSensor
    sensorType: VisualSearch
    maxRange: 200
    providesName: false
    requireExternalMount: false
  - type: KsVisualSearch

# Stock datalink configuration: frequency 1200, AnnounceSelf and RelayContacts on.
- type: entity
  id: KsQualityTestTx
  name: test datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000

# A pure relay/repeater: forwards its pool but never announces its own grid.
- type: entity
  id: KsQualityTestSilentTx
  name: test silent datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000
    announceSelf: false

- type: entity
  id: KsQualityTestRx
  name: test datalink receiver
  components:
  - type: KsDatalinkReceiver

- type: entity
  id: KsQualityTestJammer
  name: test jammer array
  components:
  - type: KsJammer
    enabled: true
    jammingPower: 120
    halfAngle: 180
    requireExternalMount: false
";

    /// <summary>
    ///     A bearing-only track must not leak a position: the snapshot's whole position
    ///         block is zeroed while the single strobe points from the measuring sensor
    ///         toward the target's true spot, which the pool record itself still holds.
    /// </summary>
    [Test]
    public async Task TestBearingContactWithholdsPosition()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridE = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridE = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(120f, 0f));

            entManager.SpawnEntity("KsQualityTestElint", new EntityCoordinates(gridE.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestRadar", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridE.Owner, out var pool),
                "the ELINT grid never built a contact pool");
            Assert.That(pool!.Contacts.TryGetValue(gridR.Owner, out var record),
                "ELINT should have heard the emitting radar");

            // Server truth is intact: quality must gate only the snapshot.
            Assert.That(record!.WorldPosition.X, Is.GreaterThan(100f),
                "the pool record must keep the emitter's true position");

            var contact = CollectContact(entManager, gridE.Owner, gridR.Owner);
            Assert.That(contact, Is.Not.Null, "the console snapshot should carry the bearing contact");

            Assert.Multiple(() =>
            {
                Assert.That(contact!.Quality, Is.EqualTo(KsPositionQuality.Bearing));
                Assert.That(contact.Live, "the track should still be live");
                Assert.That(contact.WorldPosition, Is.EqualTo(Vector2.Zero),
                    "a bearing contact must not carry a position");
                Assert.That(contact.LinearVelocity, Is.EqualTo(Vector2.Zero),
                    "a bearing contact must not carry a velocity");
                Assert.That(contact.LocalBounds, Is.EqualTo(default(Box2)),
                    "a bearing contact must not carry a silhouette");
                Assert.That(contact.LocalCenter, Is.EqualTo(Vector2.Zero),
                    "a bearing contact must not carry a centre of mass");
                Assert.That(contact.Bearing, Is.Not.Null, "an own-sensor bearing track must carry its strobe");
            });

            var strobe = contact!.Bearing!.Value;
            Angle expected = Math.Atan2(
                record.WorldPosition.Y - strobe.Origin.Y,
                record.WorldPosition.X - strobe.Origin.X);

            Assert.Multiple(() =>
            {
                Assert.That(strobe.SourceGrid, Is.EqualTo(entManager.GetNetEntity(gridE.Owner)),
                    "an own-sensor strobe is anchored to the viewing grid");
                Assert.That((strobe.Origin - new Vector2(0.5f, 0.5f)).Length(), Is.LessThan(1.5f),
                    "the strobe's apex sits at the measuring sensor's mount");
                Assert.That(Math.Abs(Angle.ShortestDistance(strobe.Bearing, expected).Degrees), Is.LessThan(3.0),
                    "the strobe's centreline points at the target's true position");
            });
        });
    }

    /// <summary>
    ///     A live Exact source wins the snapshot back: the same emitter also seen by a
    ///         visual search renders as a full positioned contact, no strobe.
    /// </summary>
    [Test]
    public async Task TestExactSourceUpgradesBearingContact()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridE = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridE = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(120f, 0f));

            entManager.SpawnEntity("KsQualityTestElint", new EntityCoordinates(gridE.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestVisual", new EntityCoordinates(gridE.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestRadar", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridE.Owner, out var pool));
            Assert.That(pool!.Contacts.TryGetValue(gridR.Owner, out var record));

            // Scenario sanity: the combine is only exercised if the bearing source is
            // genuinely co-present with the visual one.
            Assert.That(record!.Sources.Values.Any(s => s.Quality == KsPositionQuality.Bearing),
                "the ELINT bearing source must be tracking alongside the visual one");

            var contact = CollectContact(entManager, gridE.Owner, gridR.Owner);
            Assert.That(contact, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(contact!.Quality, Is.EqualTo(KsPositionQuality.Exact),
                    "a live visual track must upgrade the co-heard emitter to a fix");
                Assert.That((contact.WorldPosition - record!.WorldPosition).Length(), Is.LessThan(0.01f),
                    "the fix is the record's true position");
                Assert.That(contact.Bearing, Is.Null, "an Exact contact carries no strobe");
            });
        });
    }

    /// <summary>
    ///     A relayed bearing stays a bearing (invariant: an ally's bearing track never
    ///         arrives as an exact fix), and its strobe is anchored at the position the
    ///         RECEIVER knows for the measuring ally (its datalink self-report), never
    ///         at the target.
    /// </summary>
    [Test]
    public async Task TestRelayedBearingStaysBearing()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridB = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(0f, 60f));
            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(120f, 0f));

            // A hears the emitter and relays; B only listens to the datalink.
            entManager.SpawnEntity("KsQualityTestElint", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestTx", new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestRx", new EntityCoordinates(gridB.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestRadar", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(80);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridB.Owner, out var pool),
                "the receiver grid never built a contact pool");
            Assert.That(pool!.Contacts.TryGetValue(gridR.Owner, out var record),
                "the bearing track should ride the datalink");
            Assert.That(record!.WorldPosition.X, Is.GreaterThan(100f),
                "the relayed record still keeps the true position server-side");

            var contact = CollectContact(entManager, gridB.Owner, gridR.Owner);
            Assert.That(contact, Is.Not.Null);
            Assert.That(contact!.Bearing, Is.Not.Null,
                "the ally announced itself, so its strobe is placeable");

            var strobe = contact.Bearing!.Value;
            var allyCom = pool.Contacts[gridA.Owner].WorldPosition;

            Assert.Multiple(() =>
            {
                Assert.That(contact.Quality, Is.EqualTo(KsPositionQuality.Bearing),
                    "a relayed bearing must never arrive as an exact fix");
                Assert.That(contact.WorldPosition, Is.EqualTo(Vector2.Zero),
                    "and must not leak the target's position");
                Assert.That(strobe.SourceGrid, Is.EqualTo(entManager.GetNetEntity(gridA.Owner)),
                    "the strobe is attributed to the measuring ally");
                Assert.That((strobe.Origin - allyCom).Length(), Is.LessThan(0.01f),
                    "the strobe's apex is the ally position the receiver knows via self-report");
            });
        });
    }

    /// <summary>
    ///     Two live bearing tracks from grids far enough apart triangulate: the
    ///         snapshot upgrades to an Exact fix at the record's true position and the
    ///         strobe disappears (the plot shows the fix instead).
    /// </summary>
    [Test]
    public async Task TestTriangulationRevealsExact()
    {
        var (contact, record) = await RunTriangulationScenario("KsQualityTestElint", new Vector2(0f, 120f));

        Assert.Multiple(() =>
        {
            Assert.That(contact.Quality, Is.EqualTo(KsPositionQuality.Exact),
                "a ~45 degree baseline across two grids must triangulate into a fix");
            Assert.That((contact.WorldPosition - record.WorldPosition).Length(), Is.LessThan(0.01f),
                "the fix is the record's true position");
            Assert.That(contact.Bearing, Is.Null, "a triangulated contact shows the fix, not a strobe");
        });
    }

    /// <summary>
    ///     A nearly collinear pair (a few degrees of parallax) stays bearing-only: the
    ///         baseline test, not mere source count, is what earns the fix.
    /// </summary>
    [Test]
    public async Task TestTriangulationRequiresBaseline()
    {
        var (contact, _) = await RunTriangulationScenario("KsQualityTestElint", new Vector2(-60f, 10f));

        Assert.Multiple(() =>
        {
            Assert.That(contact.Quality, Is.EqualTo(KsPositionQuality.Bearing),
                "a near-collinear baseline must not triangulate");
            Assert.That(contact.WorldPosition, Is.EqualTo(Vector2.Zero));
        });
    }

    /// <summary>
    ///     triangulateMinBaseline 0 = never: the same wide baseline that fixes the
    ///         emitter for stock arrays does nothing for sensors that opted out.
    /// </summary>
    [Test]
    public async Task TestTriangulationDisabledSensorNeverFixes()
    {
        var (contact, _) = await RunTriangulationScenario("KsQualityTestElintNoTri", new Vector2(0f, 120f));

        Assert.Multiple(() =>
        {
            Assert.That(contact.Quality, Is.EqualTo(KsPositionQuality.Bearing),
                "a 0-threshold sensor's bearings must never participate in triangulation");
            Assert.That(contact.WorldPosition, Is.EqualTo(Vector2.Zero));
        });
    }

    /// <summary>
    ///     A bearing ghost's strobe is FROZEN at the last measurement: after the
    ///         emitter goes silent and the listener moves, the wedge still points from
    ///         where the bearing was taken. Recomputing it from the moving viewer would
    ///         hand the client a family of rays intersecting exactly at the withheld
    ///         last-known point.
    /// </summary>
    [Test]
    public async Task TestBearingGhostKeepsFrozenStrobe()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridE = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);
        var radarUid = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridE = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(120f, 0f));

            entManager.SpawnEntity("KsQualityTestElint", new EntityCoordinates(gridE.Owner, new Vector2(0.5f, 0.5f)));
            radarUid = entManager.SpawnEntity("KsQualityTestRadar", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        var frozenOrigin = default(Vector2);
        var frozenBearing = default(Angle);

        await server.WaitAssertion(() =>
        {
            var contact = CollectContact(entManager, gridE.Owner, gridR.Owner);
            Assert.That(contact?.Bearing, Is.Not.Null, "the live bearing track should exist before the emitter goes dark");

            frozenOrigin = contact!.Bearing!.Value.Origin;
            frozenBearing = contact.Bearing.Value.Bearing;
        });

        // The emitter goes silent; the track decays to a memory ghost.
        await server.WaitPost(() =>
        {
            entManager.GetComponent<KsSensorComponent>(radarUid).Enabled = false;
        });

        // Past the live window (2 ticks * 0.5s), then some margin, then move the
        // listener: a recomputed strobe would follow it, a frozen one must not.
        await Pair.RunTicksSync(240);

        await server.WaitPost(() =>
        {
            xformSystem.SetLocalPosition(gridE.Owner, new Vector2(0f, 50f));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var contact = CollectContact(entManager, gridE.Owner, gridR.Owner);
            Assert.That(contact, Is.Not.Null, "the ghost should persist (nothing confirmed the spot empty)");
            Assert.That(contact!.Live, Is.False, "the track must have decayed to a memory ghost");
            Assert.That(contact.Bearing, Is.Not.Null, "the ghost keeps its last strobe");

            var strobe = contact.Bearing!.Value;
            Assert.Multiple(() =>
            {
                Assert.That((strobe.Origin - frozenOrigin).Length(), Is.LessThan(0.01f),
                    "the ghost strobe's apex must stay frozen at the measurement position");
                Assert.That(Math.Abs(Angle.ShortestDistance(strobe.Bearing, frozenBearing).Degrees), Is.LessThan(0.01),
                    "and its centreline frozen at the measured direction");
            });
        });
    }

    /// <summary>
    ///     A relayed strobe's apex is the measuring ally's position, so it may only be
    ///         shown when the viewer knows that ally at EXACT effective quality. Here
    ///         ally B never announces itself (pure relay) and the viewer only
    ///         bearing-knows B through ELINT, so B's relayed bearing of the jammer ship
    ///         must arrive roster-only: a strobe would anchor at B's true position and
    ///         leak it.
    /// </summary>
    [Test]
    public async Task TestBearingKnownAllyAnchorsNoStrobe()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridV = default(Entity<MapGridComponent>);
        var gridB = default(Entity<MapGridComponent>);
        var gridT = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridT = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridV = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridT.Owner, new Vector2(90f, 0f));
            xformSystem.SetLocalPosition(gridV.Owner, new Vector2(0f, 100f));

            // T jams B's radar -> B files one Bearing home-on-jam track of T and
            // relays it WITHOUT announcing itself. V's ELINT hears B's (still
            // emitting) jammed radar, so V knows B only as a bearing.
            entManager.SpawnEntity("KsQualityTestRadar", new EntityCoordinates(gridB.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestSilentTx", new EntityCoordinates(gridB.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestJammer", new EntityCoordinates(gridT.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestElint", new EntityCoordinates(gridV.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestRx", new EntityCoordinates(gridV.Owner, new Vector2(1.5f, 0.5f)));
        });

        await Pair.RunTicksSync(80);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridV.Owner, out var pool),
                "the viewer grid never built a contact pool");
            Assert.That(pool!.Contacts.ContainsKey(gridB.Owner),
                "the viewer's ELINT should be hearing the ally's jammed-but-emitting radar");
            Assert.That(pool.Contacts.ContainsKey(gridT.Owner),
                "the ally's home-on-jam track of the jammer ship should ride the relay");

            var allyContact = CollectContact(entManager, gridV.Owner, gridB.Owner);
            var jammerContact = CollectContact(entManager, gridV.Owner, gridT.Owner);
            Assert.That(allyContact, Is.Not.Null);
            Assert.That(jammerContact, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(allyContact!.Quality, Is.EqualTo(KsPositionQuality.Bearing),
                    "scenario sanity: the viewer must know the unannounced ally only as a bearing");
                Assert.That(allyContact.Bearing, Is.Not.Null,
                    "the viewer's own strobe on the ally is fine (apex at the viewer itself)");

                Assert.That(jammerContact!.Quality, Is.EqualTo(KsPositionQuality.Bearing));
                Assert.That(jammerContact.WorldPosition, Is.EqualTo(Vector2.Zero));
                Assert.That(jammerContact.Bearing, Is.Null,
                    "a bearing relayed by a bearing-known ally must be roster-only: its strobe apex would leak the ally's true position");
            });
        });
    }

    /// <summary>
    ///     The strobe anchor is pinned per contact. After the viewer's own bearing
    ///         source is lost and pruned, the still-Bearing contact must NOT re-anchor
    ///         its strobe at the relaying ally: two true centrelines from distinct
    ///         origins, shown across time, would intersect at a static target's
    ///         withheld position, sidestepping the triangulation baseline gate (here
    ///         even set to 0 = never).
    /// </summary>
    [Test]
    public async Task TestStrobeAnchorPinnedAcrossSourceLoss()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        // Source pruning normally takes a minute; shrink it so the own-source loss
        // actually happens within the test. The fixture restores the CVar itself.
        await OverrideCVar(Side.Server, KsCCVars.SensorsSourceRetention, 2f);

        var map = await Pair.CreateTestMap();

        var gridV = default(Entity<MapGridComponent>);
        var gridB = default(Entity<MapGridComponent>);
        var gridT = default(Entity<MapGridComponent>);
        var elintUid = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridV = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridT = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(0f, 120f));
            xformSystem.SetLocalPosition(gridT.Owner, new Vector2(120f, 0f));

            // Non-triangulating listeners, so the wide baseline can never earn the
            // fix legitimately: any position recovery would be the anchor leak.
            elintUid = entManager.SpawnEntity("KsQualityTestElintNoTri", new EntityCoordinates(gridV.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestRx", new EntityCoordinates(gridV.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestElintNoTri", new EntityCoordinates(gridB.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestTx", new EntityCoordinates(gridB.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestRadar", new EntityCoordinates(gridT.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(80);

        await server.WaitAssertion(() =>
        {
            var contact = CollectContact(entManager, gridV.Owner, gridT.Owner);
            Assert.That(contact?.Bearing, Is.Not.Null, "the viewer should start with its own strobe on the emitter");
            Assert.That(contact!.Bearing!.Value.SourceGrid, Is.EqualTo(entManager.GetNetEntity(gridV.Owner)),
                "the strobe anchor starts pinned to the viewer's own grid");
        });

        // The viewer goes deaf; only the ally's relayed bearing keeps the track.
        await server.WaitPost(() =>
        {
            entManager.GetComponent<KsSensorComponent>(elintUid).Enabled = false;
        });

        // Past the shortened source retention, so the viewer's own stale bearing
        // source is pruned and only the ally's remains.
        await Pair.RunTicksSync(300);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridV.Owner, out var pool));
            Assert.That(pool!.Contacts.TryGetValue(gridT.Owner, out var record));
            Assert.That(record!.Sources.Values.Any(s => s.SourceGridNet == entManager.GetNetEntity(gridV.Owner)),
                Is.False, "scenario sanity: the viewer's own source must have been pruned");

            var contact = CollectContact(entManager, gridV.Owner, gridT.Owner);
            Assert.That(contact, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(contact!.Live, "the ally's relayed bearing should keep the track live");
                Assert.That(contact.Quality, Is.EqualTo(KsPositionQuality.Bearing));
                Assert.That(contact.Bearing, Is.Null,
                    "the strobe must stay pinned to its first anchor: re-anchoring at the ally would show a second true centreline through the withheld position");
            });
        });
    }

    /// <summary>
    ///     Shared triangulation geometry: listener A at the origin (with a receiver),
    ///         a second listener at <paramref name="secondPos"/> (with a transmitter),
    ///         the emitter at (120, 0). Returns A's snapshot contact of the emitter and
    ///         the pool record, after asserting both bearing sources landed.
    /// </summary>
    private async Task<(KsSensorContactState Contact, KsContactRecord Record)> RunTriangulationScenario(
        string elintProto,
        Vector2 secondPos)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridB = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridB.Owner, secondPos);
            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(120f, 0f));

            entManager.SpawnEntity(elintProto, new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestRx", new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity(elintProto, new EntityCoordinates(gridB.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestTx", new EntityCoordinates(gridB.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsQualityTestRadar", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(80);

        KsSensorContactState? contact = null;
        KsContactRecord? record = null;

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool),
                "grid A never built a contact pool");
            Assert.That(pool!.Contacts.TryGetValue(gridR.Owner, out record),
                "grid A should hold the emitter contact");

            var bearingGrids = record!.Sources.Values
                .Where(s => s.Quality == KsPositionQuality.Bearing)
                .Select(s => s.SourceGridNet)
                .Distinct()
                .Count();
            Assert.That(bearingGrids, Is.EqualTo(2),
                "both listeners' bearing tracks must be in A's pool for the scenario to mean anything");

            contact = CollectContact(entManager, gridA.Owner, gridR.Owner);
            Assert.That(contact, Is.Not.Null);
        });

        return (contact!, record!);
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
