using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.Helpers;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.ChemicalFire;

/// <summary>
///     Behaviour of chemfires: how long they last, how they share a tile, what they do to whatever is
///         standing in them, and how they answer an extinguisher.
/// </summary>
/// <remarks>
///     Everything here runs on prototypes defined by the fixture rather than the shipped ones, so tuning a
///         real chemfire cannot quietly change what these tests mean. The test fires deliberately carry no
///         gas consumer, no ignition effects and <c>heatPower: 0</c>, leaving the tile events as the only
///         thing that could set the flammable in <see cref="TestChemicalFireIgnitesFlammableOnItsTile"/>
///         alight.
/// </remarks>
[TestFixture]
[TestOf(typeof(SharedChemicalFireSystem))]
public sealed class ChemicalFireTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseChemicalFire
  id: KsTestChemicalFire
  components:
  - type: ChemicalFire
    color: '#FF9030'
    duration: 10
    temperature: 1500
    exposedVolume: 50
    heatPower: 0

# Same connection key rules as its parent, just short enough to watch expire.
- type: entity
  parent: KsTestChemicalFire
  id: KsTestChemicalFireBrief
  components:
  - type: ChemicalFire
    duration: 1

# A distinct prototype id, so it takes a distinct connection key and stacks.
- type: entity
  parent: KsTestChemicalFire
  id: KsTestChemicalFireStacking
  components:
  - type: ChemicalFire
    color: '#50D0FF'

# A distinct prototype that deliberately claims the base fire's key, so it must replace rather than stack.
- type: entity
  parent: KsTestChemicalFire
  id: KsTestChemicalFireSharedKey
  components:
  - type: ChemicalFire
    color: '#B040FF'
    connectionKey: KsTestChemicalFire

- type: entity
  id: KsTestFlammable
  name: test flammable
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.25
        hard: false
        layer:
        - MidImpassable
        mask:
        - MidImpassable
  # FlammableSystem pushes fire state onto the appearance whenever fire stacks change.
  - type: Appearance
  - type: Flammable
    fireSpread: false
    canResistFire: false
    damage:
      types:
        Heat: 1
";

    private const string FireProto = "KsTestChemicalFire";
    private const string BriefFireProto = "KsTestChemicalFireBrief";
    private const string StackingFireProto = "KsTestChemicalFireStacking";
    private const string SharedKeyFireProto = "KsTestChemicalFireSharedKey";
    private const string FlammableProto = "KsTestFlammable";

    private const string WaterReagent = "Water";

    private static readonly Vector2i FireTile = Vector2i.Zero;

    /// <summary>One second of simulation, which every duration here is a whole multiple of.</summary>
    private const int TicksPerSecond = 30;

    #region Duration

    [Test]
    public async Task TestChemicalFireRespectsItsPrototypeDuration()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        var testMap = await pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        Entity<ChemicalFireComponent>? briefFire = null;
        Entity<ChemicalFireComponent>? longFire = null;

        await server.WaitAssertion(() =>
        {
            // Stacks rather than replaces, so the long fire is a control for the brief one expiring.
            briefFire = chemicalFireSystem.SpawnChemicalFire(BriefFireProto, (gridUid, null), FireTile);
            longFire = chemicalFireSystem.SpawnChemicalFire(StackingFireProto, (gridUid, null), FireTile);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(briefFire, Is.Not.Null, "One-second chemfire failed to spawn.");
                Assert.That(longFire, Is.Not.Null, "Ten-second chemfire failed to spawn.");
            }
        });

        await server.WaitRunTicks(TicksPerSecond / 2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.EntityExists(briefFire!.Value.Owner),
                "One-second chemfire died within half a second.");
        });

        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entityManager.EntityExists(briefFire!.Value.Owner), Is.False,
                    "One-second chemfire was still alive after a second and a half.");

                Assert.That(entityManager.EntityExists(longFire!.Value.Owner),
                    "Ten-second chemfire expired early.");
            }
        });
    }

    [Test]
    public async Task TestChemicalFireDurationCanBeOverridden()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        var testMap = await pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        Entity<ChemicalFireComponent>? fire = null;

        await server.WaitAssertion(() =>
        {
            // The prototype says ten seconds; the caller says one.
            fire = chemicalFireSystem.SpawnChemicalFire(FireProto, (gridUid, null), FireTile, TimeSpan.FromSeconds(1));

            Assert.That(fire, Is.Not.Null, "Chemfire failed to spawn.");
            Assert.That(fire!.Value.Comp.Duration, Is.EqualTo(TimeSpan.FromSeconds(1)),
                "Duration override did not reach the chemfire.");
        });

        await server.WaitRunTicks(TicksPerSecond / 2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.EntityExists(fire!.Value.Owner),
                "Chemfire died before its overridden duration was up.");
        });

        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.EntityExists(fire!.Value.Owner), Is.False,
                "Chemfire outlived its overridden duration, so the prototype's ten seconds won.");
        });
    }

    /// <summary>
    ///     Re-applying a chemfire restarts it rather than adding a second one, which is the whole reason
    ///         placing one is an "ensure" rather than a spawn.
    /// </summary>
    [Test]
    public async Task TestReapplyingChemicalFireRefreshesItInPlace()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        var testMap = await pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        Entity<ChemicalFireComponent>? fire = null;
        var endTimeBefore = TimeSpan.Zero;

        await server.WaitAssertion(() =>
        {
            fire = chemicalFireSystem.SpawnChemicalFire(FireProto, (gridUid, null), FireTile);
            Assert.That(fire, Is.Not.Null, "Chemfire failed to spawn.");

            endTimeBefore = fire!.Value.Comp.EndTime;
        });

        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            var refreshedFire = chemicalFireSystem.SpawnChemicalFire(FireProto, (gridUid, null), FireTile);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(refreshedFire?.Owner, Is.EqualTo(fire!.Value.Owner),
                    "Re-applying the same chemfire made a second entity instead of refreshing the first.");

                Assert.That(fire!.Value.Comp.EndTime, Is.GreaterThan(endTimeBefore),
                    "Re-applying the same chemfire did not restart its lifetime.");

                Assert.That(GetTileFireCount(chemicalFireSystem, gridUid), Is.EqualTo(1),
                    "Tile holds more than one chemfire after the same one was applied twice.");
            }
        });
    }

    #endregion

    #region Stacking

    [Test]
    public async Task TestChemicalFiresWithDifferentKeysStack()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        var testMap = await pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            var firstFire = chemicalFireSystem.SpawnChemicalFire(FireProto, (gridUid, null), FireTile);
            var secondFire = chemicalFireSystem.SpawnChemicalFire(StackingFireProto, (gridUid, null), FireTile);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(firstFire, Is.Not.Null, "First chemfire failed to spawn.");
                Assert.That(secondFire, Is.Not.Null, "Second chemfire failed to spawn.");
                Assert.That(secondFire?.Owner, Is.Not.EqualTo(firstFire?.Owner),
                    "Two chemfires with different connection keys collapsed into one entity.");

                Assert.That(GetTileFireCount(chemicalFireSystem, gridUid), Is.EqualTo(2),
                    "Tile did not end up holding both chemfires.");

                Assert.That(entityManager.EntityExists(firstFire!.Value.Owner),
                    "First chemfire was removed by the second one stacking on it.");
            }
        });
    }

    [Test]
    public async Task TestChemicalFireReplacesOneSharingItsKey()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        var testMap = await pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            var replacedFire = chemicalFireSystem.SpawnChemicalFire(FireProto, (gridUid, null), FireTile);
            var replacementFire = chemicalFireSystem.SpawnChemicalFire(SharedKeyFireProto, (gridUid, null), FireTile);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(replacedFire, Is.Not.Null, "Chemfire failed to spawn.");
                Assert.That(replacementFire, Is.Not.Null, "Replacement chemfire failed to spawn.");

                Assert.That(entityManager.EntityExists(replacedFire!.Value.Owner), Is.False,
                    "A different prototype claiming the same connection key did not evict the incumbent.");

                Assert.That(GetTileFireCount(chemicalFireSystem, gridUid), Is.EqualTo(1),
                    "Tile holds both chemfires despite them sharing a connection key.");
            }
        });
    }

    /// <summary>
    ///     Chemfires subscribe <c>TileExtinguishEvent</c> so extinguishers can put them out, and raise it on
    ///         their tile when they end. Without excluding chemfires from that raise, one expiring would take
    ///         every chemfire sharing its tile with it.
    /// </summary>
    [Test]
    public async Task TestExpiringChemicalFireDoesNotExtinguishItsTileMates()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        var testMap = await pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        Entity<ChemicalFireComponent>? expiringFire = null;
        Entity<ChemicalFireComponent>? survivingFire = null;

        await server.WaitAssertion(() =>
        {
            entityManager.EnsureComponent<GridAtmosphereComponent>(gridUid);

            expiringFire = chemicalFireSystem.SpawnChemicalFire(BriefFireProto, (gridUid, null), FireTile);
            survivingFire = chemicalFireSystem.SpawnChemicalFire(StackingFireProto, (gridUid, null), FireTile);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(expiringFire, Is.Not.Null, "Chemfire failed to spawn.");
                Assert.That(survivingFire, Is.Not.Null, "Second chemfire failed to stack on the tile.");
            }
        });

        await server.WaitRunTicks(TicksPerSecond * 2);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entityManager.EntityExists(expiringFire!.Value.Owner), Is.False,
                    "Chemfire did not expire.");

                Assert.That(entityManager.EntityExists(survivingFire!.Value.Owner),
                    "Chemfire expiring put out the other chemfire sharing its tile.");
            }
        });
    }

    #endregion

    #region Tile effects

    /// <summary>
    ///     Standing in a chemfire has to read the same as standing in a gas fire, which means the chemfire
    ///         raising the tile fire events a hotspot would have raised.
    /// </summary>
    [Test]
    public async Task TestChemicalFireIgnitesFlammableOnItsTile()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        var testMap = await pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        var flammableUid = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            entityManager.EnsureComponent<GridAtmosphereComponent>(gridUid);

            flammableUid = entityManager.SpawnEntity(FlammableProto, new EntityCoordinates(gridUid, 0.5f, 0.5f));

            Assert.That(entityManager.GetComponent<FlammableComponent>(flammableUid).OnFire, Is.False,
                "Test flammable spawned already alight.");

            Assert.That(chemicalFireSystem.SpawnChemicalFire(FireProto, (gridUid, null), FireTile), Is.Not.Null,
                "Chemfire failed to spawn.");
        });

        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.GetComponent<FlammableComponent>(flammableUid).OnFire,
                "A chemfire spawning on top of a flammable did not set it alight.");
        });
    }

    /// <summary>
    ///     The other half of <see cref="TestChemicalFireIgnitesFlammableOnItsTile"/>: a chemfire burning out has
    ///         to tell its tile that the fire is over.
    /// </summary>
    /// <remarks>
    ///     Deleting an entity detaches it to nullspace before shutting its components down, so a chemfire loses
    ///         the tile it was registered on partway through dying. The shutdown hook has to still know where it
    ///         was burning, or the extinguish is silently dropped.
    /// </remarks>
    [Test]
    public async Task TestExpiringChemicalFireExtinguishesItsTile()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();
        var listenerSystem = entityManager.System<ChemicalFireEventListenerSystem>();

        var testMap = await pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        var flammableUid = EntityUid.Invalid;
        Entity<ChemicalFireComponent>? fire = null;

        await server.WaitAssertion(() =>
        {
            listenerSystem.Clear();

            entityManager.EnsureComponent<GridAtmosphereComponent>(gridUid);

            flammableUid = entityManager.SpawnEntity(FlammableProto, new EntityCoordinates(gridUid, 0.5f, 0.5f));
            entityManager.EnsureComponent<TestListenerComponent>(flammableUid);

            fire = chemicalFireSystem.SpawnChemicalFire(BriefFireProto, (gridUid, null), FireTile);
            Assert.That(fire, Is.Not.Null, "Chemfire failed to spawn.");
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(listenerSystem.GetTileFireEventCount(flammableUid), Is.EqualTo(1),
                    "Chemfire did not announce itself to the tile exactly once.");

                Assert.That(listenerSystem.GetTileExtinguishEventCount(flammableUid), Is.Zero,
                    "Chemfire announced its tile as extinguished while still burning.");
            }
        });

        await server.WaitRunTicks(TicksPerSecond * 2);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entityManager.EntityExists(fire!.Value.Owner), Is.False,
                    "Chemfire did not expire.");

                Assert.That(listenerSystem.GetTileExtinguishEventCount(flammableUid), Is.EqualTo(1),
                    "Chemfire expiring did not extinguish its tile exactly once.");
            }
        });
    }

    #endregion

    #region Extinguishing

    /// <summary>
    ///     A fire extinguisher sprays water, water carries an <c>ExtinguishTileReaction</c>, that reaction
    ///         gates on <see cref="AtmosphereSystem.IsHotspotActive"/> and then calls
    ///         <see cref="AtmosphereSystem.HotspotExtinguish"/>. Chemfires answer both ends of that.
    /// </summary>
    [Test]
    public async Task TestExtinguisherPutsOutChemicalFire()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var atmosphereSystem = entityManager.System<AtmosphereSystem>();
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();
        var puddleSystem = entityManager.System<SharedPuddleSystem>();

        var testMap = await pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        Entity<ChemicalFireComponent>? fire = null;

        await server.WaitAssertion(() =>
        {
            // HotspotExtinguish only reaches the tile's entities through a grid atmosphere.
            entityManager.EnsureComponent<GridAtmosphereComponent>(gridUid);

            Assert.That(atmosphereSystem.IsHotspotActive(gridUid, FireTile), Is.False,
                "Test tile is somehow already on fire.");

            fire = chemicalFireSystem.SpawnChemicalFire(FireProto, (gridUid, null), FireTile);
            Assert.That(fire, Is.Not.Null, "Chemfire failed to spawn.");
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            // Note there is nothing burnable in the air - the chemfire alone has to make this true, or the
            //     water's tile reaction bails out before it can extinguish anything.
            Assert.That(atmosphereSystem.IsHotspotActive(gridUid, FireTile),
                "Chemfire did not report its tile as burning.");

            var water = new Solution(WaterReagent, FixedPoint2.New(100));
            puddleSystem.DoTileReactions(testMap.Tile, water);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entityManager.EntityExists(fire!.Value.Owner), Is.False,
                    "Chemfire survived being sprayed with water.");

                Assert.That(atmosphereSystem.IsHotspotActive(gridUid, FireTile), Is.False,
                    "Tile still reports as burning after its only chemfire was extinguished.");
            }
        });
    }

    /// <summary>
    ///     The <see cref="ChemicalFireComponent.Extinguishable"/> opt-out, for chemfires that are meant to
    ///         burn through a dousing.
    /// </summary>
    [Test]
    public async Task TestInextinguishableChemicalFireSurvives()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();
        var puddleSystem = entityManager.System<SharedPuddleSystem>();

        var testMap = await pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        Entity<ChemicalFireComponent>? fire = null;

        await server.WaitAssertion(() =>
        {
            entityManager.EnsureComponent<GridAtmosphereComponent>(gridUid);

            fire = chemicalFireSystem.SpawnChemicalFire(FireProto, (gridUid, null), FireTile);
            Assert.That(fire, Is.Not.Null, "Chemfire failed to spawn.");

            fire!.Value.Comp.Extinguishable = false;
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var water = new Solution(WaterReagent, FixedPoint2.New(100));
            puddleSystem.DoTileReactions(testMap.Tile, water);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.EntityExists(fire!.Value.Owner),
                "Inextinguishable chemfire was put out anyway.");
        });
    }

    #endregion

    private static int GetTileFireCount(SharedChemicalFireSystem chemicalFireSystem, EntityUid gridUid)
        => chemicalFireSystem.GetTileChemicalFires((gridUid, null), FireTile)?.Fires.Count ?? 0;
}
