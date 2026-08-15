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
using Content.Shared.Wires;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Sensors;

/// <summary>
///     Active radar, ELINT and jamming. Radar detects by radar cross-section (an
///         independent per-wall value on the same exposed-sides crawler as thermal),
///         with line-of-sight bleed-through: a hull too faint to perceive casts no
///         shadow. ELINT locates active emitters and is deaf while its own ship emits.
///         Jamming blinds a radar in its slice unless the radar has burnt through, and
///         the jammed radar reveals the jammer once via home-on-jam.
/// </summary>
public sealed class KsRadarSystemTest : GameTest
{
    // The JAMMED-indicator case reads the replicated console BUI state; open the console
    // server-side on a disconnected pair, the pattern the IRST/LOS tests use.
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsRadarTestSensor
  name: test radar array
  components:
  - type: KsSensor
    sensorType: Radar
    maxRange: 100
    providesName: false
    requireExternalMount: false
    intel:
    - KsIntelRcs
  - type: KsRadar
    minDetectable: 10
    minDetectableAtMaxRange: 80
    factor: 2
    coneRangeFactor: 2
    burnThroughFactor: 0.5

- type: entity
  id: KsRadarTestElint
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
    # Mirrors the shipped array: a passive listener resolves a bearing, not a fix.
    resolvesPosition: Bearing
    intel:
    - KsIntelEmitterRange
  - type: KsElint
    ignoreFraction: 0

# Mirrors the shipped receiver: bare bearings, crude accuracy, NEVER triangulates.
- type: entity
  id: KsRadarTestRwr
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

- type: entity
  id: KsRadarTestJammer
  name: test jammer array
  components:
  - type: KsJammer
    # The live jammer defaults off; these tests exercise an active one, so force it on.
    enabled: true
    jammingPower: 120
    halfAngle: 180
    requireExternalMount: false

# RCS walls only (no KsThermalSource): a grid radar-bright but thermally invisible,
# proving the two signatures are independent. At a grid corner (5 exposed tiles) the
# per-side value multiplies by 5: 20 -> 100, 8 -> 40, 1 -> 5 (below the floor).
- type: entity
  id: KsRadarTestWallHot
  name: test bright wall
  components:
  - type: KsRadarSource
    signature: 20

- type: entity
  id: KsRadarTestWallMid
  name: test dim wall
  components:
  - type: KsRadarSource
    signature: 8

- type: entity
  id: KsRadarTestWallCold
  name: test faint wall
  components:
  - type: KsRadarSource
    signature: 1

# 2 -> a grid RCS of 10 at a corner: exactly KsRadarTestSensor's minDetectable, yet under the
# RCS its taper needs to yield any range at all (80 - 100/2 = 30). The only wall that can sit
# between the two floors, which is what the transparency case needs.
- type: entity
  id: KsRadarTestWallLow
  name: test dim-but-present wall
  components:
  - type: KsRadarSource
    signature: 2

# A SHALLOW taper, unlike KsRadarTestSensor: at factor 1.2 over 200 range the curve bottoms out
# below zero RCS, so it never binds and minDetectable is the only gate left, letting a test
# isolate the sensitivity floor from the taper clamp.
- type: entity
  id: KsRadarTestFloorSensor
  name: test shallow-taper radar array
  components:
  - type: KsSensor
    sensorType: Radar
    maxRange: 200
    providesName: false
    requireExternalMount: false
    intel:
    - KsIntelRcs
  - type: KsRadar
    minDetectable: 10
    minDetectableAtMaxRange: 80
    factor: 1.2
    coneRangeFactor: 2
    burnThroughFactor: 0.5

# The shipped jammer is a 45-degree wedge, not an omni bubble. Needed to exercise the
# arc test at all: with halfAngle 180 the angular comparison is a tautology.
- type: entity
  id: KsRadarTestJammerNarrow
  name: test directional jammer array
  components:
  - type: KsJammer
    enabled: true
    jammingPower: 120
    halfAngle: 45
    requireExternalMount: false

# A crude listener: hears an emitter only inside the inner 40% of its cone.
- type: entity
  id: KsRadarTestElintCrude
  name: test crude elint array
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
    intel:
    - KsIntelEmitterRange
  - type: KsElint
    ignoreFraction: 0.6

# For the co-tracking case: a passive sensor that DOES reveal velocity, so a contact can
# be held by both it and a velocity-hiding ELINT at once.
- type: entity
  id: KsRadarTestIrst
  name: test irst array
  components:
  - type: KsSensor
    sensorType: IRST
    maxRange: 100
    providesName: false
    requireExternalMount: false
  - type: KsIrst
    minDetectable: 10
    minDetectableAtMaxRange: 80
    factor: 2

# Radar-bright AND hot, so one grid can be seen by radar and IRST together.
- type: entity
  id: KsRadarTestWallDual
  name: test bright hot wall
  components:
  - type: KsRadarSource
    signature: 20
  - type: KsThermalSource
    signature: 20

# Both default to frequency 1200 and AnnounceSelf true, i.e. the stock configuration.
- type: entity
  id: KsRadarTestDatalinkTx
  name: test datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000

- type: entity
  id: KsRadarTestDatalinkRx
  name: test datalink receiver
  components:
  - type: KsDatalinkReceiver

- type: entity
  id: KsRadarTestOccluder
  name: test occluder
  components:
  - type: Occluder

- type: entity
  id: KsRadarTestShuttleConsole
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
    ///     Radar detects by radar cross-section on the Model B curve: a bright target out
    ///         to full range, a dimmer one only inside its degraded effective range, one
    ///         below the floor never. The cross-section is summed by the same exposed-sides
    ///         crawler as thermal but from an INDEPENDENT per-wall value, so these
    ///         radar-bright grids read zero thermal signature. The contact carries a
    ///         numeric RCS readout.
    /// </summary>
    [Test]
    public async Task TestRadarDetectionCurveAndRcsIntel()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var intel = entManager.System<KsSensorIntelSystem>();

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

            // effRange(S) = clamp(100 - 2*(80 - S), 0, 100): hot(100)->100, mid(40)->20.
            xformSystem.SetLocalPosition(gridHot.Owner, new Vector2(90f, 0f));
            xformSystem.SetLocalPosition(gridMidNear.Owner, new Vector2(14f, 0f));
            xformSystem.SetLocalPosition(gridMidFar.Owner, new Vector2(50f, 0f));
            // In range, but its taper bottoms out: effRange needs RCS > 30 here, so this leg
            // is decided by the effRange clamp, NOT by the minDetectable gate.
            xformSystem.SetLocalPosition(gridCold.Owner, new Vector2(14f, 14f));

            SpawnRadarWall(entManager, xformSystem, "KsRadarTestWallHot", gridHot.Owner, 0, 0);
            SpawnRadarWall(entManager, xformSystem, "KsRadarTestWallMid", gridMidNear.Owner, 0, 0);
            SpawnRadarWall(entManager, xformSystem, "KsRadarTestWallMid", gridMidFar.Owner, 0, 0);
            SpawnRadarWall(entManager, xformSystem, "KsRadarTestWallCold", gridCold.Owner, 0, 0);

            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool),
                "the radar grid never built a contact pool");

            Assert.Multiple(() =>
            {
                Assert.That(pool!.Contacts.ContainsKey(gridHot.Owner),
                    "a strong cross-section well within range should be detected");
                Assert.That(pool.Contacts.ContainsKey(gridMidNear.Owner),
                    "a moderate cross-section inside its degraded effective range should be detected");
                Assert.That(pool.Contacts.ContainsKey(gridMidFar.Owner), Is.False,
                    "the same moderate cross-section past its degraded effective range must not be detected");
                Assert.That(pool.Contacts.ContainsKey(gridCold.Owner), Is.False,
                    "a cross-section whose taper leaves no effective range must never be detected, even in range");

                // RCS and thermal are independent: these grids have only KsRadarSource.
                Assert.That(intel.GetRadarSignature(gridHot.Owner), Is.EqualTo(100f).Within(0.01f),
                    "the radar cross-section sums exposure-scaled RCS walls (20*5)");
                Assert.That(intel.GetThermalSignature(gridHot.Owner), Is.EqualTo(0f).Within(0.01f),
                    "a radar-bright grid with no thermal source must read zero thermal signature");

                var radarSource = pool.Contacts[gridHot.Owner].Sources.Values
                    .First(s => s.Type == KsSensorType.Radar);
                Assert.That(radarSource.Intel, Is.Not.Null, "the radar detection carried no intel");
                Assert.That(radarSource.Intel!["KsIntelRcs"], Is.EqualTo("100"),
                    "the RCS readout must be the grid's numeric radar cross-section");
            });
        });
    }

    /// <summary>
    ///     A grid too radar-faint to perceive (below-floor RCS) casts no radar shadow, so
    ///         a bright ship behind it is still seen, while a bright (detectable) grid in
    ///         identical shadowing geometry still blocks. The only difference between the
    ///         two blockers is their radar cross-section.
    /// </summary>
    [Test]
    public async Task TestRadarBleedsThroughStealthGridNotBright()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var stealthBlocker = default(Entity<MapGridComponent>);
        var brightBlocker = default(Entity<MapGridComponent>);
        var brightThroughStealth = default(Entity<MapGridComponent>);
        var brightBehindBright = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = mapManager.CreateGridEntity(map.MapId);
            AddTiles(mapSystem, gridA, 3, 3);

            stealthBlocker = mapManager.CreateGridEntity(map.MapId);
            AddTiles(mapSystem, stealthBlocker, 2, 8);
            brightBlocker = mapManager.CreateGridEntity(map.MapId);
            AddTiles(mapSystem, brightBlocker, 2, 8);
            brightThroughStealth = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            brightBehindBright = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Blockers hug the sensor (wide shadow); targets sit far behind them.
            xformSystem.SetLocalPosition(stealthBlocker.Owner, new Vector2(4f, -4f));
            xformSystem.SetLocalPosition(brightBlocker.Owner, new Vector2(-6f, -4f));
            xformSystem.SetLocalPosition(brightThroughStealth.Owner, new Vector2(40f, 0f));
            xformSystem.SetLocalPosition(brightBehindBright.Owner, new Vector2(-40f, 0f));

            // Occluder walls that fully shadow each target from the sensor.
            SpawnOccluderColumn(entManager, stealthBlocker.Owner, 0, 1, 7); // world x=4,  y=-3..3
            SpawnOccluderColumn(entManager, brightBlocker.Owner, 1, 1, 7);  // world x=-5, y=-3..3

            // The bright blocker also reflects (RCS 100), so it occludes; the stealth
            // blocker carries no RCS (below floor) and is transparent to radar.
            SpawnRadarWall(entManager, xformSystem, "KsRadarTestWallHot", brightBlocker.Owner, 0, 0);

            SpawnRadarWall(entManager, xformSystem, "KsRadarTestWallHot", brightThroughStealth.Owner, 0, 0);
            SpawnRadarWall(entManager, xformSystem, "KsRadarTestWallHot", brightBehindBright.Owner, 0, 0);

            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool),
                "the radar grid never built a contact pool");
            Assert.Multiple(() =>
            {
                Assert.That(pool!.Contacts.ContainsKey(brightThroughStealth.Owner),
                    "a bright ship behind a stealthy (below-floor RCS) grid should be seen: the ray bleeds through");
                Assert.That(pool.Contacts.ContainsKey(brightBehindBright.Owner), Is.False,
                    "a bright ship behind a bright (detectable) grid must stay hidden: that hull still blocks radar");
            });
        });
    }

    /// <summary>
    ///     Bleed-through follows what the radar can actually perceive, not the declared RCS
    ///         floor. A grid at <see cref="KsRadarComponent.MinDetectable"/> but under the RCS
    ///         its taper needs to yield any range is undetectable however close it sits, so it
    ///         must not shadow the target behind it either. The mirror of
    ///         <c>KsIrstSystemTest.TestIrstBleedsThroughGridBelowItsTaperFloor</c>: the two
    ///         systems compute the transparency floor separately.
    /// </summary>
    [Test]
    public async Task TestRadarBleedsThroughGridBelowItsTaperFloor()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var dimBlocker = default(Entity<MapGridComponent>);
        var brightTarget = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = mapManager.CreateGridEntity(map.MapId);
            AddTiles(mapSystem, gridA, 3, 3);

            dimBlocker = mapManager.CreateGridEntity(map.MapId);
            AddTiles(mapSystem, dimBlocker, 2, 8);
            brightTarget = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(dimBlocker.Owner, new Vector2(4f, -4f));
            xformSystem.SetLocalPosition(brightTarget.Owner, new Vector2(40f, 0f));

            SpawnOccluderColumn(entManager, dimBlocker.Owner, 0, 1, 7);

            // RCS 2 on a corner wall (5 exposed sides) sums to 10: exactly the sensor's
            // minDetectable, yet effRange(10) = 100 - 2*(80 - 10) clamps to 0, so the sweep can
            // never resolve it however close it sits.
            SpawnRadarWall(entManager, xformSystem, "KsRadarTestWallLow", dimBlocker.Owner, 0, 0);
            SpawnRadarWall(entManager, xformSystem, "KsRadarTestWallHot", brightTarget.Owner, 0, 0);

            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool),
                "the radar grid never built a contact pool");
            Assert.Multiple(() =>
            {
                Assert.That(pool!.Contacts.ContainsKey(dimBlocker.Owner), Is.False,
                    "the blocker sits below the taper floor, so it must not be detected at all");
                Assert.That(pool.Contacts.ContainsKey(brightTarget.Owner),
                    "a bright ship behind a grid the radar cannot perceive should be seen: the ray bleeds through");
            });
        });
    }

    /// <summary>
    ///     The <see cref="KsRadarComponent.MinDetectable"/> gate in isolation, the mirror of
    ///         <c>KsIrstSystemTest.TestMinDetectableFloorRejectsBelowItAlone</c>. This sensor's
    ///         taper never binds, so the floor is the only thing that can reject a target, and
    ///         the pair straddles the boundary: the gate is a strict less-than, so an RCS equal
    ///         to the floor must be detected and one under it must not.
    /// </summary>
    [Test]
    public async Task TestRadarMinDetectableFloorRejectsBelowItAlone()
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

            // Both sit far inside the shallow taper's effective range (>= 104 for any RCS), so
            // distance cannot be what separates them.
            xformSystem.SetLocalPosition(gridAtFloor.Owner, new Vector2(40f, 0f));
            xformSystem.SetLocalPosition(gridBelowFloor.Owner, new Vector2(-40f, 0f));

            // Corner walls, so the grid RCS is 5x the per-wall value: 2 -> 10 (exactly
            // minDetectable) and 1 -> 5 (under it).
            SpawnRadarWall(entManager, xformSystem, "KsRadarTestWallLow", gridAtFloor.Owner, 0, 0);
            SpawnRadarWall(entManager, xformSystem, "KsRadarTestWallCold", gridBelowFloor.Owner, 0, 0);

            entManager.SpawnEntity("KsRadarTestFloorSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool),
                "the radar grid never built a contact pool");
            Assert.Multiple(() =>
            {
                Assert.That(pool!.Contacts.ContainsKey(gridAtFloor.Owner),
                    "an RCS exactly at minDetectable is not below it, so it must be detected");
                Assert.That(pool.Contacts.ContainsKey(gridBelowFloor.Owner), Is.False,
                    "an RCS under minDetectable must be rejected by the floor gate, which is the only gate this fixture can trip");
            });
        });
    }

    /// <summary>
    ///     ELINT hears an active emitter without emitting itself: an emitting radar on
    ///         another grid appears as an Elint-tier, Bearing-quality contact (the pool
    ///         record holds the emitter's true position; the source only resolves a
    ///         direction), carrying the emitter's own detection range as its EMITTER
    ///         RANGE readout.
    /// </summary>
    [Test]
    public async Task TestElintHearsEmittingRadar()
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

            // gridR emits (radar, cone reach 2*100 = 200); gridE is well inside that.
            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(120f, 0f));

            entManager.SpawnEntity("KsRadarTestElint", new EntityCoordinates(gridE.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridE.Owner, out var pool),
                "the ELINT grid never built a contact pool");
            Assert.That(pool!.Contacts.ContainsKey(gridR.Owner),
                "ELINT should have located the emitting radar");

            var elintSource = pool.Contacts[gridR.Owner].Sources.Values.First();
            Assert.Multiple(() =>
            {
                Assert.That(elintSource.Type, Is.EqualTo(KsSensorType.Elint),
                    "a located radar emitter is an Elint-tier return");
                Assert.That(elintSource.Quality, Is.EqualTo(KsPositionQuality.Bearing),
                    "the shipped ELINT resolves a bearing, not a fix");
                Assert.That(elintSource.Intel, Is.Not.Null, "the ELINT return carried no intel");
                Assert.That(elintSource.Intel!["KsIntelEmitterRange"], Is.EqualTo("100m"),
                    "ELINT reports the emitting radar's own detection range");
            });
        });
    }

    /// <summary>
    ///     ELINT is self-blinded while its own grid runs any active emitter: with its
    ///         own radar switched on it hears nothing, so an enemy radar it would
    ///         otherwise locate (and which its own radar cannot see, being RCS-blank)
    ///         never enters its pool.
    /// </summary>
    [Test]
    public async Task TestElintSelfBlindedByOwnEmitter()
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

            // gridE runs BOTH an ELINT and its own radar (RCS-blank gridR is invisible
            // to that radar), so the only way gridR could appear is via ELINT.
            entManager.SpawnEntity("KsRadarTestElint", new EntityCoordinates(gridE.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridE.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var hasPool = entManager.TryGetComponent<KsSensorContactPoolComponent>(gridE.Owner, out var pool);
            Assert.That(!hasPool || !pool!.Contacts.ContainsKey(gridR.Owner),
                "an ELINT on a grid running its own radar must be deaf: the enemy emitter must not appear");
        });
    }

    /// <summary>
    ///     A jammer whose slice covers a radar ship (outside its burn-through range)
    ///         blinds that radar: a target it was tracking goes stale, the grid reads as
    ///         jammed, and the radar reveals the jammer once via a home-on-jam return
    ///         (a Jammer-tier contact of the jammer's grid).
    /// </summary>
    [Test]
    public async Task TestJammerBlindsRadarWithHomeOnJam()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var updateIntervalSeconds = cfg.GetCVar(KsCCVars.SensorsUpdateInterval);
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var sensors = entManager.System<KsSensorSystem>();

        var map = await Pair.CreateTestMap();

        var gridR = default(Entity<MapGridComponent>);
        var gridT = default(Entity<MapGridComponent>);
        var gridJ = default(Entity<MapGridComponent>);
        var radarUid = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId); // CoM ~ (4,4)
            gridT = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridJ = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Move every grid off the shared origin BEFORE mounting anything, or a fresh
            // grid overlapping at (0,0) parents the spawned machine to the wrong grid.
            xformSystem.SetLocalPosition(gridT.Owner, new Vector2(50f, 0f));
            xformSystem.SetLocalPosition(gridJ.Owner, new Vector2(90f, 0f));

            // A bright target in radar range.
            SpawnRadarWall(entManager, xformSystem, "KsRadarTestWallHot", gridT.Owner, 0, 0);

            radarUid = entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridR.Owner, out var pool)
                && pool!.Contacts.ContainsKey(gridT.Owner),
                "the radar should track the target before jamming");
            Assert.That(sensors.IsGridJammed(gridR.Owner), Is.False, "not jammed before any jammer exists");
        });

        // Bring up a jammer whose omni slice (power 120) covers gridR's CoM at ~86 m,
        // outside burn-through (120 * 0.5 = 60). gridJ is already positioned at (90,0).
        await server.WaitPost(() =>
        {
            entManager.SpawnEntity("KsRadarTestJammer", new EntityCoordinates(gridJ.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(50);

        await server.WaitAssertion(() =>
        {
            Assert.That(sensors.IsGridJammed(gridR.Owner),
                "the radar ship is inside the jam slice and beyond burn-through, so it must read jammed");

            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridR.Owner, out var pool));

            // A live radar refreshes every sensor tick, so a gap of two-plus ticks proves
            // the jammed radar produced no fresh returns.
            Assert.That(pool!.Contacts.TryGetValue(gridT.Owner, out var targetRecord), "the target ghost should linger");
            var radarSource = targetRecord!.Sources[radarUid];
            Assert.That((timing.CurTime - radarSource.LastSeen).TotalSeconds, Is.GreaterThan(updateIntervalSeconds * 2),
                "a jammed radar produces no fresh returns, so the target's radar source must have gone stale");

            Assert.That(pool.Contacts.TryGetValue(gridJ.Owner, out var jammerRecord),
                "the jammed radar must reveal the jammer once via home-on-jam");
            Assert.That(jammerRecord!.Sources.Values.Any(s => s.Type == KsSensorType.Jammer),
                "the home-on-jam return must be classified as a Jammer return");
            Assert.That(jammerRecord.Sources.Values.Where(s => s.Type == KsSensorType.Jammer).All(s => s.Quality == KsPositionQuality.Bearing),
                "a jammed set knows the noise direction, not the range: home-on-jam is Bearing quality");
        });
    }

    /// <summary>
    ///     A jammer that would blind a radar from afar no longer does once the radar ship
    ///         is within JammingPower * BurnThroughFactor of it.
    /// </summary>
    [Test]
    public async Task TestRadarBurnsThroughCloseJammer()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var sensors = entManager.System<KsSensorSystem>();

        var map = await Pair.CreateTestMap();

        var gridR = default(Entity<MapGridComponent>);
        var gridT = default(Entity<MapGridComponent>);
        var gridJ = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId); // CoM ~ (4,4)
            gridT = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridJ = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridT.Owner, new Vector2(50f, 0f));
            SpawnRadarWall(entManager, xformSystem, "KsRadarTestWallHot", gridT.Owner, 0, 0);

            // Jammer close enough (~36 m to gridR CoM) to be inside burn-through (60).
            xformSystem.SetLocalPosition(gridJ.Owner, new Vector2(40f, 4f));
            entManager.SpawnEntity("KsRadarTestJammer", new EntityCoordinates(gridJ.Owner, new Vector2(0.5f, 0.5f)));

            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(sensors.IsGridJammed(gridR.Owner), Is.False,
                "a radar inside the jammer's burn-through range must not be jammed");
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridR.Owner, out var pool)
                && pool!.Contacts.ContainsKey(gridT.Owner),
                "an un-jammed radar keeps tracking its target normally");
        });
    }

    /// <summary>
    ///     The console "JAMMED" indicator must clear on the un-jam falling edge even on
    ///         a quiet grid, where that transition mutates no contact pool: the jam-state
    ///         change forces a console re-push, so the indicator does not latch on stale
    ///         after the jammer is gone.
    /// </summary>
    [Test]
    public async Task TestJammedIndicatorClearsWhenJammerRemoved()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var uiSystem = entManager.System<SharedUserInterfaceSystem>();
        var sensors = entManager.System<KsSensorSystem>();

        var map = await Pair.CreateTestMap();

        var gridR = default(Entity<MapGridComponent>);
        var gridJ = default(Entity<MapGridComponent>);
        var console = default(EntityUid);
        var jammerUid = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridR = mapManager.CreateGridEntity(map.MapId);
            AddTiles(mapSystem, gridR, 8, 8);
            gridJ = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridJ.Owner, new Vector2(90f, 0f));

            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
            jammerUid = entManager.SpawnEntity("KsRadarTestJammer", new EntityCoordinates(gridJ.Owner, new Vector2(0.5f, 0.5f)));

            console = entManager.SpawnEntity("KsRadarTestShuttleConsole", new EntityCoordinates(gridR.Owner, new Vector2(2.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

            var actor = entManager.SpawnEntity(null, new EntityCoordinates(gridR.Owner, new Vector2(2.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });

        // Let jamming settle and the one-shot home-on-jam contact decay to a stable ghost,
        // so the falling edge that follows mutates no contact pool.
        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(sensors.IsGridJammed(gridR.Owner), "the radar should be jammed while the jammer is up");
            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state));
            Assert.That(state!.NavState.KsSensorNav?.Jammed ?? false, "the console should show JAMMED while jammed");
        });

        // On this quiet grid the un-jam transition changes no contact pool, so only the
        // forced push clears the indicator.
        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(jammerUid);
        });

        await Pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            Assert.That(sensors.IsGridJammed(gridR.Owner), Is.False, "removing the jammer must un-jam the radar");
            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state));
            Assert.That(state!.NavState.KsSensorNav?.Jammed ?? false, Is.False,
                "the JAMMED indicator must clear once the jammer is gone, even on a quiet grid where no contact changed");
        });
    }

    /// <summary>
    ///     Switching a grid's jammer off stops it jamming (an enemy radar in its slice
    ///         springs back), switching it on resumes, and
    ///         <see cref="KsSensorSystem.GridJammerState"/> reports presence and on/off
    ///         state throughout, which is what drives the console button.
    /// </summary>
    [Test]
    public async Task TestJammerToggleStopsAndResumesJamming()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var sensors = entManager.System<KsSensorSystem>();

        var map = await Pair.CreateTestMap();

        var gridR = default(Entity<MapGridComponent>);
        var gridJ = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId); // CoM ~ (4,4)
            gridJ = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Jammer at ~86 m from gridR's CoM: inside its omni slice (power 120), beyond
            // burn-through (60), so an enabled jammer jams the radar ship.
            xformSystem.SetLocalPosition(gridJ.Owner, new Vector2(90f, 0f));

            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestJammer", new EntityCoordinates(gridJ.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.That(sensors.IsGridJammed(gridR.Owner), "an enabled jammer in range must jam the radar");
            Assert.That(sensors.GridJammerState(gridJ.Owner), Is.EqualTo((true, true)),
                "the jammer grid should read as mounting a jammer that is switched on");
        });

        await server.WaitPost(() => sensors.ToggleGridJammer(gridJ.Owner));
        await Pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            Assert.That(sensors.IsGridJammed(gridR.Owner), Is.False,
                "a switched-off jammer must stop jamming: the radar springs back");
            Assert.That(sensors.GridJammerState(gridJ.Owner), Is.EqualTo((true, false)),
                "the jammer is still mounted but now reads off");
        });

        await server.WaitPost(() => sensors.ToggleGridJammer(gridJ.Owner));
        await Pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            Assert.That(sensors.IsGridJammed(gridR.Owner), "toggling the jammer back on must resume jamming");
            Assert.That(sensors.GridJammerState(gridJ.Owner), Is.EqualTo((true, true)), "the jammer reads on again");
        });
    }

    /// <summary>
    ///     Radar and jammer are mutually exclusive on a grid: switching the jammer on
    ///         silences the radar, switching the radar on silences the jammer, and the
    ///         effect is real (an enemy radar in the jammer's slice is jammed only while
    ///         the jammer is the active emitter).
    /// </summary>
    [Test]
    public async Task TestRadarToggleSilencesJammerAndViceVersa()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var sensors = entManager.System<KsSensorSystem>();

        var map = await Pair.CreateTestMap();

        var gridG = default(Entity<MapGridComponent>); // ours: radar + jammer
        var gridE = default(Entity<MapGridComponent>); // enemy: emitting radar in our jam slice
        var radarUid = default(EntityUid);
        var jammerUid = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridG = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridE = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Enemy radar ship ~90 m off: inside our omni jam slice (power 120), beyond
            // burn-through (60), so it is jammed exactly when our jammer is the emitter.
            xformSystem.SetLocalPosition(gridE.Owner, new Vector2(90f, 0f));

            radarUid = entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridG.Owner, new Vector2(0.5f, 0.5f)));
            jammerUid = entManager.SpawnEntity("KsRadarTestJammer", new EntityCoordinates(gridG.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridE.Owner, new Vector2(0.5f, 0.5f)));

            // Start from a clean, non-conflicting state: radar active, jammer silent.
            entManager.GetComponent<KsSensorComponent>(radarUid).Enabled = true;
            entManager.GetComponent<KsJammerComponent>(jammerUid).Enabled = false;
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(sensors.GridRadarState(gridG.Owner).Active, "radar starts active");
            Assert.That(sensors.GridJammerState(gridG.Owner).Active, Is.False, "jammer starts silent");
            Assert.That(sensors.IsGridJammed(gridE.Owner), Is.False, "our jammer is off, so the enemy radar is not jammed");
        }));

        await server.WaitPost(() => sensors.ToggleGridJammer(gridG.Owner));
        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(sensors.GridJammerState(gridG.Owner).Active, "the jammer is now active");
            Assert.That(sensors.GridRadarState(gridG.Owner).Active, Is.False, "turning the jammer on silenced the radar");
            Assert.That(sensors.IsGridJammed(gridE.Owner), "the active jammer jams the enemy radar");
        }));

        await server.WaitPost(() => sensors.ToggleGridRadar(gridG.Owner));
        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(sensors.GridRadarState(gridG.Owner).Active, "the radar is now active again");
            Assert.That(sensors.GridJammerState(gridG.Owner).Active, Is.False, "turning the radar on silenced the jammer");
            Assert.That(sensors.IsGridJammed(gridE.Owner), Is.False, "the silenced jammer no longer jams the enemy radar");
        }));
    }

    /// <summary>
    ///     The RebuildEmissions safety net: if a grid is forced into the invalid both-on
    ///         state the console toggles normally prevent (map/VV/admin), radar wins, so
    ///         the radar emits and the jammer stays silent, never both at once.
    /// </summary>
    [Test]
    public async Task TestRadarWinsWhenBothForcedOn()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var sensors = entManager.System<KsSensorSystem>();

        var map = await Pair.CreateTestMap();

        var gridG = default(Entity<MapGridComponent>);
        var gridE = default(Entity<MapGridComponent>);
        var radarUid = default(EntityUid);
        var jammerUid = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridG = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridE = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridE.Owner, new Vector2(90f, 0f));

            radarUid = entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridG.Owner, new Vector2(0.5f, 0.5f)));
            jammerUid = entManager.SpawnEntity("KsRadarTestJammer", new EntityCoordinates(gridG.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridE.Owner, new Vector2(0.5f, 0.5f)));

            // Force the invalid state the toggles normally prevent: both emitters switched on.
            entManager.GetComponent<KsSensorComponent>(radarUid).Enabled = true;
            entManager.GetComponent<KsJammerComponent>(jammerUid).Enabled = true;
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(sensors.RadarEmissions.Any(r => r.Grid == gridG.Owner), "the radar still emits");
            Assert.That(sensors.JammerEmissions.Any(j => j.Grid == gridG.Owner), Is.False,
                "radar wins: the jammer is suppressed while the radar emits, even with both switched on");
            Assert.That(sensors.IsGridJammed(gridE.Owner), Is.False,
                "the suppressed jammer does not jam the enemy radar");
        }));
    }

    /// <summary>
    ///     A directional jammer only blinds radars inside its wedge, and the wedge follows
    ///         the mount's rotation: two radar ships sit at equal range either side of the
    ///         jammer, and rotating the mount 180 degrees must swap which one is blinded.
    ///     The only case that exercises the arc at all: with the omni test jammer
    ///         (halfAngle 180) the angular comparison is a tautology, so the degrees to
    ///         radians conversion, the angle wrap and the mount rotation all go untested.
    /// </summary>
    [Test]
    public async Task TestDirectionalJammerArcFollowsMountRotation()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var sensors = entManager.System<KsSensorSystem>();

        var map = await Pair.CreateTestMap();

        var uiSystem = entManager.System<SharedUserInterfaceSystem>();

        var gridJ = default(Entity<MapGridComponent>);
        var gridEast = default(Entity<MapGridComponent>);
        var gridWest = default(Entity<MapGridComponent>);
        var jammerUid = default(EntityUid);
        var console = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridJ = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridEast = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridWest = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // One radar ship due east of the jammer mount (93.6 m to its centre of mass) and
            // one due west (86.6 m). Both are well inside the 120 m reach and well outside
            // burn-through (120 * 0.5 = 60), so range cannot separate them and only the arc
            // decides. Their bearings are 2.1 and 177.7 degrees off the mount's facing
            // against a 45 degree half-angle, so neither comparison is anywhere near the edge.
            xformSystem.SetLocalPosition(gridEast.Owner, new Vector2(90f, 0f));
            xformSystem.SetLocalPosition(gridWest.Owner, new Vector2(-90f, 0f));

            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridEast.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridWest.Owner, new Vector2(0.5f, 0.5f)));

            jammerUid = entManager.SpawnEntity("KsRadarTestJammerNarrow", new EntityCoordinates(gridJ.Owner, new Vector2(0.5f, 0.5f)));
            xformSystem.SetWorldRotation(jammerUid, Angle.Zero);

            // The jammer ship carries no sensors, so its contact pool never changes: the
            // drawn wedge can only refresh through the rotation-forced console push.
            console = entManager.SpawnEntity("KsRadarTestShuttleConsole", new EntityCoordinates(gridJ.Owner, new Vector2(2.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

            var actor = entManager.SpawnEntity(null, new EntityCoordinates(gridJ.Owner, new Vector2(2.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });

        await Pair.RunTicksSync(40);

        var wedgeBefore = default(List<Vector2>);

        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(sensors.IsGridJammed(gridEast.Owner),
                "the ship on the mount's facing bearing is inside the 45 degree wedge and must be jammed");
            Assert.That(sensors.IsGridJammed(gridWest.Owner), Is.False,
                "the ship behind the mount is in the blind rear and must not be jammed");

            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state));
            var wedge = state!.NavState.KsSensorNav?.Regions?.FirstOrDefault(r => r.Type == KsSensorType.Jammer);
            Assert.That(wedge, Is.Not.Null, "the jammer's wedge should be drawn");
            Assert.That(wedge!.WorldOffsets, Is.False,
                "a jam wedge follows its mount, so it must stay fully grid-local: world-oriented offsets would leave it behind while the ship spins");
            wedgeBefore = wedge.Points;
        }));

        await server.WaitPost(() =>
        {
            xformSystem.SetWorldRotation(jammerUid, new Angle(Math.PI));
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(sensors.IsGridJammed(gridWest.Owner),
                "after rotating the mount 180 degrees the wedge must cover the western ship");
            Assert.That(sensors.IsGridJammed(gridEast.Owner), Is.False,
                "and the eastern ship must fall into the new blind rear");

            // The crew aims by console readout, so the drawn wedge must follow the mount
            // on a grid where nothing else forces a push.
            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state));
            var wedge = state!.NavState.KsSensorNav?.Regions?.FirstOrDefault(r => r.Type == KsSensorType.Jammer);
            Assert.That(wedge, Is.Not.Null);
            Assert.That(wedge!.Points, Is.Not.EqualTo(wedgeBefore),
                "rotating the mount must re-push the console, or it keeps drawing the old bearing forever");
        }));
    }

    /// <summary>
    ///     A datalink self-report must carry the ally's own motion through to the console.
    ///         It is filed at the top tier, so it wins the per-source contest outright: with
    ///         no velocity every allied ship on the datalink would render as stationary,
    ///         including one an own sensor is simultaneously tracking correctly.
    /// </summary>
    [Test]
    public async Task TestDatalinkSelfReportCarriesVelocity()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var uiSystem = entManager.System<SharedUserInterfaceSystem>();
        var physicsSystem = entManager.System<SharedPhysicsSystem>();

        var map = await Pair.CreateTestMap();

        var gridRx = default(Entity<MapGridComponent>);
        var gridTx = default(Entity<MapGridComponent>);
        var console = default(EntityUid);
        var txNet = default(NetEntity);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridRx = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridTx = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridTx.Owner, new Vector2(60f, 0f));

            entManager.SpawnEntity("KsRadarTestDatalinkTx", new EntityCoordinates(gridTx.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestDatalinkRx", new EntityCoordinates(gridRx.Owner, new Vector2(0.5f, 0.5f)));

            console = entManager.SpawnEntity("KsRadarTestShuttleConsole", new EntityCoordinates(gridRx.Owner, new Vector2(2.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

            var actor = entManager.SpawnEntity(null, new EntityCoordinates(gridRx.Owner, new Vector2(2.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);

            physicsSystem.SetLinearVelocity(gridTx.Owner, new Vector2(0f, 5f));
            txNet = entManager.GetNetEntity(gridTx.Owner);
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state));
            var contact = state!.NavState.KsSensorNav?.Contacts?.FirstOrDefault(c => c.Grid == txNet);
            Assert.That(contact, Is.Not.Null, "the receiver should hear the transmitter's self-report");
            Assert.That(contact!.LinearVelocity.Length(), Is.GreaterThan(0.01f),
                "a self-reporting ally's own motion must reach the console, not be zeroed by the source record");
        });
    }

    /// <summary>
    ///     ELINT hears a jammer, files it as a Jammer return rather than an Elint one, and
    ///         does so THROUGH terrain: jamming is broadband noise that ignores line of
    ///         sight, the deliberate asymmetry against the radar branch (which an occluder
    ///         does block).
    /// </summary>
    [Test]
    public async Task TestElintHearsJammerThroughOccluder()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridE = default(Entity<MapGridComponent>);
        var gridJ = default(Entity<MapGridComponent>);
        var gridBlock = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridE = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridJ = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridBlock = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridJ.Owner, new Vector2(60f, 0f));

            // A solid occluder wall squarely between the two, which would deafen the ELINT
            // to a radar at this spot but must not deafen it to a jammer.
            xformSystem.SetLocalPosition(gridBlock.Owner, new Vector2(30f, 0f));
            SpawnOccluderColumn(entManager, gridBlock.Owner, 4, 0, 7);

            entManager.SpawnEntity("KsRadarTestElint", new EntityCoordinates(gridE.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestJammer", new EntityCoordinates(gridJ.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridE.Owner, out var pool));
            Assert.That(pool!.Contacts.TryGetValue(gridJ.Owner, out var record),
                "ELINT must locate a jamming emitter even with terrain in the way");
            Assert.That(record!.Sources.Values.Any(s => s.Type == KsSensorType.Jammer),
                "a located jammer files as a Jammer return, not as an Elint one");
        });
    }

    /// <summary>
    ///     ELINT is deafened to a RADAR emitter by terrain, the other half of the asymmetry
    ///         in <see cref="TestElintHearsJammerThroughOccluder"/>: a radar beam is
    ///         occluded, so an ELINT sitting in the emitter's shadow hears nothing even
    ///         well inside its reach.
    /// </summary>
    [Test]
    public async Task TestElintDeafToOccludedRadar()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridE = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);
        var gridBlock = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridE = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridBlock = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Well inside the radar's cone reach (100 * 2 = 200), so only occlusion can
            // explain a miss.
            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(60f, 0f));
            xformSystem.SetLocalPosition(gridBlock.Owner, new Vector2(30f, 0f));
            SpawnOccluderColumn(entManager, gridBlock.Owner, 4, 0, 7);

            entManager.SpawnEntity("KsRadarTestElint", new EntityCoordinates(gridE.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            var heard = entManager.TryGetComponent<KsSensorContactPoolComponent>(gridE.Owner, out var pool)
                        && pool!.Contacts.ContainsKey(gridR.Owner);

            Assert.That(heard, Is.False,
                "a radar beam is blocked by terrain, so an ELINT in the emitter's shadow must not hear it");
        });
    }

    /// <summary>
    ///     IgnoreFraction is the only knob separating a crude ELINT from an advanced one.
    ///         Two listeners at the same distance from one emitter: the sensitive one
    ///         (fraction 0) hears it, the crude one (fraction 0.6, so reach shrinks to the
    ///         inner 40% of the cone) does not.
    /// </summary>
    [Test]
    public async Task TestElintIgnoreFractionLimitsReach()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridFine = default(Entity<MapGridComponent>);
        var gridCrude = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridFine = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridCrude = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Cone reach is 100 * 2 = 200. Sensitive reach 200, crude reach 200 * 0.4 = 80.
            // Both listeners sit ~120 m from the emitter: inside one, outside the other.
            xformSystem.SetLocalPosition(gridFine.Owner, new Vector2(120f, 0f));
            xformSystem.SetLocalPosition(gridCrude.Owner, new Vector2(-120f, 0f));

            entManager.SpawnEntity("KsRadarTestElint", new EntityCoordinates(gridFine.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestElintCrude", new EntityCoordinates(gridCrude.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridFine.Owner, out var finePool)
                        && finePool!.Contacts.ContainsKey(gridR.Owner),
                "a sensitive ELINT hears an emitter across nearly its whole cone");

            var crudeHeard = entManager.TryGetComponent<KsSensorContactPoolComponent>(gridCrude.Owner, out var crudePool)
                             && crudePool!.Contacts.ContainsKey(gridR.Owner);
            Assert.That(crudeHeard, Is.False,
                "a crude ELINT only picks an emitter up once well inside the cone");
        }));
    }

    /// <summary>
    ///     ELINT is deaf while its own grid runs a JAMMER, not just its own radar: the
    ///         self-blind set is fed by both emitter kinds, so any active emission
    ///         drowns it.
    /// </summary>
    [Test]
    public async Task TestElintSelfBlindedByOwnJammer()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridE = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);
        var jammerUid = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridE = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(60f, 0f));

            entManager.SpawnEntity("KsRadarTestElint", new EntityCoordinates(gridE.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
            jammerUid = entManager.SpawnEntity("KsRadarTestJammer", new EntityCoordinates(gridE.Owner, new Vector2(2.5f, 0.5f)));
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            var heard = entManager.TryGetComponent<KsSensorContactPoolComponent>(gridE.Owner, out var pool)
                        && pool!.Contacts.ContainsKey(gridR.Owner);

            Assert.That(heard, Is.False, "an ELINT cannot hear whispers while its own jammer is shouting");
        });

        await server.WaitPost(() =>
        {
            entManager.GetComponent<KsJammerComponent>(jammerUid).Enabled = false;
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridE.Owner, out var pool)
                        && pool!.Contacts.ContainsKey(gridR.Owner),
                "silencing the ship's own jammer restores the ELINT's hearing");
        });
    }

    /// <summary>
    ///     Home-on-jam is a ONE-SHOT: the jammed radar reveals the jammer on the tick it
    ///         goes dark and never again. Asserting the contact merely exists proves
    ///         nothing, because records are only ever removed on target deletion, so this
    ///         pins the rising-edge machinery by asserting the return stops being refreshed.
    /// </summary>
    [Test]
    public async Task TestHomeOnJamIsOneShot()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var sensors = entManager.System<KsSensorSystem>();

        var map = await Pair.CreateTestMap();

        var gridR = default(Entity<MapGridComponent>);
        var gridJ = default(Entity<MapGridComponent>);
        var radarUid = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridJ = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridJ.Owner, new Vector2(90f, 0f));

            radarUid = entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestJammer", new EntityCoordinates(gridJ.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(40);

        var firstSeen = default(TimeSpan);

        await server.WaitAssertion(() =>
        {
            Assert.That(sensors.IsGridJammed(gridR.Owner), "the radar must be jammed for home-on-jam to fire");
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridR.Owner, out var pool));
            Assert.That(pool!.Contacts.TryGetValue(gridJ.Owner, out var record),
                "the jammer must be revealed once");

            firstSeen = record!.Sources[radarUid].LastSeen;
        });

        // Many sensor ticks later, still jammed: a repeating return would keep bumping
        // LastSeen forward.
        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(sensors.IsGridJammed(gridR.Owner), "still jammed, so any repeat would have fired by now");
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridR.Owner, out var pool));
            Assert.That(pool!.Contacts.TryGetValue(gridJ.Owner, out var record));
            Assert.That(record!.Sources[radarUid].LastSeen, Is.EqualTo(firstSeen),
                "home-on-jam fires exactly once: the radar goes dark afterwards and never refreshes the return");
        });
    }

    /// <summary>
    ///     Radar wins the radar/jammer tie, and every jammer readout must say so. A grid
    ///         running both (reachable in ordinary play, since a freshly built radar array
    ///         comes up enabled) has its jammers suppressed, so reporting the jammer as ON,
    ///         or drawing its wedge, would tell the crew they are jamming when they are not.
    /// </summary>
    [Test]
    public async Task TestSuppressedJammerReportsOffAndDrawsNoCone()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var uiSystem = entManager.System<SharedUserInterfaceSystem>();
        var sensors = entManager.System<KsSensorSystem>();

        var map = await Pair.CreateTestMap();

        var gridG = default(Entity<MapGridComponent>);
        var console = default(EntityUid);
        var radarUid = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridG = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            entManager.SpawnEntity("KsRadarTestJammer", new EntityCoordinates(gridG.Owner, new Vector2(1.5f, 0.5f)));

            console = entManager.SpawnEntity("KsRadarTestShuttleConsole", new EntityCoordinates(gridG.Owner, new Vector2(2.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

            var actor = entManager.SpawnEntity(null, new EntityCoordinates(gridG.Owner, new Vector2(2.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(sensors.GridJammerState(gridG.Owner).Active, "the jammer alone on the grid is active");
            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state));
            Assert.That(state!.NavState.KsSensorNav?.JammerActive ?? false, "the console reports it as ON");
            Assert.That(state.NavState.KsSensorNav?.Regions?.Any(r => r.Type == KsSensorType.Jammer) == true,
                "and draws its wedge");
        }));

        // A radar array comes up enabled, so from the next tick the jamming ship's
        // jammers are suppressed by the radar-wins rule.
        await server.WaitPost(() =>
        {
            radarUid = entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridG.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(entManager.GetComponent<KsSensorComponent>(radarUid).Enabled,
                "a freshly built radar array comes up enabled, which is what makes this reachable");
            Assert.That(sensors.IsGridJammerSuppressed(gridG.Owner), "the radar suppresses the grid's jammers");
            Assert.That(sensors.GridJammerState(gridG.Owner).Active, Is.False,
                "a suppressed jammer must not report as active");

            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state));
            Assert.That(state!.NavState.KsSensorNav?.JammerActive ?? false, Is.False,
                "the console must not claim JAMMER: ON while the radar is silencing it");
            Assert.That(state.NavState.KsSensorNav?.Regions?.Any(r => r.Type == KsSensorType.Jammer), Is.Not.True,
                "and must not draw a jam wedge the ship is not projecting");
        }));
    }

    /// <summary>
    ///     A velocity-hiding sensor must not erase motion a revealing one reported. The
    ///         gated velocity rides the per-source record rather than the shared contact
    ///         record, so an ELINT co-tracking a target an IRST also sees (a hot,
    ///         radar-emitting ship) cannot zero the heading the IRST earned.
    /// </summary>
    [Test]
    public async Task TestElintDoesNotEraseCoTrackedVelocity()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var uiSystem = entManager.System<SharedUserInterfaceSystem>();
        var physicsSystem = entManager.System<SharedPhysicsSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridT = default(Entity<MapGridComponent>);
        var console = default(EntityUid);
        var targetNet = default(NetEntity);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridT = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridT.Owner, new Vector2(60f, 0f));

            // The target is hot (so the IRST sees it) and emits radar (so the ELINT hears
            // it), which is what puts two sources with different velocity policies on one
            // contact record.
            SpawnRadarWall(entManager, xformSystem, "KsRadarTestWallDual", gridT.Owner, 0, 0);
            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridT.Owner, new Vector2(0.5f, 0.5f)));

            entManager.SpawnEntity("KsRadarTestIrst", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestElint", new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));

            console = entManager.SpawnEntity("KsRadarTestShuttleConsole", new EntityCoordinates(gridA.Owner, new Vector2(2.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

            var actor = entManager.SpawnEntity(null, new EntityCoordinates(gridA.Owner, new Vector2(2.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);

            physicsSystem.SetLinearVelocity(gridT.Owner, new Vector2(0f, 5f));
            targetNet = entManager.GetNetEntity(gridT.Owner);
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool));
            Assert.That(pool!.Contacts.TryGetValue(gridT.Owner, out var record));

            var types = record!.Sources.Values.Select(s => s.Type).ToList();
            Assert.That(types, Does.Contain(KsSensorType.IRST), "the IRST must be tracking the target");
            Assert.That(types, Does.Contain(KsSensorType.Elint), "and the ELINT must be hearing its radar");

            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state));
            var contact = state!.NavState.KsSensorNav?.Contacts?.FirstOrDefault(c => c.Grid == targetNet);
            Assert.That(contact, Is.Not.Null, "the console should show the co-tracked contact");
            Assert.That(contact!.LinearVelocity.Length(), Is.GreaterThan(0.01f),
                "the velocity-hiding ELINT source must not zero the heading the IRST reported");
        });
    }

    /// <summary>
    ///     The console radar toggle works over its real BUI message, and the state that
    ///         drives the button's visibility and ON/OFF label rides the nav state. The
    ///         other toggle tests call the system method directly, so nothing otherwise
    ///         covers the subscription wiring or the HasRadar/RadarActive plumbing.
    /// </summary>
    [Test]
    public async Task TestRadarToggleOverConsoleMessage()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var uiSystem = entManager.System<SharedUserInterfaceSystem>();
        var sensors = entManager.System<KsSensorSystem>();

        var map = await Pair.CreateTestMap();

        var gridR = default(Entity<MapGridComponent>);
        var console = default(EntityUid);
        var actor = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));

            console = entManager.SpawnEntity("KsRadarTestShuttleConsole", new EntityCoordinates(gridR.Owner, new Vector2(2.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

            actor = entManager.SpawnEntity(null, new EntityCoordinates(gridR.Owner, new Vector2(2.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state));
            Assert.That(state!.NavState.KsSensorNav?.HasRadar ?? false, "the grid mounts a radar, so the toggle must be offered");
            Assert.That(state.NavState.KsSensorNav?.RadarActive ?? false, "and it comes up emitting");
        }));

        // Stand in for a client's BUI message: populate the fields the engine's receive
        // path fills in, then raise it directed at the console exactly as that path does.
        // Carrying a real UiKey is the point, since the handler is scoped to one key.
        await server.WaitPost(() =>
        {
            var msg = new KsToggleRadarMessage
            {
                Actor = actor,
                Entity = entManager.GetNetEntity(console),
                UiKey = ShuttleConsoleUiKey.Key,
            };

            entManager.EventBus.RaiseLocalEvent(console, (object) msg, true);
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(sensors.GridRadarState(gridR.Owner).Active, Is.False,
                "the console message must reach the handler and silence the grid's radars");
            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state));
            Assert.That(state!.NavState.KsSensorNav?.RadarActive ?? false, Is.False,
                "and the nav state must carry the new OFF label state back to the client");
        }));

        // The same message on the console's OTHER interface must do nothing. The engine
        // validates the sender only against the key the message arrived on and then raises
        // it to every subscriber, so an unscoped handler would accept this and let a
        // wires-panel actor bypass the ID lock and the powered-UI gate.
        await server.WaitPost(() =>
        {
            var msg = new KsToggleRadarMessage
            {
                Actor = actor,
                Entity = entManager.GetNetEntity(console),
                UiKey = WiresUiKey.Key,
            };

            entManager.EventBus.RaiseLocalEvent(console, (object) msg, true);
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.That(sensors.GridRadarState(gridR.Owner).Active, Is.False,
                "a toggle sent on the wires UI key must be ignored, so the radar stays off");
        });
    }

    /// <summary>
    ///     The ESM fit flags ride the nav state: a grid mounting an ELINT array and an
    ///         RWR reports both, a bare-console grid reports neither. The client gates
    ///         the ESM tab's precision/warning panels on exactly these flags, and
    ///         nothing else covers their collect-to-state plumbing.
    /// </summary>
    [Test]
    public async Task TestEsmFitFlagsRideNavState()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var uiSystem = entManager.System<SharedUserInterfaceSystem>();

        var map = await Pair.CreateTestMap();

        var gridFit = default(Entity<MapGridComponent>);
        var gridBare = default(Entity<MapGridComponent>);
        var consoleFit = default(EntityUid);
        var consoleBare = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridFit = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridBare = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            xformSystem.SetLocalPosition(gridBare.Owner, new Vector2(120f, 0f));

            entManager.SpawnEntity("KsRadarTestElint", new EntityCoordinates(gridFit.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestRwr", new EntityCoordinates(gridFit.Owner, new Vector2(1.5f, 0.5f)));

            consoleFit = entManager.SpawnEntity("KsRadarTestShuttleConsole", new EntityCoordinates(gridFit.Owner, new Vector2(2.5f, 0.5f)));
            xformSystem.AnchorEntity((consoleFit, entManager.GetComponent<TransformComponent>(consoleFit)));
            consoleBare = entManager.SpawnEntity("KsRadarTestShuttleConsole", new EntityCoordinates(gridBare.Owner, new Vector2(2.5f, 0.5f)));
            xformSystem.AnchorEntity((consoleBare, entManager.GetComponent<TransformComponent>(consoleBare)));

            var actorFit = entManager.SpawnEntity(null, new EntityCoordinates(gridFit.Owner, new Vector2(2.5f, 0.5f)));
            uiSystem.OpenUi(consoleFit, ShuttleConsoleUiKey.Key, actorFit);
            var actorBare = entManager.SpawnEntity(null, new EntityCoordinates(gridBare.Owner, new Vector2(2.5f, 0.5f)));
            uiSystem.OpenUi(consoleBare, ShuttleConsoleUiKey.Key, actorBare);
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(consoleFit, ShuttleConsoleUiKey.Key, out var fitState));
            Assert.That(fitState!.NavState.KsSensorNav, Is.Not.Null);
            Assert.That(fitState.NavState.KsSensorNav!.HasElint, "the grid mounts an ELINT array, so the precision panels must unlock");
            Assert.That(fitState.NavState.KsSensorNav.HasRwr, "the grid mounts an RWR, so the warning panels must unlock");

            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(consoleBare, ShuttleConsoleUiKey.Key, out var bareState));
            Assert.That(bareState!.NavState.KsSensorNav, Is.Not.Null);
            Assert.That(bareState.NavState.KsSensorNav!.HasElint, Is.False, "a grid without an ELINT array must degrade the precision panels");
            Assert.That(bareState.NavState.KsSensorNav.HasRwr, Is.False, "a grid without an RWR must degrade the warning panels");
        }));
    }

    /// <summary>
    ///     A datalinked ally's coverage cone rides the console's nav state marked
    ///         Relayed, with its points transformed into the CONSOLE grid's local
    ///         frame server-side (the ally can be beyond PVS, so the client could
    ///         never place the cone itself). This feeds the sector map's network
    ///         picture; the radar tab filters Relayed cones out client-side.
    /// </summary>
    [Test]
    public async Task TestRelayedAllyConeRidesNavStateConsoleLocal()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var uiSystem = entManager.System<SharedUserInterfaceSystem>();

        var map = await Pair.CreateTestMap();

        var gridC = default(Entity<MapGridComponent>);
        var gridA = default(Entity<MapGridComponent>);
        var console = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridC = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            xformSystem.SetLocalPosition(gridA.Owner, new Vector2(120f, 0f));

            // The ally runs an emitting radar (its coverage cone) and relays over
            // datalink; the console grid only listens.
            entManager.SpawnEntity("KsRadarTestSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestDatalinkTx", new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsRadarTestDatalinkRx", new EntityCoordinates(gridC.Owner, new Vector2(0.5f, 0.5f)));

            console = entManager.SpawnEntity("KsRadarTestShuttleConsole", new EntityCoordinates(gridC.Owner, new Vector2(2.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));

            var actor = entManager.SpawnEntity(null, new EntityCoordinates(gridC.Owner, new Vector2(2.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(uiSystem.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state));
            var regions = state!.NavState.KsSensorNav?.Regions;
            Assert.That(regions, Is.Not.Null, "the datalinked ally's coverage must reach the console's nav state");

            var relayed = regions!.Find(r => r.Relayed);
            Assert.That(relayed, Is.Not.Null, "the ally's cone must arrive marked Relayed");
            Assert.That(relayed!.Grid, Is.EqualTo(entManager.GetNetEntity(gridC.Owner)),
                "relayed points must be filed console-grid-local, not against the ally's grid");
            Assert.That(relayed.Points, Has.Count.GreaterThanOrEqualTo(3));

            // The cone apex is the ally's radar mount: ~120 m out along +X in the
            // console grid's local frame (both grids are axis-aligned at spawn).
            var apex = relayed.Points[0];
            Assert.That(apex.X, Is.InRange(100f, 140f), "the apex must sit at the ally's offset in console-local space");
            Assert.That(MathF.Abs(apex.Y), Is.LessThan(10f));

            // The boundary is apex-relative in world orientation. The first ray is
            // cast at world angle 0 and nothing occludes this map, so its offset is
            // exactly (+cone reach, 0): a console-grid-local absolute would sit
            // another ~120 m out and fail this.
            Assert.That(relayed.WorldOffsets, Is.True, "a sensor fan must ship world-oriented boundary offsets");
            var reach = 200f; // KsRadarTestSensor maxRange 100 x coneRangeFactor 2
            var off = relayed.Points[1];
            Assert.That(off.X, Is.EqualTo(reach).Within(0.5f), "the unoccluded +X ray must reach the full cone range, apex-relative");
            Assert.That(MathF.Abs(off.Y), Is.LessThan(0.5f));

            // And nothing on the console's own grid produced a cone, so every
            // region present is the relayed set.
            Assert.That(regions.TrueForAll(r => r.Relayed), "the listening grid mounts no sensors of its own");
        }));
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
            entManager.SpawnEntity("KsRadarTestOccluder", new EntityCoordinates(grid, new Vector2(x, y)));
        }
    }

    /// <summary>Must be anchored: the crawler counts only anchored hull.</summary>
    private static void SpawnRadarWall(
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
