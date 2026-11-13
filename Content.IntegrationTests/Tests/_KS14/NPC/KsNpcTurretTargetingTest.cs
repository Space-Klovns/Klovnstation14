#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.LockOn;
using Content.Shared.Damage.Systems;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Which target a planning turret keeps (KsTargetLockOn.KeepTargetUntilLost) and whether it fires past a friend
///         (KsNpcAimTracking.IgnoreFriendlyBlocking).
/// </summary>
public sealed class KsNpcTurretTargetingTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string StickyTurret = "KsTurretTargetingTestSticky";
    private const string FickleTurret = "KsTurretTargetingTestFickle";
    private const string FireThroughTurret = "KsTurretTargetingTestFireThrough";
    private const string HoldingTurret = "KsTurretTargetingTestHolding";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: WeaponTurretSyndicate
  id: KsTurretTargetingTestFickle
  components:
  - type: KsTargetLockOn
    lockOnTime: 2

- type: entity
  parent: KsTurretTargetingTestFickle
  id: KsTurretTargetingTestSticky
  components:
  - type: KsTargetLockOn
    keepTargetUntilLost: true

- type: entity
  parent: WeaponTurretSyndicate
  id: KsTurretTargetingTestHolding
  components:
  - type: KsNpcAimTracking

- type: entity
  parent: KsTurretTargetingTestHolding
  id: KsTurretTargetingTestFireThrough
  components:
  - type: KsNpcAimTracking
    ignoreFriendlyBlocking: true
";

    /// <summary>
    ///     Two hostiles take turns at being the closest. A turret that keeps its target finishes its lock on the first
    ///         and holds it while it is in sight, and moves to the other only once a wall hides it. Without the switch, the
    ///         control, the lock keeps changing hands and never finishes.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestKeepTargetUntilLost(bool keepTarget)
    {
        var (entManager, gridUid) = await SetUpOpenGrid();
        var lockSystem = entManager.System<SharedKsTargetLockOnSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var tickRate = (float)Pair.Server.ResolveDependency<IGameTiming>().TickRate;
        EntityUid turretUid = default, firstUid = default, secondUid = default;
        var origin = Vector2.Zero;

        await Pair.Server.WaitPost(() =>
        {
            turretUid = SpawnAt(entManager, keepTarget ? StickyTurret : FickleTurret, gridUid, 0, 0);
            firstUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 3, 0);
            secondUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 0, -3);
            var godmodeSystem = entManager.System<SharedGodmodeSystem>();
            godmodeSystem.EnableGodmode(firstUid);
            godmodeSystem.EnableGodmode(secondUid);
            origin = transformSystem.GetWorldPosition(turretUid);
        });

        // They swap places every half second, so each is the closest in turn.
        var targets = new List<EntityUid>();
        var finishedFor = default(EntityUid);
        var swapTicks = (int)(0.5f * tickRate);

        for (var tick = 0; tick < (int)(6f * tickRate); tick++)
        {
            var firstNear = tick / swapTicks % 2 == 0;

            await Pair.Server.WaitPost(() =>
            {
                transformSystem.SetWorldPosition(firstUid, origin + new Vector2(firstNear ? 3f : 4.5f, 0f));
                transformSystem.SetWorldPosition(secondUid, origin + new Vector2(0f, firstNear ? -4.5f : -3f));

                var lockOn = entManager.GetComponent<KsTargetLockOnComponent>(turretUid);
                if (lockOn.Target is { } lockTarget)
                {
                    if (targets.Count == 0 || targets[^1] != lockTarget)
                        targets.Add(lockTarget);

                    if (lockSystem.IsLocked(turretUid, lockTarget))
                        finishedFor = lockTarget;
                }
            });
            await Pair.RunTicksSync(1);
        }

        if (!keepTarget)
        {
            Assert.That(targets, Has.Count.GreaterThan(2), "control: the lock should have been changing hands");
            Assert.That(finishedFor, Is.EqualTo(default(EntityUid)), "control: no lock should have finished");
            return;
        }

        Assert.That(targets, Has.Count.EqualTo(1), "a sticky turret should lock one target and keep it");
        Assert.That(finishedFor, Is.EqualTo(targets[0]), "its lock should have finished");

        // The kept target is walled off: the turret turns to the other.
        var keptUid = targets[0];
        await Pair.Server.WaitPost(() =>
        {
            var keptPosition = transformSystem.GetWorldPosition(keptUid) - origin;
            var wallPosition = origin + keptPosition.Normalized() * 1.5f;
            entManager.SpawnEntity(TestWall, new EntityCoordinates(gridUid, wallPosition));
        });

        await RunSeconds(1.5f);

        await Pair.Server.WaitPost(() =>
        {
            var lockOn = entManager.GetComponent<KsTargetLockOnComponent>(turretUid);
            Assert.That(lockOn.Target, Is.EqualTo(keptUid == firstUid ? secondUid : firstUid), "once the first is out of sight it should take the other");
        });
    }

    /// <summary>
    ///     A hostile at the end of a line with a friend of the turret's in the middle of it. Only a turret that ignores
    ///         friendly blocking fires; the control holds fire.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestIgnoreFriendlyBlocking(bool ignore)
    {
        var (entManager, gridUid) = await SetUpOpenGrid();
        EntityUid turretUid = default;

        await Pair.Server.WaitPost(() =>
        {
            turretUid = SpawnAt(entManager, ignore ? FireThroughTurret : HoldingTurret, gridUid, 0, 0);
            var hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 6, 0);
            var friendUid = SpawnAt(entManager, SyndicateMob, gridUid, 3, 0);
            var godmodeSystem = entManager.System<SharedGodmodeSystem>();
            godmodeSystem.EnableGodmode(hostileUid);
            godmodeSystem.EnableGodmode(friendUid);
        });

        var startAmmo = AmmoCount(entManager, turretUid);
        Assert.That(startAmmo, Is.GreaterThan(0));

        await RunSeconds(4f);

        if (ignore)
            Assert.That(AmmoCount(entManager, turretUid), Is.LessThan(startAmmo), "it should fire through the friend");
        else
            Assert.That(AmmoCount(entManager, turretUid), Is.EqualTo(startAmmo), "control: it should hold fire for the friend");
    }

    private async Task RunSeconds(float seconds)
    {
        var tickRate = (float)Pair.Server.ResolveDependency<IGameTiming>().TickRate;
        await Pair.RunTicksSync((int)Math.Ceiling(seconds * tickRate));
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
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-8, -8), new Vector2i(12, 8)).Owner;
        });

        return (entManager, gridUid);
    }
}
