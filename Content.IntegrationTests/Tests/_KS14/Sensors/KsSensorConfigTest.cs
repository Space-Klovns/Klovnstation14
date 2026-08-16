#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Sensors;
using Content.Shared._KS14.Sensors;
using Content.Shared._KS14.Sensors.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Sensors;

/// <summary>
///     YAML configurability of the sensor framework: the stock readouts are
///         prototype data classified by one public evaluator rather than hard-coded
///         switch arms, and every sensor/transmitter switch is exercised against a
///         default-configured control so the flag, not the surrounding setup, is
///         shown to flip the outcome.
/// </summary>
public sealed class KsSensorConfigTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCfgHideSilSensor
  name: test silhouette-hiding array
  components:
  - type: KsSensor
    maxRange: 500
    providesName: true
    requireExternalMount: false
    renderMode: Outline
    revealSilhouette: false
  - type: KsVisualSearch

- type: entity
  id: KsCfgShowSilSensor
  name: test silhouette array
  components:
  - type: KsSensor
    maxRange: 500
    providesName: true
    requireExternalMount: false
    renderMode: Outline
  - type: KsVisualSearch

- type: entity
  id: KsCfgHideVelSensor
  name: test velocity-hiding array
  components:
  - type: KsSensor
    maxRange: 500
    providesName: true
    requireExternalMount: false
    revealVelocity: false
  - type: KsVisualSearch

- type: entity
  id: KsCfgShowVelSensor
  name: test velocity array
  components:
  - type: KsSensor
    maxRange: 500
    providesName: true
    requireExternalMount: false
  - type: KsVisualSearch

- type: entity
  id: KsCfgRelaySensor
  name: test relay sensor array
  components:
  - type: KsSensor
    maxRange: 200
    requireExternalMount: false
  - type: KsVisualSearch

- type: entity
  id: KsCfgTxDefault
  name: test datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000

- type: entity
  id: KsCfgTxNoName
  name: test anonymous datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000
    revealName: false

- type: entity
  id: KsCfgTxNoAnnounce
  name: test relay-only datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000
    announceSelf: false

- type: entity
  id: KsCfgTxNoRelay
  name: test beacon-only datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000
    relayContacts: false

- type: entity
  id: KsCfgTxBlipSelf
  name: test blip datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000
    selfRenderMode: Blip

- type: entity
  id: KsCfgRx
  name: test datalink receiver
  components:
  - type: KsDatalinkReceiver

# Scales the raw metric (x2) and keeps one decimal, to prove the evaluator
# applies Scale and a positive Round.
- type: ksSensorIntel
  id: KsCfgIntelScaledMass
  label: ks-sensor-intel-mass-label
  metric: Mass
  valueFormat: ks-sensor-intel-mass-value
  scale: 2
  round: 1
  order: 99
";

    /// <summary>
    ///     The data-driven evaluator, called directly. SIZE classifies the grid area
    ///         into the first threshold band it falls under, compared against the loc
    ///         string so a wording change never breaks the banding. MASS substitutes
    ///         the scaled, rounded metric into its value format, whole because MASS is
    ///         Round 0. A metric with no value (TOP SPEED on an engineless grid) falls
    ///         back to its configured none label instead of vanishing.
    /// </summary>
    [Test]
    public async Task TestDataDrivenIntelEvaluator()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var locMan = server.ResolveDependency<ILocalizationManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var physicsSystem = entManager.System<SharedPhysicsSystem>();
        var intelSystem = entManager.System<KsSensorIntelSystem>();

        var map = await Pair.CreateTestMap();

        var gridSmall = default(Entity<MapGridComponent>);
        var gridMedium = default(Entity<MapGridComponent>);
        var gridLarge = default(Entity<MapGridComponent>);
        var gridMassive = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            // Size metric is width * height of the local AABB, so one grid lands in
            // each of the four size bands: 8x8 -> 64 (small, < 200), 20x20 -> 400
            // (medium), 40x40 -> 1600 (large), 71x71 -> 5041 (the massive catch-all,
            // >= 5000). All four prove the threshold list classifies rather than
            // merely formats.
            gridSmall = MakeGrid(mapManager, mapSystem, map.MapId, 8, 8);
            gridMedium = MakeGrid(mapManager, mapSystem, map.MapId, 20, 20);
            gridLarge = MakeGrid(mapManager, mapSystem, map.MapId, 40, 40);
            gridMassive = MakeGrid(mapManager, mapSystem, map.MapId, 71, 71);

            // Spread the grids off the shared origin so they never overlap.
            xformSystem.SetLocalPosition(gridMedium.Owner, new Vector2(300f, 0f));
            xformSystem.SetLocalPosition(gridLarge.Owner, new Vector2(0f, 300f));
            xformSystem.SetLocalPosition(gridMassive.Owner, new Vector2(500f, 500f));

            // A static grid reports zero physics mass, so make the one whose MASS
            // readout is checked dynamic for a real, non-zero tonnage.
            physicsSystem.SetBodyType(gridSmall.Owner, BodyType.Dynamic,
                body: entManager.GetComponent<PhysicsComponent>(gridSmall.Owner));
        });

        // Let the fixtures settle so the dynamic grid's mass is populated.
        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var smallPhysics = entManager.GetComponent<PhysicsComponent>(gridSmall.Owner);
            var mediumPhysics = entManager.GetComponent<PhysicsComponent>(gridMedium.Owner);
            var largePhysics = entManager.GetComponent<PhysicsComponent>(gridLarge.Owner);
            var massivePhysics = entManager.GetComponent<PhysicsComponent>(gridMassive.Owner);

            // Re-fetched rather than read off the captured Entity<> structs: those were
            // assigned inside a WaitPost lambda, which nullable flow analysis cannot see
            // through, so their Comp reads as maybe-null under warnings-as-errors.
            var smallGrid = entManager.GetComponent<MapGridComponent>(gridSmall.Owner);
            var mediumGrid = entManager.GetComponent<MapGridComponent>(gridMedium.Owner);
            var largeGrid = entManager.GetComponent<MapGridComponent>(gridLarge.Owner);
            var massiveGrid = entManager.GetComponent<MapGridComponent>(gridMassive.Owner);

            var small = intelSystem.Evaluate(
                new List<ProtoId<KsSensorIntelPrototype>> { "KsIntelSize", "KsIntelMass", "KsIntelTopSpeed", "KsCfgIntelScaledMass" },
                gridSmall.Owner, smallPhysics, smallGrid);
            var medium = intelSystem.Evaluate(
                new List<ProtoId<KsSensorIntelPrototype>> { "KsIntelSize" }, gridMedium.Owner, mediumPhysics, mediumGrid);
            var large = intelSystem.Evaluate(
                new List<ProtoId<KsSensorIntelPrototype>> { "KsIntelSize" }, gridLarge.Owner, largePhysics, largeGrid);
            var massive = intelSystem.Evaluate(
                new List<ProtoId<KsSensorIntelPrototype>> { "KsIntelSize" }, gridMassive.Owner, massivePhysics, massiveGrid);

            Assert.That(small, Is.Not.Null, "the evaluator returned nothing for a non-empty intel list");
            Assert.That(medium, Is.Not.Null, "the evaluator returned no SIZE readout for the medium grid");
            Assert.That(large, Is.Not.Null, "the evaluator returned no SIZE readout for the large grid");
            Assert.That(massive, Is.Not.Null, "the evaluator returned no SIZE readout for the massive grid");

            var smallReadouts = small!;

            Assert.Multiple(() =>
            {
                // The value lands in the first band it is strictly under; compared
                // against the loc strings, not the literal text.
                Assert.That(smallReadouts["KsIntelSize"], Is.EqualTo(locMan.GetString("ks-sensor-intel-size-small")),
                    "an 8x8 grid (area 64) should classify into the smallest size band");
                Assert.That(medium!["KsIntelSize"], Is.EqualTo(locMan.GetString("ks-sensor-intel-size-medium")),
                    "a 20x20 grid (area 400) should classify into the medium size band");
                Assert.That(large!["KsIntelSize"], Is.EqualTo(locMan.GetString("ks-sensor-intel-size-large")),
                    "a 40x40 grid (area 1600) should classify into the large size band");
                Assert.That(massive!["KsIntelSize"], Is.EqualTo(locMan.GetString("ks-sensor-intel-size-massive")),
                    "a 71x71 grid (area 5041) should reach the massive catch-all band");

                // { $value } is replaced by the rounded metric; the mass check keeps
                // a broken zero-mass from passing.
                Assert.That(smallPhysics.Mass, Is.GreaterThan(0f), "the dynamic grid should have a real, non-zero mass");
                Assert.That(smallReadouts["KsIntelMass"],
                    Is.EqualTo(locMan.GetString("ks-sensor-intel-mass-value", ("value", (int) System.MathF.Round(smallPhysics.Mass)))),
                    "MASS should render its whole-number tonnage through the value format");

                // The scaled variant is twice the mass kept to one decimal, distinct
                // from the Round-0 MASS above, so both Scale and Round are provably
                // applied.
                Assert.That(smallReadouts["KsCfgIntelScaledMass"],
                    Is.EqualTo(locMan.GetString("ks-sensor-intel-mass-value", ("value", System.MathF.Round(smallPhysics.Mass * 2f, 1)))),
                    "the scaled readout should double the mass and keep one decimal");

                // noneLabel: TOP SPEED has no value on a thrusterless grid.
                Assert.That(smallReadouts["KsIntelTopSpeed"], Is.EqualTo(locMan.GetString("ks-sensor-intel-topspeed-none")),
                    "a grid with no linear thrusters should read the engineless top-speed label");
            });
        });
    }

    /// <summary>
    ///     revealSilhouette:false degrades a sensor's contacts to an anonymous blip
    ///         even when its render mode is Outline. The control sensor differs only
    ///         in that switch, so nothing else can explain the blip.
    /// </summary>
    [Test]
    public async Task TestRevealSilhouetteFalseForcesBlip()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridHide = default(Entity<MapGridComponent>);
        var gridShow = default(Entity<MapGridComponent>);
        var gridTarget = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridHide = MakeShipGrid(mapManager, mapSystem, map.MapId);
            gridShow = MakeShipGrid(mapManager, mapSystem, map.MapId);
            gridTarget = MakeShipGrid(mapManager, mapSystem, map.MapId);

            // Move the control sensor and the target off the origin BEFORE
            // mounting, so gridHide sits alone at the origin and each sensor
            // parents to its own grid.
            xformSystem.SetLocalPosition(gridShow.Owner, new Vector2(0f, 300f));
            xformSystem.SetLocalPosition(gridTarget.Owner, new Vector2(100f, 0f));

            // Both sensors render Outline; only revealSilhouette differs, so a blip
            // on the hiding sensor can come only from the switch, not the mode.
            entManager.SpawnEntity("KsCfgHideSilSensor", new EntityCoordinates(gridHide.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsCfgShowSilSensor", new EntityCoordinates(gridShow.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridHide.Owner, out var hidePool)
                && hidePool!.Contacts.ContainsKey(gridTarget.Owner),
                "the silhouette-hiding sensor should still detect the target");
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridShow.Owner, out var showPool)
                && showPool!.Contacts.ContainsKey(gridTarget.Owner),
                "the control sensor should detect the target");

            var hidden = hidePool!.Contacts[gridTarget.Owner].Sources.Values.First();
            var shown = showPool!.Contacts[gridTarget.Owner].Sources.Values.First();

            Assert.Multiple(() =>
            {
                Assert.That(hidden.RenderMode, Is.EqualTo(KsContactRenderMode.Blip),
                    "revealSilhouette:false must degrade the contact to a blip even though renderMode is Outline");
                Assert.That(shown.RenderMode, Is.EqualTo(KsContactRenderMode.Outline),
                    "the control sensor (revealSilhouette default) should keep its Outline render mode");
            });
        });
    }

    /// <summary>
    ///     revealVelocity:false strips a moving target's velocity from a sensor's
    ///         contacts, so a console draws no heading marker. The control sensor
    ///         records the same target's real velocity, proving the zero is the
    ///         switch's doing and not a stationary target.
    /// </summary>
    [Test]
    public async Task TestRevealVelocityFalseZeroesVelocity()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var physicsSystem = entManager.System<SharedPhysicsSystem>();

        var map = await Pair.CreateTestMap();

        var gridHide = default(Entity<MapGridComponent>);
        var gridShow = default(Entity<MapGridComponent>);
        var gridTarget = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridHide = MakeShipGrid(mapManager, mapSystem, map.MapId);
            gridShow = MakeShipGrid(mapManager, mapSystem, map.MapId);
            gridTarget = MakeShipGrid(mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridShow.Owner, new Vector2(0f, 300f));
            xformSystem.SetLocalPosition(gridTarget.Owner, new Vector2(100f, 0f));

            entManager.SpawnEntity("KsCfgHideVelSensor", new EntityCoordinates(gridHide.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsCfgShowVelSensor", new EntityCoordinates(gridShow.Owner, new Vector2(0.5f, 0.5f)));

            // A grid is static by default and cannot carry velocity, so make the
            // target dynamic before giving it a heading.
            var body = entManager.GetComponent<PhysicsComponent>(gridTarget.Owner);
            physicsSystem.SetBodyType(gridTarget.Owner, BodyType.Dynamic, body: body);
            physicsSystem.SetLinearVelocity(gridTarget.Owner, new Vector2(5f, 0f), body: body);
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridHide.Owner, out var hidePool)
                && hidePool!.Contacts.ContainsKey(gridTarget.Owner),
                "the velocity-hiding sensor should still detect the moving target");
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridShow.Owner, out var showPool)
                && showPool!.Contacts.ContainsKey(gridTarget.Owner),
                "the control sensor should detect the moving target");

            var hidden = hidePool!.Contacts[gridTarget.Owner];
            var shown = showPool!.Contacts[gridTarget.Owner];

            Assert.Multiple(() =>
            {
                Assert.That(shown.LinearVelocity.Length(), Is.GreaterThan(0.1f),
                    "the control sensor should record the target's real velocity, proving it is genuinely moving");
                Assert.That(hidden.LinearVelocity, Is.EqualTo(Vector2.Zero),
                    "revealVelocity:false must strip the target's velocity so consoles draw no heading");
            });
        });
    }

    /// <summary>
    ///     revealName:false anonymises a transmitter's datalink self-report: the
    ///         receiving grid learns the transmitter's position and outline but no
    ///         name. The control transmitter still carries its grid name, so only the
    ///         switch decides whether the name travels.
    /// </summary>
    [Test]
    public async Task TestRevealNameFalseHidesSelfReport()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridAnon = default(Entity<MapGridComponent>);
        var gridNamed = default(Entity<MapGridComponent>);
        var gridRx = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridAnon = MakeShipGrid(mapManager, mapSystem, map.MapId);
            gridNamed = MakeShipGrid(mapManager, mapSystem, map.MapId);
            gridRx = MakeShipGrid(mapManager, mapSystem, map.MapId);

            // Spread the grids off the origin so each machine parents to its own
            // grid; both transmitters sit well within their 1000 range of the
            // receiver and share the default frequency.
            xformSystem.SetLocalPosition(gridNamed.Owner, new Vector2(0f, 80f));
            xformSystem.SetLocalPosition(gridRx.Owner, new Vector2(300f, 0f));

            entManager.SpawnEntity("KsCfgTxNoName", new EntityCoordinates(gridAnon.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsCfgTxDefault", new EntityCoordinates(gridNamed.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsCfgRx", new EntityCoordinates(gridRx.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridRx.Owner, out var pool),
                "the receiver never built a pool, it heard no self-reports at all");
            Assert.That(pool!.Contacts.ContainsKey(gridAnon.Owner),
                "the anonymous transmitter's self-report never reached the receiver");
            Assert.That(pool.Contacts.ContainsKey(gridNamed.Owner),
                "the named control transmitter's self-report never reached the receiver");

            var anon = pool.Contacts[gridAnon.Owner].Sources.Values.First();
            var named = pool.Contacts[gridNamed.Owner].Sources.Values.First();

            Assert.Multiple(() =>
            {
                Assert.That(anon.Name, Is.Null,
                    "revealName:false still leaked the transmitting grid's name over datalink");
                Assert.That(named.Name, Is.Not.Null,
                    "the default transmitter's self-report should carry its grid name");
            });
        });
    }

    /// <summary>
    ///     selfRenderMode chooses how a transmitter's datalink self-report draws on
    ///         allied consoles, against a control transmitter left at the default.
    /// </summary>
    [Test]
    public async Task TestSelfRenderModeBlipDegradesSelfReport()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridBlip = default(Entity<MapGridComponent>);
        var gridOutline = default(Entity<MapGridComponent>);
        var gridRx = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridBlip = MakeShipGrid(mapManager, mapSystem, map.MapId);
            gridOutline = MakeShipGrid(mapManager, mapSystem, map.MapId);
            gridRx = MakeShipGrid(mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridOutline.Owner, new Vector2(0f, 80f));
            xformSystem.SetLocalPosition(gridRx.Owner, new Vector2(300f, 0f));

            entManager.SpawnEntity("KsCfgTxBlipSelf", new EntityCoordinates(gridBlip.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsCfgTxDefault", new EntityCoordinates(gridOutline.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsCfgRx", new EntityCoordinates(gridRx.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridRx.Owner, out var pool),
                "the receiver never built a pool, it heard no self-reports at all");
            Assert.That(pool!.Contacts.ContainsKey(gridBlip.Owner),
                "the blip transmitter's self-report never reached the receiver");
            Assert.That(pool.Contacts.ContainsKey(gridOutline.Owner),
                "the control transmitter's self-report never reached the receiver");

            var blip = pool.Contacts[gridBlip.Owner].Sources.Values.First();
            var outline = pool.Contacts[gridOutline.Owner].Sources.Values.First();

            Assert.Multiple(() =>
            {
                Assert.That(blip.RenderMode, Is.EqualTo(KsContactRenderMode.Blip),
                    "selfRenderMode:Blip must render the self-report as a dot");
                Assert.That(outline.RenderMode, Is.EqualTo(KsContactRenderMode.Outline),
                    "the default transmitter should render its self-report as an outline");
            });
        });
    }

    /// <summary>
    ///     announceSelf:false makes a pure relay/repeater: it forwards a contact it
    ///         detected but never files a self-report. The relayed contact still
    ///         arriving proves the datalink works, so the missing self-report is the
    ///         switch's doing.
    /// </summary>
    [Test]
    public async Task TestAnnounceSelfFalseSuppressesSelfReport()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridTx = default(Entity<MapGridComponent>);
        var gridDetected = default(Entity<MapGridComponent>);
        var gridRx = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridTx = MakeShipGrid(mapManager, mapSystem, map.MapId);
            gridDetected = MakeShipGrid(mapManager, mapSystem, map.MapId);
            gridRx = MakeShipGrid(mapManager, mapSystem, map.MapId);

            // The detected target sits within the transmitter's sensor range (200);
            // the receiver sits beyond it but within transmitter range (1000), so
            // it can only learn the target over datalink.
            xformSystem.SetLocalPosition(gridDetected.Owner, new Vector2(50f, 0f));
            xformSystem.SetLocalPosition(gridRx.Owner, new Vector2(500f, 0f));

            entManager.SpawnEntity("KsCfgRelaySensor", new EntityCoordinates(gridTx.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsCfgTxNoAnnounce", new EntityCoordinates(gridTx.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsCfgRx", new EntityCoordinates(gridRx.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridTx.Owner, out var txPool)
                && txPool!.Contacts.ContainsKey(gridDetected.Owner),
                "the transmitter's own sensor should have detected the target, otherwise the relay path is untested");
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridRx.Owner, out var rxPool),
                "the receiver never built a pool, it heard nothing at all");

            Assert.Multiple(() =>
            {
                Assert.That(rxPool!.Contacts.ContainsKey(gridDetected.Owner),
                    "a relayed contact should still cross the datalink, proving the link works");
                Assert.That(rxPool.Contacts.ContainsKey(gridTx.Owner), Is.False,
                    "announceSelf:false must suppress the transmitter's own self-report");
            });
        });
    }

    /// <summary>
    ///     relayContacts:false makes a pure position beacon: it announces its own
    ///         grid but forwards nothing it detected. The self-report still arriving
    ///         proves the datalink works, so the missing relay is the switch's doing.
    /// </summary>
    [Test]
    public async Task TestRelayContactsFalseSuppressesRelay()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var map = await Pair.CreateTestMap();

        var gridTx = default(Entity<MapGridComponent>);
        var gridDetected = default(Entity<MapGridComponent>);
        var gridRx = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridTx = MakeShipGrid(mapManager, mapSystem, map.MapId);
            gridDetected = MakeShipGrid(mapManager, mapSystem, map.MapId);
            gridRx = MakeShipGrid(mapManager, mapSystem, map.MapId);

            xformSystem.SetLocalPosition(gridDetected.Owner, new Vector2(50f, 0f));
            xformSystem.SetLocalPosition(gridRx.Owner, new Vector2(500f, 0f));

            entManager.SpawnEntity("KsCfgRelaySensor", new EntityCoordinates(gridTx.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsCfgTxNoRelay", new EntityCoordinates(gridTx.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsCfgRx", new EntityCoordinates(gridRx.Owner, new Vector2(0.5f, 0.5f)));
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridTx.Owner, out var txPool)
                && txPool!.Contacts.ContainsKey(gridDetected.Owner),
                "the transmitter's own sensor should have detected the target, otherwise the relay test is vacuous");
            Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridRx.Owner, out var rxPool),
                "the receiver never built a pool, it heard nothing at all");

            Assert.Multiple(() =>
            {
                Assert.That(rxPool!.Contacts.ContainsKey(gridTx.Owner),
                    "the transmitter's own self-report should still cross the datalink, proving the link works");
                Assert.That(rxPool.Contacts.ContainsKey(gridDetected.Owner), Is.False,
                    "relayContacts:false must stop the transmitter forwarding contacts it detected");
            });
        });
    }

    /// <summary>8x8, big enough to clear the &lt;10 mass junk filter.</summary>
    private static Entity<MapGridComponent> MakeShipGrid(
        IMapManager mapManager,
        SharedMapSystem mapSystem,
        MapId mapId)
    {
        return MakeGrid(mapManager, mapSystem, mapId, 8, 8);
    }

    private static Entity<MapGridComponent> MakeGrid(
        IMapManager mapManager,
        SharedMapSystem mapSystem,
        MapId mapId,
        int width,
        int height)
    {
        var grid = mapManager.CreateGridEntity(mapId);

        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            }
        }

        mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);
        return grid;
    }
}
