#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.Fluids.HotPuddle;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Fluids;
using Content.Shared._KS14.TileEffects.Effects;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Item.ItemToggle;
using Content.Shared.StepTrigger.Components;
using Content.Shared.Temperature;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     Hot things lying in puddles heat them: a lit welder dropped in welding fuel sets it alight.
/// </summary>
[TestFixture]
[TestOf(typeof(KsHotPuddleSystem))]
[TestOf(typeof(IsHotEvent))]
public sealed class KsHotPuddleTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseItem
  id: KsTestHotPuddleHotItem
  name: test hot item
  components:
  - type: AlwaysHot

- type: entity
  parent: BaseItem
  id: KsTestHotPuddleColdItem
  name: test cold item

# Hot only through ItemToggleHot: a welder is also an ignition source, which answers for it as well.
- type: entity
  parent: BaseItem
  id: KsTestHotPuddleToggleHotItem
  name: test toggle hot item
  components:
  - type: ItemToggle
  - type: ItemToggleHot

# Touches puddles with two fixtures: its own, and the wider one it gets for being flammable.
- type: entity
  parent: BaseItem
  id: KsTestHotPuddleFlammableItem
  name: test flammable item
  components:
  - type: Appearance
  - type: Flammable
    fireSpread: false
    canResistFire: false
    damage:
      types:
        Heat: 1

# A mob's own fixture is kept off the puddle's touch fixture; its wider flammable one is not.
- type: entity
  id: KsTestHotPuddleMob
  name: test mob
  components:
  - type: MobState
  - type: Physics
    bodyType: KinematicController
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.25
        hard: true
        layer:
        - MobLayer
        mask:
        - MobMask
  - type: Appearance
  - type: Flammable
    fireSpread: false
    canResistFire: false
    damage:
      types:
        Heat: 1

- type: reagent
  id: KsTestHotPuddleInertLiquid
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
";

    private static readonly ProtoId<ReagentPrototype> WeldingFuelReagent = "WeldingFuel";
    private static readonly EntProtoId WelderPrototype = "Welder";
    private static readonly EntProtoId PuddlePrototype = "Puddle";
    private static readonly EntProtoId BatPrototype = "MobBat";
    private static readonly EntProtoId HumanPrototype = "MobHuman";
    private static readonly EntProtoId BulletPrototype = "BulletPistol";
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<DamageTypePrototype> PiercingDamage = "Piercing";

    // Deliberately NOT static: these only exist in the test YAML above, and the YAML linter validates static ids.
    private readonly EntProtoId _hotItemPrototype = "KsTestHotPuddleHotItem";
    private readonly EntProtoId _coldItemPrototype = "KsTestHotPuddleColdItem";
    private readonly EntProtoId _toggleHotItemPrototype = "KsTestHotPuddleToggleHotItem";
    private readonly EntProtoId _flammableItemPrototype = "KsTestHotPuddleFlammableItem";
    private readonly EntProtoId _mobPrototype = "KsTestHotPuddleMob";
    private readonly ProtoId<ReagentPrototype> _inertLiquidReagent = "KsTestHotPuddleInertLiquid";

    private const string SlipFixtureId = "slipFixture";
    private const int TicksPerSecond = 30;
    private const int RowLength = 6;

    /// <summary>
    ///     A test map with, if asked, a row of floor and breathable air. Without air nothing exchanges heat with the
    ///         puddles, so what they gain is only what is put in.
    /// </summary>
    /// <remarks>
    ///     Puddles over <see cref="PuddleComponent.OverflowVolume"/> spread onto floored neighbours, so on a row the
    ///         tests keep them under it.
    /// </remarks>
    private async Task<TestMapData> CreateTestMap(bool breathable, bool floorRow = true)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var mapSystem = server.EntMan.System<SharedMapSystem>();
            for (var x = 1; floorRow && x < RowLength; x++)
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), testMap.Tile.Tile);

            if (!breathable)
                return;

            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);

            server.EntMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
        });

        await server.WaitRunTicks(30);
        return testMap;
    }

    private EntityUid SpillPuddle(TestMapData testMap, Vector2i tile, ProtoId<ReagentPrototype> reagent, float volume, float temperature)
    {
        var entityManager = Pair.Server.EntMan;
        var tileRef = entityManager.System<SharedMapSystem>().GetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, tile);

        var spilled = new Solution(reagent.Id, FixedPoint2.New(volume)) { Temperature = temperature };
        Assert.That(entityManager.System<PuddleSystem>().TrySpillAt(tileRef, spilled, out var puddleUid, sound: false, tileReact: false),
            $"Puddle failed to spill on {tile}.");

        return puddleUid;
    }

    private Solution PuddleSolution(EntityUid puddleUid)
    {
        var entityManager = Pair.Server.EntMan;
        var puddleComponent = entityManager.GetComponent<PuddleComponent>(puddleUid);
        Assert.That(entityManager.System<SharedSolutionContainerSystem>()
                .ResolveSolution(puddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution, out var solution),
            "Puddle has no solution.");
        return solution!;
    }

    private float ThermalEnergy(EntityUid puddleUid)
        => PuddleSolution(puddleUid).GetThermalEnergy(Pair.Server.ProtoMan);

    private float Temperature(EntityUid puddleUid)
        => PuddleSolution(puddleUid).Temperature;

    /// <summary>
    ///     The puddles something touches, or null if it touches none. The component outlives the last puddle by up to a
    ///         check, with an empty set, which counts as touching none.
    /// </summary>
    private HashSet<EntityUid>? Touched(EntityUid uid)
    {
        var puddles = Pair.Server.EntMan.GetComponentOrNull<KsTouchingPuddleComponent>(uid)?.Puddles;
        return puddles is { Count: > 0 } ? puddles.ToHashSet() : null;
    }

    /// <summary>
    ///     Moves something and wakes it, as dropping it would: physics only updates the contacts of a body that is awake.
    /// </summary>
    private async Task MoveTo(EntityUid uid, TestMapData testMap, float x)
    {
        await Pair.Server.WaitPost(() =>
        {
            Pair.Server.EntMan.System<SharedTransformSystem>().SetCoordinates(uid, new EntityCoordinates(testMap.Grid.Owner, x, 0.5f));
            Pair.Server.EntMan.System<SharedPhysicsSystem>().WakeBody(uid);
        });
        await Pair.Server.WaitRunTicks(3);
    }

    /// <summary>
    ///     Runs until just after the entity's next check, and no further than that one check.
    /// </summary>
    private async Task RunPastNextCheck(EntityUid uid)
    {
        var server = Pair.Server;
        var ticks = 0;
        await server.WaitAssertion(() =>
        {
            var touchingComponent = server.EntMan.GetComponent<KsTouchingPuddleComponent>(uid);
            var remaining = touchingComponent.NextCheck - server.Timing.CurTime;
            ticks = Math.Max(1, (int)Math.Ceiling(remaining / server.Timing.TickPeriod) + 1);
            Assert.That(ticks * server.Timing.TickPeriod, Is.LessThan(server.EntMan.System<KsHotPuddleSystem>().CheckInterval * 2),
                "Running past the next check would run past the one after it as well.");
        });
        await server.WaitRunTicks(ticks);
    }

    /// <summary>
    ///     A lit welder dropped in welding fuel at room temperature sets it alight within a few seconds, as an open
    ///         flame rather than by heating the fuel to its ignition point; an unlit one, or something that is never hot,
    ///         leaves it as it was.
    /// </summary>
    [Test]
    public async Task TestHotThingInFuelSetsItAlight([Values("lit", "unlit", "cold")] string source)
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        // One tile only, so the 100u stays in one puddle rather than spreading.
        var testMap = await CreateTestMap(breathable: true, floorRow: false);
        var gridUid = testMap.Grid.Owner;
        var tile = testMap.Tile.GridIndices;
        EntityUid puddleUid = default;
        EntityUid itemUid = default;
        var startTemperature = 0f;
        var heatCapacity = 0f;

        await server.WaitPost(() =>
        {
            puddleUid = SpillPuddle(testMap, tile, WeldingFuelReagent, 100f, Atmospherics.T20C);
            startTemperature = Temperature(puddleUid);
            heatCapacity = PuddleSolution(puddleUid).GetHeatCapacity(SProtoMan);

            // Dropped in the fuel.
            itemUid = entityManager.SpawnEntity(source == "cold" ? _coldItemPrototype : WelderPrototype, new EntityCoordinates(gridUid, 0.5f, 0.5f));
            entityManager.System<SharedPhysicsSystem>().WakeBody(itemUid);
            if (source == "lit")
                Assert.That(entityManager.System<ItemToggleSystem>().TryActivate(itemUid, user: null, predicted: false), "The welder did not light.");
        });
        await server.WaitRunTicks(3);

        // Every item lying in a puddle touches it, through the puddle's touch fixture; only a hot one heats it.
        await server.WaitAssertion(() => Assert.That(Touched(itemUid) ?? [], Is.EquivalentTo(new[] { puddleUid }),
            $"The {source} item lying in the puddle is not touching it."));

        await server.WaitRunTicks(TicksPerSecond * 3);

        await server.WaitAssertion(() =>
        {
            var burning = chemicalFireSystem.GetTileChemicalFires((gridUid, null), tile) is { Fires.Count: > 0 };
            if (source == "lit")
            {
                // The flame's doing, not the heat's: the most the welder could have warmed the fuel by falls short.
                var ignitionTemperature = SProtoMan.Index(WeldingFuelReagent).KsTileEffects
                    .OfType<LogicAndTileEffect>()
                    .SelectMany(effect => effect.Conditions)
                    .OfType<CheckTemperatureTileEffect>()
                    .Single(condition => condition.LitByFlame)
                    .Minimum!.Value;
                Assert.That(startTemperature + KsHotPuddleSystem.HeatPower * 3f / heatCapacity, Is.LessThan(ignitionTemperature),
                    "The welder's heat alone could light the fuel, so this proves nothing about its flame.");

                Assert.That(burning, $"A lit welder lying in welding fuel did not set it alight within 3 seconds (puddle at {(entityManager.EntityExists(puddleUid) ? Temperature(puddleUid) : -1f)} K, touching {Touched(itemUid)?.Count ?? 0}).");
                return;
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(burning, Is.False, $"The fuel lit with a {source} item lying in it.");
                Assert.That(Temperature(puddleUid), Is.EqualTo(startTemperature).Within(0.5f), $"A {source} item heated the puddle.");
            }
        });
    }

    /// <summary>
    ///     Something touching puddles on two tiles heats both; moving off one stops heating that one; moving off both
    ///         takes it out of puddles altogether.
    /// </summary>
    [Test]
    public async Task TestHeatsEveryPuddleTouched()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var testMap = await CreateTestMap(breathable: false);
        var gridUid = testMap.Grid.Owner;
        EntityUid firstPuddleUid = default;
        EntityUid secondPuddleUid = default;
        EntityUid itemUid = default;

        await server.WaitPost(() =>
        {
            firstPuddleUid = SpillPuddle(testMap, new Vector2i(0, 0), _inertLiquidReagent, 40f, Atmospherics.T20C);
            secondPuddleUid = SpillPuddle(testMap, new Vector2i(1, 0), _inertLiquidReagent, 40f, Atmospherics.T20C);
            itemUid = entityManager.SpawnEntity(_hotItemPrototype, new EntityCoordinates(gridUid, 4.5f, 0.5f));
        });
        await server.WaitRunTicks(5);
        await server.WaitAssertion(() => Assert.That(Touched(itemUid), Is.Null, "The item touches puddles before it reached any."));

        // On the seam between the two puddles.
        await MoveTo(itemUid, testMap, 1f);
        await server.WaitRunTicks(TicksPerSecond * 2);

        var firstTemperature = 0f;
        var secondTemperature = 0f;
        await server.WaitAssertion(() =>
        {
            firstTemperature = Temperature(firstPuddleUid);
            secondTemperature = Temperature(secondPuddleUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Touched(itemUid) ?? [], Is.EquivalentTo(new[] { firstPuddleUid, secondPuddleUid }),
                    "On the seam, the item is not touching both puddles.");
                Assert.That(firstTemperature, Is.GreaterThan(Atmospherics.T20C + 1f), "The first puddle was not heated.");
                Assert.That(secondTemperature, Is.GreaterThan(Atmospherics.T20C + 1f), "The second puddle was not heated.");
            }
        });

        // Only on the second.
        await MoveTo(itemUid, testMap, 1.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Touched(itemUid) ?? [], Is.EquivalentTo(new[] { secondPuddleUid }),
                "Leaving one of two puddles did not leave the item in the other alone.");
            firstTemperature = Temperature(firstPuddleUid);
            secondTemperature = Temperature(secondPuddleUid);
        });

        await server.WaitRunTicks(TicksPerSecond * 2);
        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                // The puddle left behind still warms a little, by conduction from its heated neighbour, but far less.
                var firstGain = Temperature(firstPuddleUid) - firstTemperature;
                var secondGain = Temperature(secondPuddleUid) - secondTemperature;
                Assert.That(secondGain, Is.GreaterThan(1f), "The puddle the item stayed in stopped being heated.");
                Assert.That(firstGain, Is.LessThan(secondGain * 0.5f), "The puddle the item left was still heated.");
            }
        });

        await MoveTo(itemUid, testMap, 4.5f);
        await server.WaitAssertion(() => Assert.That(Touched(itemUid), Is.Null, "Leaving every puddle did not take the item out of puddles."));
    }

    /// <summary>
    ///     Something touching a puddle's touch fixture with several fixtures is touching it until the last of them
    ///         leaves.
    /// </summary>
    [Test]
    public async Task TestTouchingWithSeveralFixtures()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var testMap = await CreateTestMap(breathable: false);
        EntityUid firstPuddleUid = default;
        EntityUid secondPuddleUid = default;
        EntityUid itemUid = default;

        await server.WaitPost(() =>
        {
            firstPuddleUid = SpillPuddle(testMap, new Vector2i(0, 0), _inertLiquidReagent, 40f, Atmospherics.T20C);
            secondPuddleUid = SpillPuddle(testMap, new Vector2i(1, 0), _inertLiquidReagent, 40f, Atmospherics.T20C);
            itemUid = entityManager.SpawnEntity(_flammableItemPrototype, new EntityCoordinates(testMap.Grid.Owner, 4.5f, 0.5f));
        });
        await server.WaitRunTicks(5);

        await MoveTo(itemUid, testMap, 1f);
        await server.WaitAssertion(() => Assert.That(Touched(itemUid) ?? [], Is.EquivalentTo(new[] { firstPuddleUid, secondPuddleUid }),
            "On the seam, the item is not touching both puddles."));

        // Its own fixture (to 0.95) off the first puddle (to 0.9), but its wider flammable one (to 0.85) still on it.
        await MoveTo(itemUid, testMap, 1.2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(TouchFixturesTouching(itemUid, firstPuddleUid), Is.EqualTo(1),
                "At 1.2 the item should touch the first puddle's touch fixture with its flammable fixture alone.");
            Assert.That(Touched(itemUid) ?? [], Is.EquivalentTo(new[] { firstPuddleUid, secondPuddleUid }),
                "One of the item's fixtures leaving a puddle took it out of that puddle while another still touched it.");
        });

        await MoveTo(itemUid, testMap, 1.5f);
        await server.WaitAssertion(() => Assert.That(Touched(itemUid) ?? [], Is.EquivalentTo(new[] { secondPuddleUid }),
            "With both its fixtures off the first puddle, the item is still touching it."));
    }

    /// <summary>
    ///     A mob's hard fixture never touches a puddle's touch fixture, so how it steps on puddles is left to the
    ///         <c>slipFixture</c> as before. It is still in the puddle, through its non-hard flammable fixture, which
    ///         is how a mob on fire heats one.
    /// </summary>
    [Test]
    public async Task TestMobTouchesThroughItsFlammableFixtureOnly()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var testMap = await CreateTestMap(breathable: false);
        EntityUid puddleUid = default;
        EntityUid mobUid = default;

        await server.WaitPost(() =>
        {
            puddleUid = SpillPuddle(testMap, new Vector2i(1, 0), _inertLiquidReagent, 40f, Atmospherics.T20C);
            mobUid = entityManager.SpawnEntity(_mobPrototype, new EntityCoordinates(testMap.Grid.Owner, 4.5f, 0.5f));
        });
        await server.WaitRunTicks(5);

        await MoveTo(mobUid, testMap, 1.5f);
        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(HardFixturesTouching(mobUid, puddleUid, KsPuddleTouchSystem.TouchFixtureId), Is.Zero,
                    "The mob's hard fixture touches the puddle's touch fixture.");
                Assert.That(HardFixturesTouching(mobUid, puddleUid, SlipFixtureId), Is.EqualTo(1),
                    "The mob's hard fixture does not touch the puddle's slip fixture, so it could not slip on it.");
                Assert.That(TouchFixturesTouching(mobUid, puddleUid), Is.EqualTo(1),
                    "The mob's flammable fixture does not touch the puddle's touch fixture.");
                Assert.That(Touched(mobUid) ?? [], Is.EquivalentTo(new[] { puddleUid }), "The mob is not in the puddle.");
            }
        });
    }

    /// <summary>
    ///     A flying mob's mask is the same as an item's, so the touch fixture is one it would touch. It is kept off it,
    ///         and steps on puddles no more than it did before they had one.
    /// </summary>
    [Test]
    public async Task TestFlyingMobDoesNotStepOnPuddles()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var testMap = await CreateTestMap(breathable: true);
        EntityUid puddleUid = default;
        EntityUid batUid = default;

        await server.WaitPost(() =>
        {
            puddleUid = SpillPuddle(testMap, new Vector2i(1, 0), WaterReagent, 30f, Atmospherics.T20C);
            batUid = entityManager.SpawnEntity(BatPrototype, new EntityCoordinates(testMap.Grid.Owner, 4.5f, 0.5f));
        });
        await server.WaitRunTicks(5);

        await MoveTo(batUid, testMap, 1.5f);
        await server.WaitAssertion(() =>
        {
            var stepTriggerComponent = entityManager.GetComponent<StepTriggerComponent>(puddleUid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(stepTriggerComponent.Active, "The water puddle is not slippery, so this proves nothing.");
                Assert.That(HardFixturesTouching(batUid, puddleUid, fixtureId: null), Is.Zero,
                    "A bat's hard fixture touches a puddle.");
                Assert.That(stepTriggerComponent.Colliding, Does.Not.Contain(batUid), "A bat is stepping on a puddle.");
            }
        });
    }

    /// <summary>
    ///     A bullet fired across puddles still hits what is behind them.
    /// </summary>
    [Test]
    public async Task TestBulletCrossesPuddles()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var testMap = await CreateTestMap(breathable: true);
        var gridUid = testMap.Grid.Owner;
        var puddleUids = new List<EntityUid>();
        EntityUid targetUid = default;

        await server.WaitPost(() =>
        {
            for (var x = 1; x < 4; x++)
                puddleUids.Add(SpillPuddle(testMap, new Vector2i(x, 0), WaterReagent, 30f, Atmospherics.T20C));

            targetUid = entityManager.SpawnEntity(HumanPrototype, new EntityCoordinates(gridUid, 5.5f, 0.5f));
        });
        await server.WaitRunTicks(5);

        await server.WaitPost(() =>
        {
            var bulletUid = entityManager.SpawnEntity(BulletPrototype, new EntityCoordinates(gridUid, 0.5f, 0.5f));
            entityManager.System<SharedGunSystem>().ShootProjectile(bulletUid, new Vector2(1f, 0f), Vector2.Zero, gunUid: null, speed: 10f);
        });
        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var puddleUid in puddleUids)
                    Assert.That(entityManager.EntityExists(puddleUid), "A puddle the bullet crossed is gone, so this proves nothing.");

                // Obsolete, but the one way a test may read damage: the component's own field is [Access]-restricted.
#pragma warning disable CS0618
                var damage = entityManager.System<DamageableSystem>().GetAllDamage(targetUid).DamageDict.GetValueOrDefault(PiercingDamage);
#pragma warning restore CS0618
                Assert.That(damage, Is.GreaterThan(FixedPoint2.Zero), "A bullet fired across puddles did not hit the target behind them.");
            }
        });
    }

    /// <summary>
    ///     How many of the entity's fixtures are touching the puddle's touch fixture.
    /// </summary>
    private int TouchFixturesTouching(EntityUid uid, EntityUid puddleUid)
    {
        var count = 0;
        var contacts = Pair.Server.EntMan.System<SharedPhysicsSystem>().GetContacts(uid);
        while (contacts.MoveNext(out var contact))
        {
            if (contact.IsTouching &&
                (contact.EntityA == puddleUid && contact.FixtureAId == KsPuddleTouchSystem.TouchFixtureId ||
                 contact.EntityB == puddleUid && contact.FixtureBId == KsPuddleTouchSystem.TouchFixtureId))
                count++;
        }

        return count;
    }

    /// <summary>
    ///     How many of the entity's hard fixtures are touching the puddle's <paramref name="fixtureId"/> fixture, or
    ///         any of its fixtures if that is null.
    /// </summary>
    private int HardFixturesTouching(EntityUid uid, EntityUid puddleUid, string? fixtureId)
    {
        var count = 0;
        var contacts = Pair.Server.EntMan.System<SharedPhysicsSystem>().GetContacts(uid);
        while (contacts.MoveNext(out var contact))
        {
            if (!contact.IsTouching)
                continue;

            var puddleIsA = contact.EntityA == puddleUid;
            if (!puddleIsA && contact.EntityB != puddleUid)
                continue;

            var puddleFixtureId = puddleIsA ? contact.FixtureAId : contact.FixtureBId;
            var ourFixture = puddleIsA ? contact.FixtureB : contact.FixtureA;
            if (ourFixture is { Hard: true } && (fixtureId == null || puddleFixtureId == fixtureId))
                count++;
        }

        return count;
    }

    /// <summary>
    ///     A puddle that goes while something touches it is gone from what it touches at once: deleted outright, or
    ///         merged into the puddle already on its tile.
    /// </summary>
    [Test]
    public async Task TestNoStaleEntryForAPuddleThatGoes()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var testMap = await CreateTestMap(breathable: false);
        var gridUid = testMap.Grid.Owner;
        EntityUid firstPuddleUid = default;
        EntityUid secondPuddleUid = default;
        EntityUid itemUid = default;

        await server.WaitPost(() =>
        {
            firstPuddleUid = SpillPuddle(testMap, new Vector2i(0, 0), _inertLiquidReagent, 40f, Atmospherics.T20C);
            secondPuddleUid = SpillPuddle(testMap, new Vector2i(1, 0), _inertLiquidReagent, 40f, Atmospherics.T20C);
            itemUid = entityManager.SpawnEntity(_hotItemPrototype, new EntityCoordinates(gridUid, 4.5f, 0.5f));
        });
        await server.WaitRunTicks(5);

        await MoveTo(itemUid, testMap, 1f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Touched(itemUid) ?? [], Is.EquivalentTo(new[] { firstPuddleUid, secondPuddleUid }), "The item is not in both puddles.");
            entityManager.DeleteEntity(firstPuddleUid);
            Assert.That(Touched(itemUid) ?? [], Is.EquivalentTo(new[] { secondPuddleUid }), "Deleting a puddle left it in what touched it.");
        });

        // A second puddle turning up on the occupied tile is merged into the one there and deleted, in PuddleSystem's update.
        //     In practice that happens before physics has made a contact with it, so this only checks that the merge
        //     leaves nothing behind; the deletion above is what exercises the release, and the merge deletes the same way.
        EntityUid newcomerUid = default;
        await server.WaitPost(() =>
        {
            newcomerUid = entityManager.SpawnEntity(PuddlePrototype, new EntityCoordinates(gridUid, 1.5f, 0.5f));
            entityManager.System<SharedPhysicsSystem>().WakeBody(itemUid);
        });

        for (var i = 0; i < 10; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                foreach (var puddleUid in Touched(itemUid) ?? [])
                {
                    Assert.That(entityManager.Deleted(puddleUid) || entityManager.IsQueuedForDeletion(puddleUid), Is.False,
                        "The item is touching a puddle that is gone or going.");
                }
            });
        }

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entityManager.Deleted(newcomerUid), "The newcomer puddle was not merged away, so this proves nothing about merging.");
                Assert.That(Touched(itemUid) ?? [], Is.EquivalentTo(new[] { secondPuddleUid }), "After the merge, the item is not in only the remaining puddle.");
            }

            entityManager.DeleteEntity(secondPuddleUid);
            Assert.That(Touched(itemUid), Is.Null, "Deleting the last puddle the item touched did not take it out of puddles.");
        });

        // A puddle that stops being one without being deleted is left as well: the release is on the component's
        //     shutdown, not on the entity's deletion.
        EntityUid thirdPuddleUid = default;
        await server.WaitPost(() => thirdPuddleUid = SpillPuddle(testMap, new Vector2i(2, 0), _inertLiquidReagent, 40f, Atmospherics.T20C));
        await MoveTo(itemUid, testMap, 2.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Touched(itemUid) ?? [], Is.EquivalentTo(new[] { thirdPuddleUid }), "The item is not in the third puddle.");
            entityManager.RemoveComponent<PuddleComponent>(thirdPuddleUid);
            Assert.That(Touched(itemUid), Is.Null, "Removing the puddle's PuddleComponent did not take the item out of it.");
        });
    }

    /// <summary>
    ///     Each check puts <see cref="KsHotPuddleSystem.HeatPower"/> times the interval into the puddles touched, split
    ///         between them, and never heats a puddle past <see cref="KsHotPuddleSystem.SourceTemperature"/>.
    /// </summary>
    [Test]
    public async Task TestHeatIsBoundedPowerAndTemperature()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var testMap = await CreateTestMap(breathable: false);
        var gridUid = testMap.Grid.Owner;
        EntityUid firstPuddleUid = default;
        EntityUid secondPuddleUid = default;
        EntityUid itemUid = default;

        await server.WaitPost(() =>
        {
            firstPuddleUid = SpillPuddle(testMap, new Vector2i(0, 0), _inertLiquidReagent, 40f, Atmospherics.T20C);
            // Close to the source's temperature, so that a check would take it past.
            secondPuddleUid = SpillPuddle(testMap, new Vector2i(3, 0), _inertLiquidReagent, 10f, KsHotPuddleSystem.SourceTemperature - 1f);
            itemUid = entityManager.SpawnEntity(_hotItemPrototype, new EntityCoordinates(gridUid, 5.5f, 0.5f));
        });
        await server.WaitRunTicks(5);

        // Split: on the seam of the first puddle and an empty tile it touches only one, so put a second puddle beside it.
        EntityUid neighbourPuddleUid = default;
        await server.WaitPost(() => neighbourPuddleUid = SpillPuddle(testMap, new Vector2i(1, 0), _inertLiquidReagent, 40f, Atmospherics.T20C));
        await MoveTo(itemUid, testMap, 1f);

        var energies = new float[2];
        await server.WaitAssertion(() =>
        {
            Assert.That(Touched(itemUid) ?? [], Is.EquivalentTo(new[] { firstPuddleUid, neighbourPuddleUid }), "The item is not in both puddles.");
            energies[0] = ThermalEnergy(firstPuddleUid);
            energies[1] = ThermalEnergy(neighbourPuddleUid);
        });

        await RunPastNextCheck(itemUid);

        await server.WaitAssertion(() =>
        {
            var expected = KsHotPuddleSystem.HeatPower * (float)server.EntMan.System<KsHotPuddleSystem>().CheckInterval.TotalSeconds / 2f;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(ThermalEnergy(firstPuddleUid) - energies[0], Is.EqualTo(expected).Within(expected * 0.01f),
                    "One check did not put half the power times the interval into the first puddle.");
                Assert.That(ThermalEnergy(neighbourPuddleUid) - energies[1], Is.EqualTo(expected).Within(expected * 0.01f),
                    "One check did not put half the power times the interval into the second puddle.");
            }
        });

        // Capped at the source's temperature, check after check.
        await MoveTo(itemUid, testMap, 3.5f);
        for (var i = 0; i < 3; i++)
        {
            await RunPastNextCheck(itemUid);
            await server.WaitAssertion(() =>
            {
                Assert.That(Touched(itemUid) ?? [], Is.EquivalentTo(new[] { secondPuddleUid }), "The item is not in the hot puddle.");
                Assert.That(Temperature(secondPuddleUid), Is.EqualTo(KsHotPuddleSystem.SourceTemperature).Within(0.01f),
                    "The puddle was not heated to exactly the source's temperature, and no further.");
            });
        }
    }

    /// <summary>
    ///     Stepping out of a puddle and back in does not bring the next check forward: a hot thing that keeps doing
    ///         it heats the puddle no faster than one that stays.
    /// </summary>
    [Test]
    public async Task TestReenteringDoesNotHeatFaster()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        // Long enough for the item to go in and out several times within one interval: each move takes a few ticks
        //     of physics, more than the default interval.
        await OverrideCVar(Side.Server, KsCCVars.AtmosHotPuddleCheckInterval, 1f);

        var testMap = await CreateTestMap(breathable: false);
        var gridUid = testMap.Grid.Owner;
        EntityUid puddleUid = default;
        EntityUid itemUid = default;

        await server.WaitPost(() =>
        {
            puddleUid = SpillPuddle(testMap, new Vector2i(0, 0), _inertLiquidReagent, 40f, Atmospherics.T20C);
            itemUid = entityManager.SpawnEntity(_hotItemPrototype, new EntityCoordinates(gridUid, 4.5f, 0.5f));
        });
        await server.WaitRunTicks(5);

        var startEnergy = 0f;
        var startTime = TimeSpan.Zero;
        await server.WaitPost(() =>
        {
            startEnergy = ThermalEnergy(puddleUid);
            startTime = server.Timing.CurTime;
        });

        // In, which heats it once at once, then out and back in three times.
        await MoveTo(itemUid, testMap, 0.5f);
        for (var i = 0; i < 3; i++)
        {
            await MoveTo(itemUid, testMap, 4.5f);
            await server.WaitAssertion(() => Assert.That(Touched(itemUid), Is.Null, "The item did not leave the puddle."));
            await MoveTo(itemUid, testMap, 0.5f);
            await server.WaitAssertion(() => Assert.That(Touched(itemUid) ?? [], Is.EquivalentTo(new[] { puddleUid }), "The item did not go back into the puddle."));
        }

        var pulse = KsHotPuddleSystem.HeatPower * (float)server.EntMan.System<KsHotPuddleSystem>().CheckInterval.TotalSeconds;
        await server.WaitAssertion(() =>
        {
            Assert.That(server.Timing.CurTime - startTime, Is.LessThan(server.EntMan.System<KsHotPuddleSystem>().CheckInterval),
                "The test took a whole interval, so a second check was due anyway.");
            Assert.That(ThermalEnergy(puddleUid) - startEnergy, Is.EqualTo(pulse).Within(pulse * 0.01f),
                "Coming back into the puddle within an interval heated it again.");
        });

        // Control: once the interval is up, it heats again.
        await RunPastNextCheck(itemUid);
        await server.WaitAssertion(() =>
            Assert.That(ThermalEnergy(puddleUid) - startEnergy, Is.EqualTo(pulse * 2f).Within(pulse * 0.01f),
                "The item stopped heating the puddle after the interval was up."));
    }

    /// <summary>
    ///     <see cref="IsHotEvent"/> as a by-ref struct still carries each answer back to whoever raised it.
    /// </summary>
    [Test]
    public async Task TestIsHotEventReportsBack()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var testMap = await CreateTestMap(breathable: false);

        await server.WaitAssertion(() =>
        {
            bool IsHot(EntityUid uid)
            {
                var isHotEvent = new IsHotEvent();
                entityManager.EventBus.RaiseLocalEvent(uid, ref isHotEvent);
                return isHotEvent.IsHot;
            }

            var hotUid = entityManager.SpawnEntity(_hotItemPrototype, testMap.GridCoords);
            var coldUid = entityManager.SpawnEntity(_coldItemPrototype, testMap.GridCoords);
            var welderUid = entityManager.SpawnEntity(WelderPrototype, testMap.GridCoords);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(IsHot(hotUid), "An always-hot item is not hot.");
                Assert.That(IsHot(coldUid), Is.False, "An ordinary item is hot.");
                Assert.That(IsHot(welderUid), Is.False, "An unlit welder is hot.");
            }

            Assert.That(entityManager.System<ItemToggleSystem>().TryActivate(welderUid, user: null, predicted: false), "The welder did not light.");
            Assert.That(IsHot(welderUid), "A lit welder is not hot.");

            entityManager.System<ItemToggleSystem>().TryDeactivate(welderUid, user: null, predicted: false);
            Assert.That(IsHot(welderUid), Is.False, "A welder put out again is still hot.");

            var toggleUid = entityManager.SpawnEntity(_toggleHotItemPrototype, testMap.GridCoords);
            Assert.That(IsHot(toggleUid), Is.False, "An item that is hot when toggled on is hot while off.");
            Assert.That(entityManager.System<ItemToggleSystem>().TryActivate(toggleUid, user: null, predicted: false), "The toggle item did not turn on.");
            Assert.That(IsHot(toggleUid), "An item that is hot when toggled on is not hot while on.");
        });
    }
}
