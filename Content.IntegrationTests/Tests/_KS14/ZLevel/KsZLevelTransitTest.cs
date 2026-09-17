#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Helpers;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage.Systems;
using Content.Shared.Stunnable;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Physics;
using Content.Shared.FixedPoint;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.ZLevel;

/// <summary>
///     Vertical cross-z-level movement: starting, accelerating, crossing, landing, and everything that is
///         supposed to be suppressed in between.
/// </summary>
public sealed class KsZLevelTransitTest : KsZLevelTestBase
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsZLevelTestFaller
  name: test faller
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.35
        density: 100
        mask:
        - Impassable
        layer:
        - Impassable
  - type: Damageable
    damageContainer: Biological
  - type: Injurable
  - type: InputMover
  - type: MovementSpeedModifier
  - type: StandingState
  - type: Crawler
  - type: DoAfter
  - type: TestListener

# Solid, and on the same collision layer as the faller, so the two genuinely collide.
- type: entity
  id: KsZLevelTestVictim
  name: test victim
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.35
        hard: true
        mask:
        - Impassable
        layer:
        - Impassable
  - type: Damageable
    damageContainer: Biological
  - type: Injurable
  - type: StandingState
  - type: Crawler
  - type: DoAfter
  - type: InputMover
  - type: MovementSpeedModifier
  - type: StatusEffectContainer
  # StatusEffectStunned whitelists MobState, so without these the crush would damage but never stun.
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      1000: Critical
      2000: Dead
  - type: TestListener

# Solid, but on a layer the faller neither masks nor shares: the broadphase would never pair these two.
- type: entity
  parent: KsZLevelTestVictim
  id: KsZLevelTestUncollidableVictim
  components:
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.35
        hard: true
        mask:
        - GhostImpassable
        layer:
        - GhostImpassable

# Nothing hard about it, so it lands on nobody.
- type: entity
  parent: KsZLevelTestFaller
  id: KsZLevelTestSoftFaller
  components:
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.35
        hard: false
        mask:
        - Impassable
        layer:
        - Impassable
";

    private const string FallerProto = "KsZLevelTestFaller";
    private const string SoftFallerProto = "KsZLevelTestSoftFaller";
    private const string VictimProto = "KsZLevelTestVictim";
    private const string UncollidableVictimProto = "KsZLevelTestUncollidableVictim";

    /// <summary>
    ///     Drops <paramref name="fallerProto"/> onto a <paramref name="victimProto"/> standing where it lands,
    ///         and reports how much damage the victim took.
    /// </summary>
    private async Task<(EntityUid Victim, FixedPoint2 Damage)> DropOnto(string fallerProto, string victimProto)
    {
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();

        var faller = EntityUid.Invalid;
        var victim = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            // Directly under the hole, on the tile the faller comes to rest on.
            victim = entManager.SpawnEntity(victimProto, stack.UnderHoleCoords);

            faller = entManager.SpawnEntity(fallerProto, stack.HoleCoords);
            transitSystem.TryStartTransit(faller, -10f);
        });

        var (_, landed) = await RunUntilLanded(entManager, faller);
        await Pair.RunTicksSync(2);

        var damage = FixedPoint2.Zero;
        await server.WaitAssertion(() =>
        {
            Assert.That(landed, Is.True,
                "the faller has to reach the floor for anything to be crushed by it");
            damage = entManager.System<DamageableSystem>().GetTotalDamage(victim);
        });

        return (victim, damage);
    }

    /// <summary>
    ///     Runs single ticks until the entity has landed, and returns how long that took.
    /// </summary>
    private async Task<(int Ticks, bool Landed)> RunUntilLanded(IEntityManager entManager, EntityUid uid, int maxTicks = 300)
    {
        var ticks = 0;
        while (ticks < maxTicks && entManager.HasComponent<KsZLevelTransitComponent>(uid))
        {
            await Pair.RunTicksSync(1);
            ticks++;
        }

        return (ticks, !entManager.HasComponent<KsZLevelTransitComponent>(uid));
    }

    [Test]
    public async Task TestStandingOnFloorDoesNotTransit()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();

        var faller = EntityUid.Invalid;

        await server.WaitPost(() => faller = entManager.SpawnEntity(FallerProto, stack.SupportedCoords));
        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(transitSystem.TryStartTransit(faller), Is.False,
                "an entity standing on solid floor should have nothing to fall through");
            Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(faller), Is.False,
                "a refused TryStartTransit must not leave a transit component behind");
        });
    }

    [Test]
    public async Task TestWalkingOffTheFloorStartsTransit()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        var faller = EntityUid.Invalid;

        await server.WaitPost(() => faller = entManager.SpawnEntity(FallerProto, stack.SupportedCoords));
        await Pair.RunTicksSync(5);

        // Nothing calls TryStartTransit here: moving off the tile leaves the entity over nothing, grid
        //      traversal re-parents it to the map, and the transit system picks that up on its own.
        await server.WaitPost(() => transformSystem.SetWorldPosition(faller, new Vector2(2.5f, 0.5f)));
        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
            Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(faller), Is.True,
                "walking off the edge of a z-level should start a transit by itself"));
    }

    [Test]
    public async Task TestGravityAcceleratesTheFall()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();

        var faller = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            faller = entManager.SpawnEntity(FallerProto, stack.HoleCoords);
            transitSystem.TryStartTransit(faller);
        });

        await Pair.RunTicksSync(2);
        var earlyVelocity = 0f;
        await server.WaitAssertion(() =>
            earlyVelocity = entManager.GetComponent<KsZLevelTransitComponent>(faller).VerticalVelocity);

        await Pair.RunTicksSync(4);

        await server.WaitAssertion(() =>
        {
            var lateVelocity = entManager.GetComponent<KsZLevelTransitComponent>(faller).VerticalVelocity;

            Assert.That(earlyVelocity, Is.LessThan(0f),
                "gravity should have given the entity downward speed, which is negative");
            Assert.That(lateVelocity, Is.LessThan(earlyVelocity),
                "gravity has to keep applying every tick, not just on the tick the fall started");
        });
    }

    /// <summary>
    ///     Catches the failure mode where gravity applies on the z-level a fall starts on and then never again,
    ///         which reads in game as a fall that is merely very slow rather than broken.
    /// </summary>
    [Test]
    public async Task TestFallTakesTheTimeGravitySaysItShould()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var gameTiming = server.ResolveDependency<IGameTiming>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();

        var faller = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            faller = entManager.SpawnEntity(FallerProto, stack.HoleCoords);
            transitSystem.TryStartTransit(faller);
        });

        var (ticks, landed) = await RunUntilLanded(entManager, faller);
        var elapsed = ticks / (float)gameTiming.TickRate;

        // One z-level of Depth 1, from rest: t = sqrt(2 * 1 / 8) = 0.5s. Euler integration overshoots very
        //      slightly, so allow a couple of ticks either way.
        Assert.Multiple(() =>
        {
            Assert.That(landed, Is.True,
                "a one z-level fall should finish well inside the tick budget; still transiting means it is stuck or far too slow");
            Assert.That(elapsed, Is.EqualTo(0.5f).Within(0.1f),
                $"a one z-level fall under gravity {TestGravity} should take about half a second, took {elapsed:F2}s");
        });
    }

    [Test]
    public async Task TestCrossesAndLandsOnTheLevelBelow()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();
        var listenerSystem = entManager.System<KsZLevelTestListenerSystem>();

        listenerSystem.Reset();
        var faller = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            faller = entManager.SpawnEntity(FallerProto, stack.HoleCoords);
            transitSystem.TryStartTransit(faller);
        });

        var (_, landed) = await RunUntilLanded(entManager, faller);

        await server.WaitAssertion(() =>
        {
            var transformComponent = entManager.GetComponent<TransformComponent>(faller);

            Assert.Multiple(() =>
            {
                Assert.That(landed, Is.True,
                    "the fall should have finished; still transiting means nothing ever stopped it");
                Assert.That(transformComponent.MapID, Is.EqualTo(stack.LowerMapId),
                    "the entity should have ended up on the z-level below");
                Assert.That(listenerSystem.LevelChanges, Has.Count.EqualTo(1),
                    "crossing exactly one floor plane should raise exactly one level changed event");
                Assert.That(listenerSystem.LevelChanges[0].Rising, Is.False,
                    "the crossing was downwards, so the event should not report it as rising");
                Assert.That(listenerSystem.Landings, Has.Count.EqualTo(1),
                    "coming to rest once should raise exactly one land event, not one per tick spent on the floor");
                Assert.That(listenerSystem.TransitsStarted, Is.EqualTo(1),
                    "one fall is one transit; crossing a z-level must not start a second one");
                Assert.That(listenerSystem.TransitsEnded, Is.EqualTo(1),
                    "the transit should have ended exactly once, when the entity landed");
            });
        });
    }

    /// <summary>
    ///     Height is renormalised into 0..1 on every crossing, so no consumer ever sees it leave that range.
    /// </summary>
    [Test]
    public async Task TestHeightStaysNormalised()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();

        var faller = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            faller = entManager.SpawnEntity(FallerProto, stack.HoleCoords);
            transitSystem.TryStartTransit(faller);
        });

        for (var tick = 0; tick < 40; tick++)
        {
            await Pair.RunTicksSync(1);

            var stillFalling = true;
            await server.WaitAssertion(() =>
            {
                if (!entManager.TryGetComponent<KsZLevelTransitComponent>(faller, out var transitComponent))
                {
                    stillFalling = false;
                    return;
                }

                Assert.That(transitComponent.Height, Is.InRange(0f, 1f),
                    $"transit height escaped 0..1 (was {transitComponent.Height}), so a boundary crossing failed to renormalise it");
            });

            if (!stillFalling)
                break;
        }
    }

    [Test]
    public async Task TestWithoutGravityItCoastsButStillCrosses()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack(gravity: false);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();

        var faller = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            faller = entManager.SpawnEntity(FallerProto, stack.HoleCoords);
            transitSystem.TryStartTransit(faller, -2f);
        });

        await Pair.RunTicksSync(4);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsZLevelTransitComponent>(faller, out var transitComponent), Is.True,
                "momentum should still carry the entity between z-levels without gravity");
            Assert.That(transitComponent!.VerticalVelocity, Is.EqualTo(-2f).Within(0.0001f),
                "nothing should accelerate an entity on a z-level with no gravity");
        });

        var (_, landed) = await RunUntilLanded(entManager, faller);

        await server.WaitAssertion(() =>
        {
            Assert.That(landed, Is.True,
                "coasting downwards without gravity should still eventually reach a floor and stop");
            Assert.That(entManager.GetComponent<TransformComponent>(faller).MapID, Is.EqualTo(stack.LowerMapId),
                "coasting on momentum alone should still have carried it onto the z-level below");
        });
    }

    [Test]
    public async Task TestLandingHardEnoughDealsDamage()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();
        var listenerSystem = entManager.System<KsZLevelTestListenerSystem>();

        listenerSystem.Reset();
        var faller = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            faller = entManager.SpawnEntity(FallerProto, stack.HoleCoords);

            // Well over the impact threshold, so the landing is unambiguously a damaging one.
            transitSystem.TryStartTransit(faller, -10f);
        });

        var (_, landed) = await RunUntilLanded(entManager, faller);

        await server.WaitAssertion(() =>
        {
            Assert.That(landed, Is.True,
                "a fast fall should still come to rest on the floor below rather than falling forever");
            Assert.That(listenerSystem.Landings, Has.Count.EqualTo(1),
                "a hard landing should raise exactly one land event");
            Assert.That(listenerSystem.Landings[0].Damaged, Is.True,
                $"an impact well past {TestImpactVelocity} levels/s should be a damaging one");
            Assert.That(entManager.System<DamageableSystem>().GetTotalDamage(faller),
                Is.GreaterThan(FixedPoint2.Zero), "a damaging landing should actually deal damage");
        });
    }

    [Test]
    public async Task TestGentleLandingDealsNoDamage()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack(gravity: false);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();
        var listenerSystem = entManager.System<KsZLevelTestListenerSystem>();

        listenerSystem.Reset();
        var faller = EntityUid.Invalid;

        // No gravity, so it drifts down at a constant speed well under the impact threshold.
        await server.WaitPost(() =>
        {
            faller = entManager.SpawnEntity(FallerProto, stack.HoleCoords);
            transitSystem.TryStartTransit(faller, -1f);
        });

        var (_, landed) = await RunUntilLanded(entManager, faller);

        await server.WaitAssertion(() =>
        {
            Assert.That(landed, Is.True,
                "a slow drift should still reach the floor below, just later");
            Assert.That(listenerSystem.Landings, Has.Count.EqualTo(1),
                "a gentle landing is still a landing, so it should raise exactly one land event");
            Assert.That(listenerSystem.Landings[0].Damaged, Is.False,
                $"an impact under {TestImpactVelocity} levels/s should not be a damaging one");
            Assert.That(entManager.System<DamageableSystem>().GetTotalDamage(faller), Is.EqualTo(FixedPoint2.Zero),
                "an impact under the threshold should leave the entity completely unharmed");
        });
    }

    [Test]
    public async Task TestLandAttemptEventCanVetoDamage()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();
        var listenerSystem = entManager.System<KsZLevelTestListenerSystem>();

        listenerSystem.Reset();
        listenerSystem.VetoLandingDamage = true;

        var faller = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            faller = entManager.SpawnEntity(FallerProto, stack.HoleCoords);
            transitSystem.TryStartTransit(faller, -10f);
        });

        var (_, landed) = await RunUntilLanded(entManager, faller);

        await server.WaitAssertion(() =>
        {
            Assert.That(landed, Is.True,
                "a vetoed landing is still a landing, so the entity should have come to rest");
            Assert.That(listenerSystem.Landings, Has.Count.EqualTo(1),
                "vetoing the damage should not have suppressed the land event itself");
            Assert.That(listenerSystem.Landings[0].Damaged, Is.False,
                "the land attempt event should have talked the landing out of being damaging");
            Assert.That(entManager.System<DamageableSystem>().GetTotalDamage(faller),
                Is.EqualTo(FixedPoint2.Zero), "a vetoed landing should deal no damage at all");
        });

        listenerSystem.Reset();
    }

    /// <summary>
    ///     Rising is the same integration with the sign flipped, and a solid ceiling stops it without landing.
    ///     Under gravity the entity then falls back down and lands normally.
    /// </summary>
    [Test]
    public async Task TestRisingBumpsIntoTheCeilingAndFallsBack()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();
        var listenerSystem = entManager.System<KsZLevelTestListenerSystem>();

        listenerSystem.Reset();
        var riser = EntityUid.Invalid;

        // Under the upper z-level's one solid tile, launched hard enough to reach it.
        await server.WaitPost(() =>
        {
            riser = entManager.SpawnEntity(FallerProto, stack.UnderFloorCoords);
            transitSystem.TryStartTransit(riser, 8f);
        });

        var (_, landed) = await RunUntilLanded(entManager, riser);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(landed, Is.True,
                    "what goes up under gravity has to come back down and land");
                Assert.That(entManager.GetComponent<TransformComponent>(riser).MapID, Is.EqualTo(stack.LowerMapId),
                    "a solid ceiling should never have let the entity through to the z-level above");
                Assert.That(listenerSystem.LevelChanges, Is.Empty,
                    "the entity never left its own z-level, so no crossing should have been reported");
                Assert.That(listenerSystem.Landings, Has.Count.EqualTo(1),
                    "the ceiling bump is not a landing, so only the eventual floor impact should raise one");
            });
        });
    }

    /// <summary>
    ///     A ceiling bump with no gravity to pull the entity back down has to end the transit rather than leave
    ///         it hanging there, because a transiting entity is knocked down, cannot move itself and collides
    ///         with nothing - all of which would stick permanently.
    /// </summary>
    [Test]
    public async Task TestComingToRestWithoutGravityEndsTransit()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack(gravity: false);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();
        var actionBlockerSystem = entManager.System<ActionBlockerSystem>();
        var listenerSystem = entManager.System<KsZLevelTestListenerSystem>();

        listenerSystem.Reset();
        var riser = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            riser = entManager.SpawnEntity(FallerProto, stack.UnderFloorCoords);
            transitSystem.TryStartTransit(riser, 4f);
        });

        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(riser), Is.False,
                    "an entity with no speed left and no gravity to give it any should not stay in transit forever");
                Assert.That(entManager.GetComponent<TransformComponent>(riser).MapID, Is.EqualTo(stack.LowerMapId),
                    "a solid ceiling should not have let it through to the z-level above");
                Assert.That(listenerSystem.Landings, Is.Empty,
                    "stopping against a ceiling is not a landing, so it must not raise a land event");
                Assert.That(actionBlockerSystem.CanMove(riser), Is.True,
                    "ending the transit has to give movement back, or the entity is frozen for good");
                Assert.That(entManager.HasComponent<KsPendingZLevelTransitComponent>(riser), Is.True,
                    "the pending marker is what starts it falling again if gravity ever returns");
            });
        });
    }

    [Test]
    public async Task TestRisingThroughAHoleCrossesUpwards()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack(gravity: false);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();
        var listenerSystem = entManager.System<KsZLevelTestListenerSystem>();

        listenerSystem.Reset();
        var riser = EntityUid.Invalid;

        // Under the hole this time, so there is nothing overhead to stop it.
        await server.WaitPost(() =>
        {
            riser = entManager.SpawnEntity(FallerProto, stack.UnderHoleCoords);
            transitSystem.TryStartTransit(riser, 4f);
        });

        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entManager.GetComponent<TransformComponent>(riser).MapID, Is.EqualTo(stack.UpperMapId),
                    "an open ceiling should have let the entity rise through to the z-level above");
                Assert.That(listenerSystem.LevelChanges, Has.Count.EqualTo(1),
                    "rising through one open ceiling should report exactly one z-level change");
                Assert.That(listenerSystem.LevelChanges[0].Rising, Is.True,
                    "the crossing was upwards, so the event should report it as rising");
            });
        });
    }

    [Test]
    public async Task TestTransitBlocksSelfMovement()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack(gravity: false);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();
        var actionBlockerSystem = entManager.System<ActionBlockerSystem>();
        var listenerSystem = entManager.System<KsZLevelTestListenerSystem>();

        listenerSystem.Reset();
        var faller = EntityUid.Invalid;

        await server.WaitPost(() => faller = entManager.SpawnEntity(FallerProto, stack.SupportedCoords));
        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
            Assert.That(actionBlockerSystem.CanMove(faller), Is.True,
                "the entity should move freely while it is still standing on solid floor"));

        await server.WaitPost(() =>
        {
            entManager.System<SharedTransformSystem>().SetCoordinates(faller, stack.HoleCoords);
            transitSystem.TryStartTransit(faller, -0.5f);
        });
        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
            Assert.That(actionBlockerSystem.CanMove(faller), Is.False,
                "an entity in transit is in mid-air and should not be able to walk itself around"));

        var (_, landed) = await RunUntilLanded(entManager, faller);

        await server.WaitAssertion(() =>
        {
            Assert.That(landed, Is.True,
                "the entity has to land for the movement block to be lifted again");
            Assert.That(actionBlockerSystem.CanMove(faller), Is.True,
                "the movement block has to be lifted on landing, or the entity is stuck for good");
        });
    }

    [Test]
    public async Task TestMoveAttemptEventCanAllowMovement()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack(gravity: false);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();
        var actionBlockerSystem = entManager.System<ActionBlockerSystem>();
        var listenerSystem = entManager.System<KsZLevelTestListenerSystem>();

        listenerSystem.Reset();
        listenerSystem.AllowMovementInTransit = true;

        var faller = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            faller = entManager.SpawnEntity(FallerProto, stack.HoleCoords);
            transitSystem.TryStartTransit(faller, -0.5f);
        });
        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(faller), Is.True,
                "the entity has to actually be transiting for the override to be worth anything");
            Assert.That(actionBlockerSystem.CanMove(faller), Is.True,
                "the move attempt event should have overridden the transit movement block");
        });

        listenerSystem.Reset();
    }

    [Test]
    public async Task TestTransitPreventsCollisions()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack(gravity: false);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();

        var transformSystem = entManager.System<SharedTransformSystem>();

        var faller = EntityUid.Invalid;
        var other = EntityUid.Invalid;

        // Both on solid floor to start with, so neither is transiting yet.
        await server.WaitPost(() =>
        {
            faller = entManager.SpawnEntity(FallerProto, stack.SupportedCoords);
            other = entManager.SpawnEntity(FallerProto, stack.SupportedCoords);
        });
        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(faller), Is.False,
                "both entities start on solid floor, so neither should have begun transiting yet");
            Assert.That(RaisePreventCollide(entManager, faller, other), Is.False,
                "two entities that are not in transit should be free to collide");
        });

        await server.WaitPost(() =>
        {
            transformSystem.SetWorldPosition(faller, new Vector2(2.5f, 0.5f));
            transitSystem.TryStartTransit(faller, -1f);
        });
        await Pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(faller), Is.True,
                "the entity has to be transiting for the collision veto to mean anything");
            Assert.That(RaisePreventCollide(entManager, faller, other), Is.True,
                "an entity in transit is between floors and should collide with nothing");
        });
    }

    /// <summary>
    ///     Falling puts an entity on the floor and keeps it there for the whole descent, however long that is,
    ///         and lets it get back up once it has landed.
    /// </summary>
    [Test]
    public async Task TestTransitKnocksDownAndBlocksStandingUp()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack(gravity: false);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();
        var stunSystem = entManager.System<SharedStunSystem>();

        var faller = EntityUid.Invalid;

        await server.WaitPost(() => faller = entManager.SpawnEntity(FallerProto, stack.SupportedCoords));
        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
            Assert.That(entManager.HasComponent<KnockedDownComponent>(faller), Is.False,
                "an entity standing on solid floor should be on its feet before anything pushes it off"));

        // Barely moving, so the descent lasts far longer than the landing knockdown would on its own.
        await server.WaitPost(() =>
        {
            entManager.System<SharedTransformSystem>().SetWorldPosition(faller, new Vector2(2.5f, 0.5f));
            transitSystem.TryStartTransit(faller, -0.25f);
        });
        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(faller), Is.True,
                "the entity has to be transiting for the rest of this test to mean anything");
            Assert.That(entManager.HasComponent<KnockedDownComponent>(faller), Is.True,
                "starting to fall should have taken the entity off its feet");
        });

        // Well past the landing knockdown duration, but still mid-fall.
        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(faller), Is.True,
                "the fall should still be going, or this proves nothing about staying down mid-air");
            Assert.That(entManager.TryGetComponent<KnockedDownComponent>(faller, out var knockedDownComponent), Is.True,
                "the entity should still be on the floor after the landing knockdown would have run out, because it has not landed");

            Assert.That(stunSystem.TryStand((faller, knockedDownComponent!)), Is.False,
                "nothing should be able to get to its feet while it is still falling");
        });

        var (_, landed) = await RunUntilLanded(entManager, faller);
        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(landed, Is.True,
                "the entity has to land for the knockdown to be allowed to lapse");

            // Whether it is already back up, or merely free to start getting up, is down to crawl timings and
            //      do-afters that are none of this system's business. What matters is that it is no longer barred.
            var freeToGetUp = !entManager.TryGetComponent<KnockedDownComponent>(faller, out var knockedDownComponent)
                              || stunSystem.TryStand((faller, knockedDownComponent!));

            Assert.That(freeToGetUp, Is.True,
                "once landed, the entity should be free to get back up rather than being pinned to the floor");
        });
    }


    /// <summary>
    ///     Something solid coming down hard enough damages and stuns whatever it lands on top of.
    /// </summary>
    [Test]
    public async Task TestLandingOnSomethingCrushesIt()
    {
        await OverrideTransitCVars();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var listenerSystem = entManager.System<KsZLevelTestListenerSystem>();

        listenerSystem.Reset();

        var (victim, damage) = await DropOnto(FallerProto, VictimProto);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(damage, Is.GreaterThan(FixedPoint2.Zero),
                    "having something solid dropped on you should hurt");
                Assert.That(entManager.HasComponent<StunnedComponent>(victim), Is.True,
                    "being landed on should stun, not just damage");
            });
        });
    }

    /// <summary>
    ///     The crush is filtered by the same fixture rules the broadphase uses, so a faller does not damage
    ///         things it could never have touched on the way down.
    /// </summary>
    [Test]
    public async Task TestLandingDoesNotCrushWhatItCannotCollideWith()
    {
        await OverrideTransitCVars();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        var (victim, damage) = await DropOnto(FallerProto, UncollidableVictimProto);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(damage, Is.EqualTo(FixedPoint2.Zero),
                    "the faller and this victim share no collision layer or mask, so landing on it should do nothing at all");
                Assert.That(entManager.HasComponent<StunnedComponent>(victim), Is.False,
                    "something the faller cannot collide with should not be stunned by it either");
            });
        });
    }

    /// <summary>
    ///     Only hard fixtures land on anything. A faller made entirely of sensors passes through.
    /// </summary>
    [Test]
    public async Task TestSoftFallerCrushesNothing()
    {
        await OverrideTransitCVars();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        var (victim, damage) = await DropOnto(SoftFallerProto, VictimProto);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(damage, Is.EqualTo(FixedPoint2.Zero),
                    "a faller with no hard fixtures has nothing to land on anyone with");
                Assert.That(entManager.HasComponent<StunnedComponent>(victim), Is.False,
                    "a faller with no hard fixtures should not stun what it passes through either");
            });
        });
    }

    /// <summary>
    ///     A gentle landing is not a crush: under the impact threshold nothing underneath is touched.
    /// </summary>
    [Test]
    public async Task TestGentleLandingCrushesNothing()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack(gravity: false);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();

        var faller = EntityUid.Invalid;
        var victim = EntityUid.Invalid;

        // No gravity, so it drifts down at a constant speed well under the impact threshold.
        await server.WaitPost(() =>
        {
            victim = entManager.SpawnEntity(VictimProto, stack.UnderHoleCoords);
            faller = entManager.SpawnEntity(FallerProto, stack.HoleCoords);
            transitSystem.TryStartTransit(faller, -1f);
        });

        var (_, landed) = await RunUntilLanded(entManager, faller);
        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(landed, Is.True,
                "the faller has to reach the floor for this to say anything about crushing");
            Assert.That(entManager.System<DamageableSystem>().GetTotalDamage(victim), Is.EqualTo(FixedPoint2.Zero),
                "drifting gently onto something should not hurt it");
        });
    }


    /// <summary>
    ///     Placing a tile in open space spawns a grid, and the parent change that comes with it used to start
    ///         the grid itself falling - dragging the knockdown, the collision veto and the landing crush along
    ///         with it, the last of which would run against the grid's own world AABB.
    /// </summary>
    [Test]
    public async Task TestGridsAndMapsNeverTransit()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();

        var placedGrid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            // Exactly what PlacementManager.PlaceNewTile does for a tile placed off-grid: a fresh, empty grid,
            //      moved into open space. Empty, so it does not even read as standing on solid floor.
            var grid = mapSystem.CreateGridEntity(stack.UpperMapId);
            placedGrid = grid.Owner;
            transformSystem.SetWorldPosition(placedGrid, new Vector2(2.5f, 0.5f));
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(placedGrid), Is.False,
                    "a grid is part of the z-level stack, not something that falls through it");
                Assert.That(transitSystem.TryStartTransit(placedGrid), Is.False,
                    "even asked outright, a grid should refuse to start transiting");
                Assert.That(transitSystem.TryStartTransit(stack.UpperMapUid), Is.False,
                    "a z-level itself is even less of a thing that falls");
                Assert.That(entManager.GetComponent<TransformComponent>(placedGrid).MapID,
                    Is.EqualTo(stack.UpperMapId),
                    "the grid should still be on the z-level it was created on");
            });
        });
    }


    /// <summary>
    ///     Editing a lot of tiles at once is the ordinary way to split a grid, and a split creates grid entities
    ///         and reparents everything off the old grid - all of it raising EntParentChangedMessage from deep
    ///         inside GridFixtureSystem, with its own iteration and the broadphase still mid-flight.
    ///     Starting a transit synchronously from there moved entities to another map underneath it, and took the
    ///         server down outright rather than throwing anything catchable.
    /// </summary>
    [Test]
    public async Task TestSplittingAZLevelGridDoesNotReenter()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();

        var tile = new Tile(tileDefinitionManager["Plating"].TileId);
        var bridgeGrid = default(Entity<MapGridComponent>);
        var rider = EntityUid.Invalid;

        // A dumbbell: two blobs joined by a single tile, with something standing on one end.
        await server.WaitPost(() =>
        {
            bridgeGrid = mapSystem.CreateGridEntity(stack.UpperMapId);

            for (var x = 0; x <= 4; x++)
                mapSystem.SetTile(bridgeGrid.Owner, bridgeGrid.Comp, new Vector2i(x, 0), tile);

            rider = entManager.SpawnEntity(FallerProto, new EntityCoordinates(bridgeGrid.Owner, 0.5f, 0.5f));
        });

        await Pair.RunTicksSync(10);

        // Knock out the middle, which disconnects the two ends and forces a split.
        await server.WaitPost(() =>
            mapSystem.SetTile(bridgeGrid.Owner, bridgeGrid.Comp!, new Vector2i(2, 0), Tile.Empty));

        await Pair.RunTicksSync(15);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entManager.EntityExists(rider), Is.True,
                    "splitting the grid under it should not have destroyed the entity standing on it");
                Assert.That(entManager.GetComponent<TransformComponent>(rider).MapID, Is.EqualTo(stack.UpperMapId),
                    "an entity still standing on solid floor must not be moved to another z-level by a split");
                Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(rider), Is.False,
                    "the entity never left the floor, so nothing should have started it falling");
            });
        });
    }

    /// <summary>
    ///     Asks the same question the broadphase does when it is deciding whether to build a contact.
    /// </summary>
    private static bool RaisePreventCollide(IEntityManager entManager, EntityUid uid, EntityUid other)
    {
        var fixturesSystem = entManager.System<Robust.Shared.Physics.Systems.FixtureSystem>();
        var ourBody = entManager.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(uid);
        var otherBody = entManager.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(other);
        var ourFixture = entManager.GetComponent<Robust.Shared.Physics.FixturesComponent>(uid).Fixtures.Values.First();
        var otherFixture = entManager.GetComponent<Robust.Shared.Physics.FixturesComponent>(other).Fixtures.Values.First();

        var preventCollideEvent = new Robust.Shared.Physics.Events.PreventCollideEvent(
            uid, other, ourBody, otherBody, ourFixture, otherFixture);
        entManager.EventBus.RaiseLocalEvent(uid, ref preventCollideEvent);

        return preventCollideEvent.Cancelled;
    }

    /// <summary>
    ///     Being picked up mid-fall ends the transit, or the next crossing would teleport the entity straight
    ///         out of whatever is holding it.
    /// </summary>
    [Test]
    public async Task TestBeingContainedEndsTransit()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack(gravity: false);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();
        var containerSystem = entManager.System<SharedContainerSystem>();

        var faller = EntityUid.Invalid;
        var holder = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            faller = entManager.SpawnEntity(FallerProto, stack.HoleCoords);
            holder = entManager.SpawnEntity(FallerProto, stack.SupportedCoords);
            transitSystem.TryStartTransit(faller, -0.5f);
        });
        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
            Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(faller), Is.True,
                "the entity should be mid-transit before anything picks it up"));

        await server.WaitPost(() =>
        {
            var container = containerSystem.EnsureContainer<Container>(holder, "test-container");
            containerSystem.Insert(faller, container, force: true);
        });
        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
            Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(faller), Is.False,
                "being put in a container should end the transit"));
    }
}
