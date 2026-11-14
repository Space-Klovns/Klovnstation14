#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.NPC.Squad;
using Content.Shared._KS14.CCVar;
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
    private const string FireThroughMob = "KsTurretTargetingTestMobFireThrough";

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
  parent: KsSquadTestMobSyndicate
  id: KsTurretTargetingTestMobFireThrough
  components:
  - type: KsNpcAimTracking
    ignoreFriendlyBlocking: true

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

        // However long the turret takes to give the first up and plan again, up to a cap.
        EntityUid? newTarget = keptUid;
        for (var tick = 0; tick < (int)(15f * tickRate) && (newTarget == null || newTarget == keptUid); tick++)
        {
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() => newTarget = entManager.GetComponent<KsTargetLockOnComponent>(turretUid).Target);
        }

        Assert.That(newTarget, Is.EqualTo(keptUid == firstUid ? secondUid : firstUid), "once the first is out of sight it should take the other");
    }

    /// <summary>
    ///     A hostile at the end of a line with a friend of the turret's in the middle of it. Only a turret that ignores
    ///         friendly blocking fires; the control holds fire, and with no friend there it fires anyway.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestIgnoreFriendlyBlocking(bool ignore)
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcSquadFireLanes, true);

        var tickRate = (float)Pair.Server.ResolveDependency<IGameTiming>().TickRate;
        var capTicks = (int)(30f * tickRate);
        var turret = ignore ? FireThroughTurret : HoldingTurret;

        // With nobody in the way either turret fires. How long that takes is how long the turret needs to wake, turn
        //      and shoot, which is what a turret holding fire has to be given, and more, before its silence means anything.
        var clearTicks = await TicksUntilFired(turret, friendInTheWay: false, capTicks);
        Assert.That(clearTicks, Is.Not.Null, "with no friend in the way it should have fired");

        if (ignore)
        {
            Assert.That(await TicksUntilFired(turret, friendInTheWay: true, capTicks), Is.Not.Null,
                "a turret that ignores friendly blocking should have fired past the friend");
            return;
        }

        var heldTicks = clearTicks!.Value * 3 + (int)tickRate;
        Assert.That(await TicksUntilFired(turret, friendInTheWay: true, heldTicks), Is.Null,
            $"control: it should hold fire for the friend (it fired within {clearTicks} ticks with nobody there)");
    }

    /// <summary>
    ///     Sets a turret against a hostile down a line, on a field of its own, and runs until it has fired. Returns the
    ///         ticks that took, or null if it had not by <paramref name="maxTicks"/>.
    /// </summary>
    private async Task<int?> TicksUntilFired(string turret, bool friendInTheWay, int maxTicks)
    {
        var (entManager, gridUid) = await SetUpOpenGrid();
        EntityUid turretUid = default;
        var startAmmo = 0;

        await Pair.Server.WaitPost(() =>
        {
            turretUid = SpawnAt(entManager, turret, gridUid, 0, 0);
            var hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 6, 0);
            var godmodeSystem = entManager.System<SharedGodmodeSystem>();
            godmodeSystem.EnableGodmode(hostileUid);

            if (friendInTheWay)
                godmodeSystem.EnableGodmode(SpawnAt(entManager, SyndicateMob, gridUid, 3, 0));

            startAmmo = AmmoCount(entManager, turretUid);
        });

        Assert.That(startAmmo, Is.GreaterThan(0));

        for (var tick = 1; tick <= maxTicks; tick++)
        {
            await Pair.RunTicksSync(1);

            var fired = false;
            await Pair.Server.WaitPost(() => fired = AmmoCount(entManager, turretUid) < startAmmo);
            if (fired)
                return tick;
        }

        return null;
    }

    /// <summary>
    ///     The fire-lane check itself, which a turret's own body hides friends from, so asked of a mob: a friend in the
    ///         line of fire makes it hold fire, unless it ignores friendly blocking, for the aim and for a round as it goes.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TestFireLaneCheckIgnoresFriendsWhenAsked(bool ignore)
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcSquadFireLanes, true);

        var (entManager, gridUid) = await SetUpOpenGrid();
        var fireLaneSystem = entManager.System<NpcSquadFireLaneSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        await Pair.Server.WaitPost(() =>
        {
            var shooterUid = SpawnAt(entManager, ignore ? FireThroughMob : SyndicateMob, gridUid, 0, 0);
            SpawnAt(entManager, SyndicateMob, gridUid, 3, 0);
            var from = transformSystem.GetMapCoordinates(shooterUid);
            var aim = new MapCoordinates(from.Position + new Vector2(6f, 0f), from.MapId);

            Assert.That(fireLaneSystem.TryGetClearShot(shooterUid, from, aim, 0.35f, out _), Is.EqualTo(ignore));
            Assert.That(fireLaneSystem.IsShotClear(shooterUid, from, aim), Is.EqualTo(ignore));
        });
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
