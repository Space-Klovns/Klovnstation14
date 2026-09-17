#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Helpers;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage.Systems;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Physics;
using Content.Shared.FixedPoint;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
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
  - type: TestListener
";

    private const string FallerProto = "KsZLevelTestFaller";

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
            Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(faller), Is.False);
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

            Assert.That(earlyVelocity, Is.LessThan(0f), "gravity should pull downwards, which is negative");
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
            Assert.That(landed, Is.True, "the entity never landed");
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
                Assert.That(landed, Is.True, "the entity never landed");
                Assert.That(transformComponent.MapID, Is.EqualTo(stack.LowerMapId),
                    "the entity should have ended up on the z-level below");
                Assert.That(listenerSystem.LevelChanges, Has.Count.EqualTo(1),
                    "crossing exactly one floor plane should raise exactly one level changed event");
                Assert.That(listenerSystem.LevelChanges[0].Rising, Is.False);
                Assert.That(listenerSystem.Landings, Has.Count.EqualTo(1));
                Assert.That(listenerSystem.TransitsStarted, Is.EqualTo(1));
                Assert.That(listenerSystem.TransitsEnded, Is.EqualTo(1));
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
                    "transit height escaped 0..1, so the boundary wrap did not renormalise it");
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
            Assert.That(landed, Is.True, "coasting downwards should still eventually reach a floor");
            Assert.That(entManager.GetComponent<TransformComponent>(faller).MapID, Is.EqualTo(stack.LowerMapId));
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
            Assert.That(landed, Is.True, "the entity never landed");
            Assert.That(listenerSystem.Landings, Has.Count.EqualTo(1));
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
            Assert.That(landed, Is.True, "the entity never landed");
            Assert.That(listenerSystem.Landings, Has.Count.EqualTo(1));
            Assert.That(listenerSystem.Landings[0].Damaged, Is.False,
                $"an impact under {TestImpactVelocity} levels/s should not be a damaging one");
            Assert.That(entManager.System<DamageableSystem>().GetTotalDamage(faller),
                Is.EqualTo(FixedPoint2.Zero));
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
            Assert.That(landed, Is.True, "the entity never landed");
            Assert.That(listenerSystem.Landings, Has.Count.EqualTo(1));
            Assert.That(listenerSystem.Landings[0].Damaged, Is.False,
                "the land attempt event should have talked the landing out of being damaging");
            Assert.That(entManager.System<DamageableSystem>().GetTotalDamage(faller),
                Is.EqualTo(FixedPoint2.Zero), "a vetoed landing should deal no damage at all");
        });

        listenerSystem.Reset();
    }

    /// <summary>
    ///     Rising is the same integration with the sign flipped, and a solid ceiling stops it without landing:
    ///         the entity is at the top of its z-level, not on the floor of it, so it stays in transit.
    /// </summary>
    [Test]
    public async Task TestRisingBumpsIntoTheCeiling()
    {
        await OverrideTransitCVars();
        var stack = await CreateStack(gravity: false);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transitSystem = entManager.System<KsZLevelPhysicsSystem>();
        var listenerSystem = entManager.System<KsZLevelTestListenerSystem>();

        listenerSystem.Reset();
        var riser = EntityUid.Invalid;

        // Under the upper z-level's one solid tile, heading up into it.
        await server.WaitPost(() =>
        {
            riser = entManager.SpawnEntity(FallerProto, stack.UnderFloorCoords);
            transitSystem.TryStartTransit(riser, 4f);
        });

        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsZLevelTransitComponent>(riser, out var transitComponent), Is.True,
                "bumping a ceiling is not landing, so the entity should still be in transit");

            Assert.Multiple(() =>
            {
                Assert.That(transitComponent!.Height, Is.EqualTo(1f).Within(0.0001f),
                    "an entity stopped by a ceiling should be resting against the top of its z-level");
                Assert.That(transitComponent.VerticalVelocity, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(entManager.GetComponent<TransformComponent>(riser).MapID, Is.EqualTo(stack.LowerMapId),
                    "a solid ceiling should not have let it through");
                Assert.That(listenerSystem.Landings, Is.Empty, "a ceiling bump should not raise a landing");
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
                Assert.That(listenerSystem.LevelChanges, Has.Count.EqualTo(1));
                Assert.That(listenerSystem.LevelChanges[0].Rising, Is.True);
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
            Assert.That(actionBlockerSystem.CanMove(faller), Is.True, "should be able to move before falling"));

        await server.WaitPost(() =>
        {
            entManager.System<SharedTransformSystem>().SetCoordinates(faller, stack.HoleCoords);
            transitSystem.TryStartTransit(faller, -0.5f);
        });
        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
            Assert.That(actionBlockerSystem.CanMove(faller), Is.False, "an entity in transit should not move itself"));

        var (_, landed) = await RunUntilLanded(entManager, faller);

        await server.WaitAssertion(() =>
        {
            Assert.That(landed, Is.True, "the entity never landed");
            Assert.That(actionBlockerSystem.CanMove(faller), Is.True, "movement should come back on landing");
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
            Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(faller), Is.True);
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
            Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(faller), Is.False);
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
            Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(faller), Is.True);
            Assert.That(RaisePreventCollide(entManager, faller, other), Is.True,
                "an entity in transit is between floors and should collide with nothing");
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
            Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(faller), Is.True));

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
