#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Sensors;
using Content.Shared._KS14.Sensors;
using Content.Shared._KS14.Sensors.Prototypes;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Sensors;

/// <summary>
///     Emitter intel: designations ("E-001") assigned on first emitter-class filing and
///         converging over datalink, band/pattern identification read back by ELINT,
///         signal strength and bearing stability on the strobe, focus analysis and the
///         pool's emission log.
/// </summary>
public sealed class KsSensorEmitterIntelTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
# The emitter the listeners hear: cone reach 200 (maxRange 100 x factor 2).
- type: entity
  id: KsIntelTestRadar
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

- type: entity
  id: KsIntelTestElint
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
    intel:
    - KsIntelEmitterRange
  - type: KsElint
    ignoreFraction: 0

# Analysis so slow it can never complete inside a test, with a stage that unlocks
# almost immediately: exercises the mid-analysis state (intel earned, still Bearing).
- type: entity
  id: KsIntelTestElintSlow
  name: test slow-analysis elint array
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
    analysisTime: 600
    analysisStages:
    - progress: 0.001
      unlocks:
      - KsIntelSize

# Analysis fast enough to complete within a few sweeps: exercises the 100% grant.
- type: entity
  id: KsIntelTestElintFast
  name: test fast-analysis elint array
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
    analysisTime: 1
    analysisStages:
    - progress: 0.5
      unlocks:
      - KsIntelSize

- type: entity
  id: KsIntelTestVisual
  name: test visual search array
  components:
  - type: KsSensor
    sensorType: VisualSearch
    maxRange: 200
    providesName: false
    requireExternalMount: false
  - type: KsVisualSearch

- type: entity
  id: KsIntelTestTx
  name: test datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000

- type: entity
  id: KsIntelTestRx
  name: test datalink receiver
  components:
  - type: KsDatalinkReceiver

- type: entity
  id: KsIntelTestJammer
  name: test jammer array
  components:
  - type: KsJammer
    enabled: true
    jammingPower: 120
    halfAngle: 180
    requireExternalMount: false

- type: entity
  id: KsIntelTestShuttleConsole
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
    ///     Hearing emitters files them under fleet designations: each gets a unique,
    ///         stable E-number; a grid tracked only by non-emitter sensors gets none.
    /// </summary>
    [Test]
    public async Task TestElintDesignatesEmitters()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridR1 = default(Entity<MapGridComponent>);
        var gridR2 = default(Entity<MapGridComponent>);
        var gridV = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR1 = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR2 = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridV = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridR1.Owner, new Vector2(0f, 120f));
            xformSystem.SetLocalPosition(gridR2.Owner, new Vector2(120f, 0f));
            xformSystem.SetLocalPosition(gridV.Owner, new Vector2(-60f, 0f));

            entManager.SpawnEntity("KsIntelTestElint", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestVisual", new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestRadar", new EntityCoordinates(gridR1.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestRadar", new EntityCoordinates(gridR2.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        string? first = null;
        string? second = null;

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var pool));
            Assert.That(pool!.Contacts.TryGetValue(gridR1.Owner, out var record1), "the first emitter was never filed");
            Assert.That(pool.Contacts.TryGetValue(gridR2.Owner, out var record2), "the second emitter was never filed");
            Assert.That(pool.Contacts.TryGetValue(gridV.Owner, out var recordV), "the visual-only grid was never tracked");

            Assert.Multiple(() =>
            {
                Assert.That(record1!.Designation, Does.Match(@"^E-\d{3}$"));
                Assert.That(record2!.Designation, Does.Match(@"^E-\d{3}$"));
                Assert.That(record1.Designation, Is.Not.EqualTo(record2.Designation),
                    "each emitter gets its own designation");
                Assert.That(recordV!.Designation, Is.Null,
                    "a grid tracked only by visual search is not an emitter");
            });

            first = record1!.Designation;
            second = record2!.Designation;

            var contact = CollectContact(entManager, gridA.Owner, gridR1.Owner);
            Assert.That(contact?.Designation, Is.EqualTo(first), "the snapshot carries the designation");
        });

        // Designations are stable across re-detection.
        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var pool = entManager.GetComponent<KsSensorContactPoolComponent>(gridA.Owner);
            Assert.Multiple(() =>
            {
                Assert.That(pool.Contacts[gridR1.Owner].Designation, Is.EqualTo(first));
                Assert.That(pool.Contacts[gridR2.Owner].Designation, Is.EqualTo(second));
            });
        });
    }

    /// <summary>
    ///     Designations ride the datalink: a receiver that never heard the emitter
    ///         itself shows the assigning grid's label; and two grids that designated
    ///         the same emitter independently converge on one label once linked.
    /// </summary>
    [Test]
    public async Task TestDesignationRelaysAndConverges()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridC = default(Entity<MapGridComponent>);
        var gridB = default(Entity<MapGridComponent>);
        var gridD = default(Entity<MapGridComponent>);
        var elintC = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            // A listens from the south, C from the north; B is the shared emitter
            // between them, D a second emitter only C can hear (it seeds C's counter
            // so C labels B differently than A does).
            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridC = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridD = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridA.Owner, new Vector2(0f, -120f));
            xformSystem.SetLocalPosition(gridC.Owner, new Vector2(0f, 150f));
            xformSystem.SetLocalPosition(gridD.Owner, new Vector2(0f, 300f));

            entManager.SpawnEntity("KsIntelTestElint", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            elintC = entManager.SpawnEntity("KsIntelTestElint", new EntityCoordinates(gridC.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestRadar", new EntityCoordinates(gridD.Owner, new Vector2(0.5f, 0.5f)));
        });

        // C files D first, seeding its counter past A's.
        await Pair.RunTicksSync(30);

        var radarB = default(EntityUid);

        await server.WaitPost(() =>
        {
            radarB = entManager.SpawnEntity("KsIntelTestRadar", new EntityCoordinates(gridB.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var poolA = entManager.GetComponent<KsSensorContactPoolComponent>(gridA.Owner);
            var poolC = entManager.GetComponent<KsSensorContactPoolComponent>(gridC.Owner);

            Assert.Multiple(() =>
            {
                Assert.That(poolA.Contacts[gridB.Owner].Designation, Is.Not.Null);
                Assert.That(poolC.Contacts[gridB.Owner].Designation, Is.Not.Null);
                Assert.That(poolC.Contacts[gridB.Owner].Designation,
                    Is.Not.EqualTo(poolA.Contacts[gridB.Owner].Designation),
                    "scenario sanity: the two pools must have labelled B differently before linking");
            });
        });

        // Link the two listeners both ways; the fleet must converge on one label.
        await server.WaitPost(() =>
        {
            entManager.SpawnEntity("KsIntelTestTx", new EntityCoordinates(gridA.Owner, new Vector2(2.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestRx", new EntityCoordinates(gridA.Owner, new Vector2(3.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestTx", new EntityCoordinates(gridC.Owner, new Vector2(2.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestRx", new EntityCoordinates(gridC.Owner, new Vector2(3.5f, 0.5f)));
        });

        await Pair.RunTicksSync(80);

        await server.WaitAssertion(() =>
        {
            var recordA = entManager.GetComponent<KsSensorContactPoolComponent>(gridA.Owner).Contacts[gridB.Owner];
            var recordC = entManager.GetComponent<KsSensorContactPoolComponent>(gridC.Owner).Contacts[gridB.Owner];

            Assert.Multiple(() =>
            {
                Assert.That(recordA.Designation, Is.EqualTo(recordC.Designation),
                    "the linked pools must converge on one designation for the shared emitter");
                Assert.That(recordA.DesignatedBy, Is.EqualTo(recordC.DesignatedBy),
                    "the linked pools must agree on the assigning grid");
                Assert.That(recordA.FirstSeen, Is.EqualTo(recordC.FirstSeen),
                    "the linked pools must agree on the conflict-resolution key");
            });
        });
    }

    /// <summary>
    ///     ELINT reads the heard emission's identity back: band and pattern land on
    ///         the contact; a grid nobody heard emit carries neither.
    /// </summary>
    [Test]
    public async Task TestElintRevealsBandAndPattern()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);
        var gridV = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridV = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(120f, 0f));
            xformSystem.SetLocalPosition(gridV.Owner, new Vector2(-60f, 0f));

            entManager.SpawnEntity("KsIntelTestElint", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestVisual", new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestRadar", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var emitter = CollectContact(entManager, gridA.Owner, gridR.Owner);
            var silent = CollectContact(entManager, gridA.Owner, gridV.Owner);

            Assert.That(emitter, Is.Not.Null);
            Assert.That(silent, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(emitter!.Band?.Id, Is.EqualTo("KsBandMid"),
                    "the heard set's band is identification intel");
                Assert.That(emitter.Pattern, Is.EqualTo(KsEmissionPattern.Continuous));
                Assert.That(silent!.Band, Is.Null, "nobody heard the silent grid emit");
                Assert.That(silent.Pattern, Is.Null);
            });
        });
    }

    /// <summary>Signal strength scales with how deep inside the emission's reach the listener sits.</summary>
    [Test]
    public async Task TestSignalStrengthScalesWithDistance()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridNear = default(Entity<MapGridComponent>);
        var gridFar = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridNear = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridFar = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // Perpendicular placements so neither emitter grid shadows the other.
            xformSystem.SetLocalPosition(gridNear.Owner, new Vector2(60f, 0f));
            xformSystem.SetLocalPosition(gridFar.Owner, new Vector2(0f, 160f));

            entManager.SpawnEntity("KsIntelTestElint", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestRadar", new EntityCoordinates(gridNear.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestRadar", new EntityCoordinates(gridFar.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var near = CollectContact(entManager, gridA.Owner, gridNear.Owner);
            var far = CollectContact(entManager, gridA.Owner, gridFar.Owner);

            Assert.That(near?.Bearing, Is.Not.Null);
            Assert.That(far?.Bearing, Is.Not.Null);

            var nearSignal = near!.Bearing!.Value.SignalStrength;
            var farSignal = far!.Bearing!.Value.SignalStrength;

            Assert.Multiple(() =>
            {
                Assert.That(nearSignal, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
                Assert.That(farSignal, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
                Assert.That(nearSignal, Is.GreaterThan(farSignal),
                    "the close emitter must read stronger than the distant one");
            });
        });
    }

    /// <summary>
    ///     Bearing stability classifies the strobe's drift rate: a stationary emitter
    ///         reads STABLE, one crossing the sky reads DRIFTING.
    /// </summary>
    [Test]
    public async Task TestBearingStabilityClassification()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(0f, 100f));

            entManager.SpawnEntity("KsIntelTestElint", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestRadar", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var contact = CollectContact(entManager, gridA.Owner, gridR.Owner);
            Assert.That(contact?.Bearing, Is.Not.Null);
            Assert.That(contact!.Stability, Is.EqualTo(KsBearingStability.Stable),
                "a stationary emitter's bearing holds steady");
        });

        // March the emitter sideways: ~48m of lateral travel at ~100m range over the
        // next couple of seconds is far past any sane drift threshold.
        for (var step = 1; step <= 12; step++)
        {
            var x = step * 4f;
            await server.WaitPost(() => xformSystem.SetLocalPosition(gridR.Owner, new Vector2(x, 100f)));
            await Pair.RunTicksSync(5);
        }

        await server.WaitAssertion(() =>
        {
            var contact = CollectContact(entManager, gridA.Owner, gridR.Owner);
            Assert.That(contact?.Bearing, Is.Not.Null, "the emitter must still be heard while crossing");
            Assert.That(contact!.Stability, Is.EqualTo(KsBearingStability.Drifting),
                "a crossing emitter's bearing rate must classify as drifting");
        });
    }

    /// <summary>
    ///     Mid-analysis: a reached stage unlocks its intel on the contact while the
    ///         emitter stays bearing-only (the fix is only granted at 100%).
    /// </summary>
    [Test]
    public async Task TestFocusStageUnlocksIntel()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var elintSystem = entManager.System<KsElintSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(120f, 0f));

            entManager.SpawnEntity("KsIntelTestElintSlow", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestRadar", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(30);

        await server.WaitPost(() => elintSystem.SetGridFocus(gridA.Owner, gridR.Owner));

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var contact = CollectContact(entManager, gridA.Owner, gridR.Owner);
            Assert.That(contact, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(contact!.Focused, "the snapshot must flag the analysed emitter");
                Assert.That(contact.AnalysisProgress, Is.GreaterThan(0f).And.LessThan(1f),
                    "a 600s analysis cannot have completed inside this test");
                Assert.That(contact.Intel.Any(i => i.Intel.Id == "KsIntelSize"),
                    "the reached stage's intel must be on the contact");
                Assert.That(contact.Quality, Is.EqualTo(KsPositionQuality.Bearing),
                    "an incomplete analysis earns no fix");
            });
        });
    }

    /// <summary>
    ///     Completed analysis: 100% resolves the emitter Exact while the track is
    ///         live; ceasing focus drops it back to a bearing but keeps the unlocked
    ///         intel (sticky knowledge is not un-learned).
    /// </summary>
    [Test]
    public async Task TestFocusCompletionGrantsExactThenSticks()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var elintSystem = entManager.System<KsElintSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(120f, 0f));

            entManager.SpawnEntity("KsIntelTestElintFast", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestRadar", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(30);

        await server.WaitPost(() => elintSystem.SetGridFocus(gridA.Owner, gridR.Owner));

        // A 1-second analysis completes within a couple of sweeps.
        await Pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var pool = entManager.GetComponent<KsSensorContactPoolComponent>(gridA.Owner);
            var record = pool.Contacts[gridR.Owner];
            var contact = CollectContact(entManager, gridA.Owner, gridR.Owner);
            Assert.That(contact, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(contact!.AnalysisProgress, Is.EqualTo(1f),
                    "the 1s analysis must have completed");
                Assert.That(contact.Quality, Is.EqualTo(KsPositionQuality.Exact),
                    "a fully analysed emission is a localised one");
                Assert.That((contact.WorldPosition - record.WorldPosition).Length(), Is.LessThan(0.01f),
                    "the granted fix is the record's truth");
                Assert.That(contact.Bearing, Is.Null, "an Exact contact carries no strobe");
            });
        });

        await server.WaitPost(() => elintSystem.SetGridFocus(gridA.Owner, null));

        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var contact = CollectContact(entManager, gridA.Owner, gridR.Owner);
            Assert.That(contact, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(contact!.Focused, Is.False);
                Assert.That(contact.AnalysisProgress, Is.EqualTo(0f));
                Assert.That(contact.Quality, Is.EqualTo(KsPositionQuality.Bearing),
                    "ceasing focus drops the live track back to a bearing");
                Assert.That(contact.Intel.Any(i => i.Intel.Id == "KsIntelSize"),
                    "unlocked stage intel is sticky and survives the cease");
            });
        });
    }

    /// <summary>
    ///     The console focus message is fog-of-war gated: only a contact the grid's own pool
    ///         filed as an emitter may be focused, so the message cannot probe unheard grids
    ///         (including ones the pool tracks visually but never heard emit).
    /// </summary>
    [Test]
    public async Task TestFocusMessageRequiresDesignatedEmitter()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var uiSystem = entManager.System<SharedUserInterfaceSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);
        var gridV = default(Entity<MapGridComponent>);
        var elint = default(EntityUid);
        var radarR = default(EntityUid);
        var console = default(EntityUid);
        var actor = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridV = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(120f, 0f));
            xformSystem.SetLocalPosition(gridV.Owner, new Vector2(0f, 60f));

            elint = entManager.SpawnEntity("KsIntelTestElint", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestVisual", new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            radarR = entManager.SpawnEntity("KsIntelTestRadar", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));

            console = entManager.SpawnEntity("KsIntelTestShuttleConsole", new EntityCoordinates(gridA.Owner, new Vector2(2.5f, 0.5f)));
            xformSystem.AnchorEntity((console, entManager.GetComponent<TransformComponent>(console)));
            actor = entManager.SpawnEntity(null, new EntityCoordinates(gridA.Owner, new Vector2(2.5f, 0.5f)));
            uiSystem.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });

        await Pair.RunTicksSync(60);

        // Stand in for a client's BUI message: fill the fields the engine's receive path
        // fills, then raise it at the console exactly as that path does (the handler is
        // scoped to the shuttle console key).
        void SendFocus(EntityUid target)
        {
            var msg = new KsElintFocusMessage
            {
                Target = entManager.GetNetEntity(target),
                Actor = actor,
                Entity = entManager.GetNetEntity(console),
                UiKey = ShuttleConsoleUiKey.Key,
            };

            entManager.EventBus.RaiseLocalEvent(console, (object) msg, true);
        }

        await server.WaitAssertion(() =>
        {
            var pool = entManager.GetComponent<KsSensorContactPoolComponent>(gridA.Owner);
            Assert.That(pool.Contacts.ContainsKey(gridV.Owner),
                "scenario sanity: the silent grid must be visually tracked (record exists, no designation)");

            SendFocus(gridV.Owner);
            Assert.That(entManager.GetComponent<KsElintComponent>(elint).FocusTarget, Is.Null,
                "focusing a grid never filed as an emitter must be refused");

            SendFocus(gridR.Owner);
            Assert.That(entManager.GetComponent<KsElintComponent>(elint).FocusTarget, Is.EqualTo(gridR.Owner),
                "focusing the designated emitter must point the array at it");
        });

        // Hide the emitter's record without killing its grid: silence it and jump it away,
        // so the visual sensor confirms the old spot empty and the record tombstones
        // (designated, alive, but off the roster).
        await server.WaitPost(() =>
        {
            entManager.EventBus.RaiseLocalEvent(console, (object) new KsElintClearFocusMessage
            {
                Actor = actor,
                Entity = entManager.GetNetEntity(console),
                UiKey = ShuttleConsoleUiKey.Key,
            }, true);

            entManager.GetComponent<KsSensorComponent>(radarR).Enabled = false;
            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(600f, 0f));
        });

        await Pair.RunTicksSync(120);

        // Regression: the gate must reject a designated record the roster hides, or the
        // message becomes an aliveness oracle for hidden ships (record survival tracks
        // grid survival exactly).
        await server.WaitAssertion(() =>
        {
            var pool = entManager.GetComponent<KsSensorContactPoolComponent>(gridA.Owner);
            Assert.That(pool.Contacts.TryGetValue(gridR.Owner, out var record),
                "scenario sanity: the hidden record must still exist in the pool");
            Assert.That(record!.ConfirmedGoneAt, Is.GreaterThan(record.LastSeen),
                "scenario sanity: the record must be tombstoned (visual confirmed the spot empty)");
            Assert.That(record.Designation, Is.Not.Null,
                "scenario sanity: the tombstoned record keeps its designation");

            SendFocus(gridR.Owner);
            Assert.That(entManager.GetComponent<KsElintComponent>(elint).FocusTarget, Is.Null,
                "focusing a designated-but-hidden (tombstoned) record must be refused");
        });
    }

    /// <summary>
    ///     Regression: among equally-live sources of the same tier, an Exact source must
    ///         beat a Bearing one, or a receiver's fresher own bearing outranks an ally's
    ///         relayed completed-analysis fix forever.
    /// </summary>
    [Test]
    public async Task TestRelayedExactFixBeatsOwnBearing()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var elintSystem = entManager.System<KsElintSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridB = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            // A analyses and relays; B hears the same emitter with its own bearing-only
            // array and listens to A's datalink.
            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridB = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(0f, 60f));
            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(120f, 0f));

            entManager.SpawnEntity("KsIntelTestElintFast", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestTx", new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestElint", new EntityCoordinates(gridB.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestRx", new EntityCoordinates(gridB.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsIntelTestRadar", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(30);

        await server.WaitPost(() => elintSystem.SetGridFocus(gridA.Owner, gridR.Owner));

        // Let the 1s analysis complete and the Exact source ride the datalink.
        await Pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var pool = entManager.GetComponent<KsSensorContactPoolComponent>(gridB.Owner);
            Assert.That(pool.Contacts.TryGetValue(gridR.Owner, out var record));

            // Scenario sanity: B must genuinely hold both a fresher own bearing source and
            // the ally's relayed Exact one, the exact tie the ordering decides.
            Assert.Multiple(() =>
            {
                Assert.That(record!.Sources.Values.Any(s => s.Hops == 0 && s.Quality == KsPositionQuality.Bearing),
                    "B's own bearing track must be present");
                Assert.That(record.Sources.Values.Any(s => s.Hops > 0 && s.Quality == KsPositionQuality.Exact),
                    "the ally's relayed completed-analysis source must be present");
            });

            var contact = CollectContact(entManager, gridB.Owner, gridR.Owner);
            Assert.That(contact, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(contact!.Quality, Is.EqualTo(KsPositionQuality.Exact),
                    "the relayed Exact source must win over the fresher same-tier bearing");
                Assert.That((contact.WorldPosition - record!.WorldPosition).Length(), Is.LessThan(0.01f),
                    "the shown fix is the record's truth");
                Assert.That(contact.Bearing, Is.Null, "an Exact contact carries no strobe");
            });
        });
    }

    /// <summary>Focus progress only advances while the emitter is actually heard.</summary>
    [Test]
    public async Task TestFocusFreezesWhileUnheard()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var elintSystem = entManager.System<KsElintSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);
        var elint = default(EntityUid);
        var radar = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(120f, 0f));

            elint = entManager.SpawnEntity("KsIntelTestElintSlow", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            radar = entManager.SpawnEntity("KsIntelTestRadar", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(30);

        await server.WaitPost(() => elintSystem.SetGridFocus(gridA.Owner, gridR.Owner));

        await Pair.RunTicksSync(30);

        var frozen = 0f;

        await server.WaitPost(() =>
        {
            var comp = entManager.GetComponent<KsElintComponent>(elint);
            Assert.That(comp.FocusProgress, Is.GreaterThan(0f), "progress must advance while the emitter is heard");

            entManager.GetComponent<KsSensorComponent>(radar).Enabled = false;
            frozen = comp.FocusProgress;
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var comp = entManager.GetComponent<KsElintComponent>(elint);
            Assert.That(comp.FocusProgress, Is.EqualTo(frozen),
                "silence must freeze the analysis timer");
        });
    }

    /// <summary>
    ///     The emission log tracks the pool's own emitter transitions (acquired under its
    ///         designation, silent when the last live emitter-class track is lost) and rides
    ///         the console snapshot.
    /// </summary>
    [Test]
    public async Task TestEmissionLogLifecycle()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridA = default(Entity<MapGridComponent>);
        var gridR = default(Entity<MapGridComponent>);
        var radar = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridR = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridR.Owner, new Vector2(120f, 0f));

            entManager.SpawnEntity("KsIntelTestElint", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            radar = entManager.SpawnEntity("KsIntelTestRadar", new EntityCoordinates(gridR.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        string? designation = null;

        await server.WaitAssertion(() =>
        {
            var pool = entManager.GetComponent<KsSensorContactPoolComponent>(gridA.Owner);
            designation = pool.Contacts[gridR.Owner].Designation;
            Assert.That(designation, Is.Not.Null);

            Assert.That(pool.EmissionLog.Any(e => e.Kind == KsEmissionLogKind.EmitterNew && e.Designation == designation),
                "acquiring the emitter must be logged under its designation");
            Assert.That(pool.EmissionLog.Any(e => e.Kind == KsEmissionLogKind.EmitterSilent), Is.False,
                "nothing has gone silent yet");

            var ev = new KsCollectNavContactsEvent(gridA.Owner);
            entManager.EventBus.RaiseEvent(EventSource.Local, ref ev);
            Assert.That(ev.EmissionLog, Is.Not.Null, "the log must ride the console snapshot");
        });

        // Silence the emitter and let the live window lapse.
        await server.WaitPost(() => entManager.GetComponent<KsSensorComponent>(radar).Enabled = false);

        await Pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var pool = entManager.GetComponent<KsSensorContactPoolComponent>(gridA.Owner);
            Assert.That(pool.EmissionLog.Any(e => e.Kind == KsEmissionLogKind.EmitterSilent && e.Designation == designation),
                "the emitter going dark must be logged");
        });
    }

    /// <summary>
    ///     The grid's own jam state edges land on its emission log: JAM START when a
    ///         radar is flooded, JAM END when the sky clears.
    /// </summary>
    [Test]
    public async Task TestJamStateLogEdges()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var sensors = entManager.System<KsSensorSystem>();

        var map = await Pair.CreateTestMap();

        var gridV = default(Entity<MapGridComponent>);
        var gridT = default(Entity<MapGridComponent>);
        var jammer = default(EntityUid);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridV = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);
            gridT = MakeShipGrid(entManager, mapManager, mapSystem, map.MapId);

            // ~86m out: inside the 120 jam power, outside the radar's burn-through
            // (120 * 0.5 = 60), so the radar is genuinely jammed.
            xformSystem.SetLocalPosition(gridT.Owner, new Vector2(90f, 0f));

            entManager.SpawnEntity("KsIntelTestRadar", new EntityCoordinates(gridV.Owner, new Vector2(0.5f, 0.5f)));
            jammer = entManager.SpawnEntity("KsIntelTestJammer", new EntityCoordinates(gridT.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(sensors.IsGridJammed(gridV.Owner), "scenario sanity: the radar must be jammed");

            var pool = entManager.GetComponent<KsSensorContactPoolComponent>(gridV.Owner);
            Assert.That(pool.EmissionLog.Any(e => e.Kind == KsEmissionLogKind.JamStart),
                "getting jammed must be logged");
            Assert.That(pool.EmissionLog.Any(e => e.Kind == KsEmissionLogKind.JamEnd), Is.False);
        });

        await server.WaitPost(() => entManager.GetComponent<KsJammerComponent>(jammer).Enabled = false);

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(sensors.IsGridJammed(gridV.Owner), Is.False);

            var pool = entManager.GetComponent<KsSensorContactPoolComponent>(gridV.Owner);
            Assert.That(pool.EmissionLog.Any(e => e.Kind == KsEmissionLogKind.JamEnd),
                "recovering from jamming must be logged");
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
