#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC.Components;
using Content.Shared._KS14.LaserSight;
using Content.Shared._KS14.LockOn;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Turret behaviour of ranged NPCs: the lock-on that must finish before firing, the grace for losing sight of the
///         lock target, and aim tracking that keeps a slow-turning shooter on a target circling it.
/// </summary>
public sealed class KsNpcTurretTrackingTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string LockShooter = "KsTurretTestLockShooter";
    private const string TrackingShooter = "KsTurretTestTrackingShooter";
    private const string PlainShooter = "KsTurretTestPlainShooter";

    // The tracking and plain shooters hold a lock that never completes, so they aim but never fire; firing would
    // run them out of ammunition, which ends the combat update.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsTurretTestBaseShooter
  abstract: true
  components:
  - type: Hands
  - type: CombatMode

- type: entity
  parent: KsTurretTestBaseShooter
  id: KsTurretTestLockShooter
  components:
  - type: KsTargetLockOn
    lockOnTime: 3
    loseLockGrace: 0.5

- type: entity
  parent: KsTurretTestBaseShooter
  id: KsTurretTestPlainShooter
  components:
  - type: KsTargetLockOn
    lockOnTime: 1000

- type: entity
  parent: KsTurretTestPlainShooter
  id: KsTurretTestTrackingShooter
  components:
  - type: KsNpcAimTracking
    angularVelocityCompensation: 1

- type: entity
  parent: KsTurretTestPlainShooter
  id: KsTurretTestLaserShooter
  components:
  - type: KsLaserSight
    onlyWhileTargeting: true
";

    /// <summary>
    ///     A shooter fires only once its lock has completed.
    /// </summary>
    [Test]
    public async Task TestLockOnGatesFiring()
    {
        var (entManager, gridUid) = await SetUpOpenGrid();
        var lockSystem = entManager.System<SharedKsTargetLockOnSystem>();
        EntityUid shooterUid = default;
        EntityUid targetUid = default;
        EntityUid gunUid = default;

        await Pair.Server.WaitPost(() =>
        {
            (shooterUid, targetUid, gunUid) = SpawnArmedShooter(entManager, gridUid, LockShooter);
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(targetUid);
            entManager.EnsureComponent<NPCRangedCombatComponent>(shooterUid).Target = targetUid;
        });

        var startAmmo = AmmoCount(entManager, gunUid);
        Assert.That(startAmmo, Is.GreaterThan(0));

        // An unlocked shooter would have opened fire well within this.
        await RunSeconds(2.5f);
        await Pair.Server.WaitPost(() =>
        {
            Assert.That(lockSystem.IsLocked(shooterUid, targetUid), Is.False, "the lock should not have completed");
            Assert.That(entManager.GetComponent<KsTargetLockOnComponent>(shooterUid).Target, Is.EqualTo(targetUid), "it should be locking on");
            Assert.That(AmmoCount(entManager, gunUid), Is.EqualTo(startAmmo), "it fired before the lock completed");
        });

        await RunSeconds(1f);
        await Pair.Server.WaitPost(() =>
        {
            Assert.That(lockSystem.IsLocked(shooterUid, targetUid), Is.True, "the lock should have completed");
            Assert.That(AmmoCount(entManager, gunUid), Is.LessThan(startAmmo), "it should fire once locked");
        });
    }

    /// <summary>
    ///     Losing sight of the target for longer than the grace drops the lock; a short loss keeps it, and regaining
    ///         sight starts counting again.
    /// </summary>
    [Test]
    public async Task TestLockDroppedAfterGrace()
    {
        var (entManager, gridUid) = await SetUpOpenGrid();
        var lockSystem = entManager.System<SharedKsTargetLockOnSystem>();
        EntityUid shooterUid = default;
        EntityUid targetUid = default;

        void UpdateLock(bool visible)
        {
            lockSystem.UpdateLock((shooterUid, entManager.GetComponent<KsTargetLockOnComponent>(shooterUid)), targetUid, visible);
        }

        await Pair.Server.WaitPost(() =>
        {
            shooterUid = SpawnAt(entManager, LockShooter, gridUid, 0, 0);
            targetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 5, 0);
            UpdateLock(true);
            Assert.That(entManager.GetComponent<KsTargetLockOnComponent>(shooterUid).Target, Is.EqualTo(targetUid));
        });

        // Lost for 0.3s of a 0.5s grace: still held.
        await Pair.Server.WaitPost(() => UpdateLock(false));
        await RunSeconds(0.3f);
        await Pair.Server.WaitPost(() =>
        {
            UpdateLock(false);
            Assert.That(entManager.GetComponent<KsTargetLockOnComponent>(shooterUid).Target, Is.EqualTo(targetUid), "within the grace");
            UpdateLock(true);
        });

        // Seen again, then lost for longer than the grace: dropped.
        await Pair.Server.WaitPost(() => UpdateLock(false));
        await RunSeconds(0.8f);
        await Pair.Server.WaitPost(() =>
        {
            UpdateLock(false);
            Assert.That(entManager.GetComponent<KsTargetLockOnComponent>(shooterUid).Target, Is.Null, "past the grace");
            Assert.That(lockSystem.IsLocked(shooterUid, targetUid), Is.False);
        });
    }

    /// <summary>
    ///     A shooter turning at 1 rad/s stays on a target circling it at 2 rad/s when it tracks, and falls behind when
    ///         it does not.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestAimTrackingKeepsUp(bool tracking)
    {
        var (entManager, gridUid) = await SetUpOpenGrid();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var tickRate = (float)Pair.Server.ResolveDependency<IGameTiming>().TickRate;
        var threshold = Angle.FromDegrees(3);
        const float radius = 4f;
        const float angularSpeed = 2f;
        EntityUid shooterUid = default;
        EntityUid targetUid = default;
        var theta = 0f;

        await Pair.Server.WaitPost(() =>
        {
            (shooterUid, targetUid, _) = SpawnArmedShooter(entManager, gridUid, tracking ? TrackingShooter : PlainShooter);
            var rangedComponent = entManager.EnsureComponent<NPCRangedCombatComponent>(shooterUid);
            rangedComponent.Target = targetUid;
            rangedComponent.RotationSpeed = new Angle(1d); // 1 rad/s
            rangedComponent.AccuracyThreshold = threshold;
        });

        var maxError = 0d;
        var settleTicks = (int)(2f * tickRate);
        var catchUpTicks = (int)(3f * tickRate);
        var measureTicks = (int)(4f * tickRate);

        for (var i = 0; i < settleTicks + catchUpTicks + measureTicks; i++)
        {
            if (i >= settleTicks)
                theta += angularSpeed / tickRate;

            await Pair.Server.WaitPost(() =>
            {
                var shooterPos = transformSystem.GetWorldPosition(shooterUid);
                transformSystem.SetWorldPosition(targetUid, shooterPos + new Vector2(MathF.Cos(theta), MathF.Sin(theta)) * radius);
            });
            await Pair.RunTicksSync(1);

            if (i < settleTicks + catchUpTicks)
                continue;

            await Pair.Server.WaitPost(() =>
            {
                var offset = transformSystem.GetWorldPosition(targetUid) - transformSystem.GetWorldPosition(shooterUid);
                var error = Angle.ShortestDistance(transformSystem.GetWorldRotation(shooterUid), offset.ToWorldAngle()).Theta;
                maxError = Math.Max(maxError, Math.Abs(error));
            });
        }

        if (tracking)
            Assert.That(maxError, Is.LessThanOrEqualTo(threshold.Theta), "a tracking shooter should stay on target");
        else
            Assert.That(maxError, Is.GreaterThan(threshold.Theta * 3), "a shooter without tracking should fall behind");
    }

    /// <summary>
    ///     An <c>onlyWhileTargeting</c> laser is dark when idle (even though <c>enabled</c> defaults to true), lit while
    ///         a target is in sight, and dark with the lock cleared once the fight ends, whether the target vanishes or
    ///         the combat component is removed.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestLaserLifecycle(bool deleteTarget)
    {
        var (entManager, gridUid) = await SetUpOpenGrid();
        EntityUid shooterUid = default;
        EntityUid targetUid = default;

        await Pair.Server.WaitPost(() =>
        {
            (shooterUid, targetUid, _) = SpawnArmedShooter(entManager, gridUid, "KsTurretTestLaserShooter");
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(targetUid);
        });

        await RunSeconds(0.2f);
        await Pair.Server.WaitPost(() =>
        {
            Assert.That(entManager.GetComponent<KsLaserSightComponent>(shooterUid).Enabled, Is.False, "idle laser must be dark");
            entManager.EnsureComponent<NPCRangedCombatComponent>(shooterUid).Target = targetUid;
        });

        await RunSeconds(0.5f);
        await Pair.Server.WaitPost(() =>
        {
            Assert.That(entManager.GetComponent<KsLaserSightComponent>(shooterUid).Enabled, Is.True, "laser should be on with a target in sight");
            Assert.That(entManager.GetComponent<KsTargetLockOnComponent>(shooterUid).Target, Is.EqualTo(targetUid));

            if (deleteTarget)
                entManager.DeleteEntity(targetUid);
            else
                entManager.RemoveComponent<NPCRangedCombatComponent>(shooterUid);
        });

        await RunSeconds(0.3f);
        await Pair.Server.WaitPost(() =>
        {
            Assert.That(entManager.GetComponent<KsLaserSightComponent>(shooterUid).Enabled, Is.False, "laser should go dark when the fight ends");
            Assert.That(entManager.GetComponent<KsTargetLockOnComponent>(shooterUid).Target, Is.Null, "the lock should be cleared");
        });
    }

    private (EntityUid Shooter, EntityUid Target, EntityUid Gun) SpawnArmedShooter(IEntityManager entManager, EntityUid gridUid, string prototype)
    {
        var shooterUid = SpawnAt(entManager, prototype, gridUid, 0, 0);
        var targetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 5, 0);

        var handsSystem = entManager.System<SharedHandsSystem>();
        handsSystem.AddHand(shooterUid, "right", HandLocation.Right);
        var gunUid = entManager.SpawnEntity("WeaponLaserCarbine", entManager.GetComponent<TransformComponent>(shooterUid).Coordinates);
        Assert.That(handsSystem.TryPickup(shooterUid, gunUid, "right"));

        return (shooterUid, targetUid, gunUid);
    }

    private static int AmmoCount(IEntityManager entManager, EntityUid gunUid)
    {
        var ev = new GetAmmoCountEvent();
        entManager.EventBus.RaiseLocalEvent(gunUid, ref ev);
        return ev.Count;
    }

    private async Task<(IEntityManager EntManager, EntityUid GridUid)> SetUpOpenGrid()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-5, -5), new Vector2i(12, 5)).Owner;
        });

        return (entManager, gridUid);
    }
}
