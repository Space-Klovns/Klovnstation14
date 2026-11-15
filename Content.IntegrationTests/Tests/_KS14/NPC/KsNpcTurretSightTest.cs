#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Squad;
using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Server.NPC.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Standing;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     What a turret sees is not what it can hit. It takes on a hostile it can see, past mobs standing or lying, and aims and
///         tracks it; it fires only once a round would get there by what the round collides with (glass stops a bullet, not
///         a laser) and, by its <see cref="NpcFireLaneMode"/>, would hit no friend, nor, if asked, a bystander. Walls still
///         hide a hostile. Of hostiles it can see, it takes on one its rounds can get to before one they cannot.
/// </summary>
public sealed class KsNpcTurretSightTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    public enum Blocker
    {
        None,
        Corpse,
        Wall,
        Window,
        Friend,
        Neutral,
    }

    private const string BallisticTurret = "WeaponTurretSyndicate";
    private const string LaserTurret = "WeaponTurretSyndicateLaser";
    private const string HeavyTurret = "KsWeaponTurretNanoTrasenHeavy";
    private const string StandingMobsBlockTurret = "KsTurretSightTestStandingMobsBlock";
    private const string FireThroughTurret = "KsTurretSightTestFireThrough";
    private const string HoldForNeutralsTurret = "KsTurretSightTestHoldForNeutrals";

    /// <summary>
    ///     Neither friendly nor hostile to the Syndicate turrets.
    /// </summary>
    private static readonly ProtoId<Content.Shared.NPC.Prototypes.NpcFactionPrototype> NeutralFaction = "Passive";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: WeaponTurretSyndicate
  id: KsTurretSightTestStandingMobsBlock
  components:
  - type: HTN
    rootTask:
      task: TurretCompound
    blackboard:
      RotateSpeed: !type:Single
        3.141
      KsStandingMobsBlockSight: !type:Bool
        true

- type: entity
  parent: WeaponTurretSyndicate
  id: KsTurretSightTestFireThrough
  components:
  - type: NpcFireLane
    mode: FireThrough

- type: entity
  parent: WeaponTurretSyndicate
  id: KsTurretSightTestHoldForNeutrals
  components:
  - type: NpcFireLane
    mode: HoldForFriendliesAndNeutrals
";

    /// <summary>
    ///     How many rounds go past the body before it is checked for hits.
    /// </summary>
    private const int CorpseRounds = 4;

    /// <summary>
    ///     How far off the hostile's bearing a turret may face and still count as aiming at it, in degrees.
    /// </summary>
    private const double AimToleranceDegrees = 10;

    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private static readonly ProtoId<DamageTypePrototype> Piercing = "Piercing";
    private static readonly ProtoId<DamageTypePrototype> Heat = "Heat";

    private sealed class Scene
    {
        public IEntityManager EntManager = default!;
        public EntityUid GridUid;
        public EntityUid TurretUid;
        public EntityUid HostileUid;
        public EntityUid? BlockerUid;
        public int StartAmmo;
    }

    /// <summary>
    ///     A real turret, a hostile six tiles off and, half way, a killed <c>MobHuman</c> of the hostile's faction, or a
    ///         wall. The turret fires past the body, its rounds pass over it, and it holds fire for the wall. The heavy
    ///         machine gun ignores friends, which the body is not; the laser turret checks its sight by the opaque mask.
    /// </summary>
    [TestCase(BallisticTurret, NanoTrasenMob, "NanoTrasen", Blocker.Corpse)]
    [TestCase(BallisticTurret, NanoTrasenMob, "NanoTrasen", Blocker.Wall)]
    [TestCase(HeavyTurret, SyndicateMob, "Syndicate", Blocker.Corpse)]
    [TestCase(HeavyTurret, SyndicateMob, "Syndicate", Blocker.Wall)]
    [TestCase(LaserTurret, NanoTrasenMob, "NanoTrasen", Blocker.Corpse)]
    [TestCase(LaserTurret, NanoTrasenMob, "NanoTrasen", Blocker.Wall)]
    public async Task TestCorpseDoesNotBlockTurret(string turret, string hostile, string hostileFaction, Blocker blocker)
    {
        var tickRate = TickRate();
        var clearTicks = await ClearTicks(turret, hostile, hostileFaction);

        if (blocker == Blocker.Wall)
        {
            var wallScene = await SetUp(turret, hostile, hostileFaction, Blocker.Wall);
            Assert.That(await RunUntilFired(wallScene, HoldTicks(clearTicks)), Is.Null,
                $"control: a wall should stop it (it fired within {clearTicks} ticks with nothing there)");
            return;
        }

        var scene = await SetUp(turret, hostile, hostileFaction, Blocker.Corpse);
        var corpseUid = scene.BlockerUid!.Value;
        FixedPoint2 corpseShotDamage = default;
        await Pair.Server.WaitPost(() => corpseShotDamage = ShotDamageOf(scene.EntManager, corpseUid));

        Assert.That(await RunUntilFired(scene, (int)(30f * tickRate)), Is.Not.Null,
            $"a body on the floor should not hide the hostile (it fired within {clearTicks} ticks with nothing there)");

        // A few more rounds through the body's tile, a laser being slower than a machine gun.
        var rounds = 1;
        for (var tick = 0; tick < (int)(10f * tickRate) && rounds < CorpseRounds; tick++)
        {
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() => rounds = scene.StartAmmo - AmmoCount(scene.EntManager, scene.TurretUid));
        }

        Assert.That(rounds, Is.GreaterThanOrEqualTo(CorpseRounds), "it should have kept firing");

        var hit = false;
        await Pair.Server.WaitPost(() => hit = ShotDamageOf(scene.EntManager, corpseUid) > corpseShotDamage);
        Assert.That(hit, Is.False, "its rounds should have passed over the body");
    }

    /// <summary>
    ///     A friend of the turret's standing in the line. The turret takes the hostile on, aims at it and follows it as both
    ///         move along, holding fire the while without giving the fight up; it fires once the friend steps aside.
    /// </summary>
    [Test]
    public async Task TestHoldsFireForFriendThenFires()
    {
        var tickRate = TickRate();
        var clearTicks = await ClearTicks(BallisticTurret, NanoTrasenMob, "NanoTrasen");
        var scene = await SetUp(BallisticTurret, NanoTrasenMob, "NanoTrasen", Blocker.Friend);

        await AssertHoldsFire(scene, HoldTicks(clearTicks), "with a friend in the line");

        // Both move, the friend staying in the line: the turret follows.
        await Pair.Server.WaitPost(() =>
        {
            MoveTo(scene, scene.HostileUid, new Vector2(6f, 3f));
            MoveTo(scene, scene.BlockerUid!.Value, new Vector2(3f, 1.5f));
        });

        await AssertHoldsFire(scene, (int)(2f * tickRate), "after both moved");

        // The friend steps aside.
        await Pair.Server.WaitPost(() => MoveTo(scene, scene.BlockerUid!.Value, new Vector2(3f, -3f)));
        Assert.That(await RunUntilFired(scene, (int)(10f * tickRate)), Is.Not.Null,
            "once the friend is out of the way it should fire");
    }

    /// <summary>
    ///     The switch back: with <c>KsStandingMobsBlockSight</c> on its blackboard, a friend standing in the line hides the
    ///         hostile, and the turret never takes it on.
    /// </summary>
    [Test]
    public async Task TestStandingMobsBlockSightSwitch()
    {
        var clearTicks = await ClearTicks(StandingMobsBlockTurret, NanoTrasenMob, "NanoTrasen");
        var scene = await SetUp(StandingMobsBlockTurret, NanoTrasenMob, "NanoTrasen", Blocker.Friend);
        var engaged = false;
        var fired = false;

        for (var tick = 0; tick < HoldTicks(clearTicks); tick++)
        {
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() =>
            {
                engaged |= scene.EntManager.HasComponent<NPCRangedCombatComponent>(scene.TurretUid);
                fired |= AmmoCount(scene.EntManager, scene.TurretUid) < scene.StartAmmo;
            });
        }

        Assert.That(engaged, Is.False, "the friend should hide the hostile");
        Assert.That(fired, Is.False, "nor should it have fired");
    }

    /// <summary>
    ///     A friend of the turret's, or a bystander (a faction neither friendly nor hostile to it), standing in the line,
    ///         under each <see cref="NpcFireLaneMode"/>. <c>FireThrough</c> fires through both; <c>HoldForFriendlies</c>, the
    ///         default (a turret without <c>NpcFireLane</c>), holds fire for the friend and fires through the bystander, as
    ///         turrets always have; <c>HoldForFriendliesAndNeutrals</c> holds for both. One holding fire aims and tracks the
    ///         while, and fires once whoever it was holding for steps aside.
    /// </summary>
    [TestCase(NpcFireLaneMode.FireThrough, Blocker.Friend)]
    [TestCase(NpcFireLaneMode.FireThrough, Blocker.Neutral)]
    [TestCase(NpcFireLaneMode.HoldForFriendlies, Blocker.Friend)]
    [TestCase(NpcFireLaneMode.HoldForFriendlies, Blocker.Neutral)]
    [TestCase(NpcFireLaneMode.HoldForFriendliesAndNeutrals, Blocker.Friend)]
    [TestCase(NpcFireLaneMode.HoldForFriendliesAndNeutrals, Blocker.Neutral)]
    public async Task TestFireLaneMode(NpcFireLaneMode mode, Blocker blocker)
    {
        var tickRate = TickRate();
        var turret = mode switch
        {
            NpcFireLaneMode.FireThrough => FireThroughTurret,
            NpcFireLaneMode.HoldForFriendliesAndNeutrals => HoldForNeutralsTurret,
            _ => BallisticTurret,
        };
        var holds = blocker == Blocker.Friend
            ? mode != NpcFireLaneMode.FireThrough
            : mode == NpcFireLaneMode.HoldForFriendliesAndNeutrals;

        if (!holds)
        {
            var scene = await SetUp(turret, NanoTrasenMob, "NanoTrasen", blocker);
            await Pair.Server.WaitPost(() => Assert.That(
                scene.EntManager.System<NpcSquadFireLaneSystem>().GetFireLaneMode(scene.TurretUid), Is.EqualTo(mode)));
            Assert.That(await RunUntilFired(scene, (int)(30f * tickRate)), Is.Not.Null,
                $"{mode} should fire through a {blocker} in the line");
            return;
        }

        var clearTicks = await ClearTicks(turret, NanoTrasenMob, "NanoTrasen");
        var holdingScene = await SetUp(turret, NanoTrasenMob, "NanoTrasen", blocker);
        await AssertHoldsFire(holdingScene, HoldTicks(clearTicks), $"{mode}, with a {blocker} in the line");

        await Pair.Server.WaitPost(() => MoveTo(holdingScene, holdingScene.BlockerUid!.Value, new Vector2(3f, -3f)));
        Assert.That(await RunUntilFired(holdingScene, (int)(10f * tickRate)), Is.Not.Null,
            $"{mode}: once the {blocker} is out of the way it should fire");
    }

    /// <summary>
    ///     The heavy machine gun fires through friends (<see cref="NpcFireLaneMode.FireThrough"/>): a friend of its in the
    ///         line does not stop it.
    /// </summary>
    [Test]
    public async Task TestHeavyTurretFiresPastFriend()
    {
        var scene = await SetUp(HeavyTurret, SyndicateMob, "Syndicate", Blocker.Friend);
        Assert.That(await RunUntilFired(scene, (int)(30f * TickRate())), Is.Not.Null,
            "a turret that ignores friendly blocking should fire past the friend");
    }

    /// <summary>
    ///     A real window in the line. A laser goes through glass and a bullet does not: the laser turret fires, the
    ///         ballistic one sees the hostile through it and aims and tracks, holding fire, without giving the fight up.
    /// </summary>
    [TestCase(LaserTurret, true)]
    [TestCase(BallisticTurret, false)]
    public async Task TestWindow(string turret, bool fires)
    {
        var tickRate = TickRate();
        var scene = await SetUp(turret, NanoTrasenMob, "NanoTrasen", Blocker.Window);

        if (fires)
        {
            Assert.That(await RunUntilFired(scene, (int)(30f * tickRate)), Is.Not.Null, "a laser should fire through the window");
            return;
        }

        var clearTicks = await ClearTicks(turret, NanoTrasenMob, "NanoTrasen");
        await AssertHoldsFire(scene, HoldTicks(clearTicks), "behind a window");

        await Pair.Server.WaitPost(() => MoveTo(scene, scene.HostileUid, new Vector2(6f, 0.4f)));
        await AssertHoldsFire(scene, (int)(2f * tickRate), "after the hostile moved behind the window");
    }

    /// <summary>
    ///     A hostile behind a window and, further off, another in the open. A ballistic turret takes on the one its bullets
    ///         can get to, though the other is nearer, and fires at it; once that one is gone it turns to the one behind the
    ///         window, aims and holds fire, as it would with only that one there. The heavy machine gun, which keeps its
    ///         target until it loses it, does the same.
    /// </summary>
    [TestCase(BallisticTurret, NanoTrasenMob, "NanoTrasen")]
    [TestCase(HeavyTurret, SyndicateMob, "Syndicate")]
    public async Task TestPrefersTargetItsRoundsReach(string turret, string hostile, string hostileFaction)
    {
        var tickRate = TickRate();
        var scene = await SetUp(turret, hostile, hostileFaction, Blocker.Window);
        var glassUid = scene.HostileUid;
        var openUid = await SpawnOpenHostile(scene, hostile);

        Assert.That(await RunUntilFired(scene, (int)(30f * tickRate)), Is.Not.Null,
            "it should have taken on the hostile in the open, which it can hit, and fired");
        await Pair.Server.WaitPost(() => Assert.That(
            scene.EntManager.GetComponent<NPCRangedCombatComponent>(scene.TurretUid).Target, Is.EqualTo(openUid),
            "it should be shooting the hostile in the open"));

        // Alone, the one behind glass is still taken on: aimed at, fire held.
        await Pair.Server.WaitPost(() =>
        {
            scene.EntManager.DeleteEntity(openUid);
            scene.StartAmmo = AmmoCount(scene.EntManager, scene.TurretUid);
        });
        scene.HostileUid = glassUid;
        await AssertHoldsFire(scene, (int)(8f * tickRate), "with only the hostile behind the window left");
    }

    /// <summary>
    ///     A turret aiming at a hostile behind a window, holding fire, turns to one that walks out into the open, further
    ///         off, and fires at it. The heavy machine gun keeps its target until it loses it, but not one it cannot hit
    ///         while another it can is there.
    /// </summary>
    [TestCase(BallisticTurret, NanoTrasenMob, "NanoTrasen")]
    [TestCase(HeavyTurret, SyndicateMob, "Syndicate")]
    public async Task TestSwitchesToTargetItsRoundsReach(string turret, string hostile, string hostileFaction)
    {
        var scene = await SetUp(turret, hostile, hostileFaction, Blocker.Window);
        await AssertHoldsFire(scene, (int)(8f * TickRate()), "behind a window");

        var openUid = await SpawnOpenHostile(scene, hostile);

        Assert.That(await RunUntilFired(scene, (int)(15f * TickRate())), Is.Not.Null,
            "it should have turned to the hostile in the open and fired");
        await Pair.Server.WaitPost(() => Assert.That(
            scene.EntManager.GetComponent<NPCRangedCombatComponent>(scene.TurretUid).Target, Is.EqualTo(openUid),
            "it should be shooting the hostile in the open"));
    }

    /// <summary>
    ///     A godmoded hostile in the open, seven tiles off: further than the one behind the window.
    /// </summary>
    private async Task<EntityUid> SpawnOpenHostile(Scene scene, string hostile)
    {
        var openUid = default(EntityUid);
        await Pair.Server.WaitPost(() =>
        {
            openUid = SpawnAt(scene.EntManager, hostile, scene.GridUid, 0, -7);
            scene.EntManager.System<SharedGodmodeSystem>().EnableGodmode(openUid);
        });

        return openUid;
    }

    /// <summary>
    ///     Runs <paramref name="ticks"/> ticks and checks the turret never fired, and that by the end it had been in the fight
    ///         with the hostile without a break since it took it on, holding fire for a blocked shot, and faced it.
    /// </summary>
    private async Task AssertHoldsFire(Scene scene, int ticks, string what)
    {
        var entManager = scene.EntManager;
        var transformSystem = entManager.System<SharedTransformSystem>();
        var engaged = false;
        var breaks = 0;
        var fired = false;

        for (var tick = 0; tick < ticks; tick++)
        {
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() =>
            {
                fired |= AmmoCount(entManager, scene.TurretUid) < scene.StartAmmo;
                var inFight = entManager.TryGetComponent(scene.TurretUid, out NPCRangedCombatComponent? ranged) &&
                    ranged.Target == scene.HostileUid;

                if (inFight)
                    engaged = true;
                else if (engaged)
                    breaks++;
            });
        }

        CombatStatus? status = null;
        double aimError = 180;
        await Pair.Server.WaitPost(() =>
        {
            if (entManager.TryGetComponent(scene.TurretUid, out NPCRangedCombatComponent? ranged))
                status = ranged.Status;

            var offset = transformSystem.GetWorldPosition(scene.HostileUid) - transformSystem.GetWorldPosition(scene.TurretUid);
            aimError = Math.Abs(Angle.ShortestDistance(transformSystem.GetWorldRotation(scene.TurretUid), offset.ToWorldAngle()).Degrees);
        });

        Assert.Multiple(() =>
        {
            Assert.That(fired, Is.False, $"{what}: it should have held fire");
            Assert.That(engaged, Is.True, $"{what}: it should have taken the hostile on");
            Assert.That(breaks, Is.Zero, $"{what}: it should not have given the fight up and replanned");
            Assert.That(status, Is.EqualTo(CombatStatus.KsShotBlocked), $"{what}: it should be holding fire for a blocked shot");
            Assert.That(aimError, Is.LessThan(AimToleranceDegrees), $"{what}: it should be aiming at the hostile");
        });
    }

    /// <summary>
    ///     How long the turret takes to wake, turn, lock and fire with nothing in the way: what one holding fire has to be
    ///         given, and more, before its silence means anything.
    /// </summary>
    private async Task<int> ClearTicks(string turret, string hostile, string hostileFaction)
    {
        var scene = await SetUp(turret, hostile, hostileFaction, Blocker.None);
        var clearTicks = await RunUntilFired(scene, (int)(30f * TickRate()));
        Assert.That(clearTicks, Is.Not.Null, "with nothing in the way it should have fired");
        return clearTicks!.Value;
    }

    private int HoldTicks(int clearTicks)
    {
        return clearTicks * 3 + (int)TickRate();
    }

    private float TickRate()
    {
        return (float)Pair.Server.ResolveDependency<IGameTiming>().TickRate;
    }

    /// <summary>
    ///     The turret at the middle of tile (0, 0) on a field of its own, a godmoded hostile at (6, 0), and half way what is
    ///         in the way: a killed <c>MobHuman</c> of the hostile's faction, a wall, a real window, or a godmoded friend of
    ///         the turret's, standing.
    /// </summary>
    private async Task<Scene> SetUp(string turret, string hostile, string hostileFaction, Blocker blocker)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();
        var scene = new Scene { EntManager = entManager };

        await server.WaitPost(() =>
        {
            scene.GridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-8, -8), new Vector2i(12, 8)).Owner;
            scene.TurretUid = SpawnAt(entManager, turret, scene.GridUid, 0, 0);
            scene.HostileUid = SpawnAt(entManager, hostile, scene.GridUid, 6, 0);
            var godmodeSystem = entManager.System<SharedGodmodeSystem>();
            var factionSystem = entManager.System<NpcFactionSystem>();
            godmodeSystem.EnableGodmode(scene.HostileUid);

            switch (blocker)
            {
                case Blocker.Wall:
                    scene.BlockerUid = SpawnAt(entManager, TestWall, scene.GridUid, 3, 0);
                    break;
                case Blocker.Window:
                    scene.BlockerUid = SpawnAt(entManager, "Window", scene.GridUid, 3, 0);
                    break;
                case Blocker.Friend:
                    var friendUid = SpawnAt(entManager, hostile == SyndicateMob ? NanoTrasenMob : SyndicateMob, scene.GridUid, 3, 0);
                    godmodeSystem.EnableGodmode(friendUid);
                    Assert.That(factionSystem.IsEntityFriendly(scene.TurretUid, friendUid), "the friend should be the turret's friend");
                    scene.BlockerUid = friendUid;
                    break;
                case Blocker.Neutral:
                    var neutralUid = SpawnAt(entManager, "MobHuman", scene.GridUid, 3, 0);
                    entManager.RemoveComponent<BarotraumaComponent>(neutralUid);
                    entManager.RemoveComponent<RespiratorComponent>(neutralUid);
                    factionSystem.ClearFactions(neutralUid);
                    factionSystem.AddFaction(neutralUid, NeutralFaction);
                    godmodeSystem.EnableGodmode(neutralUid);
                    Assert.That(factionSystem.IsEntityFriendly(scene.TurretUid, neutralUid), Is.False, "the bystander should not be the turret's friend");
                    Assert.That(factionSystem.IsEntityHostile(scene.TurretUid, neutralUid), Is.False, "nor hostile to it");
                    scene.BlockerUid = neutralUid;
                    break;
                case Blocker.Corpse:
                    var bodyUid = SpawnAt(entManager, "MobHuman", scene.GridUid, 3, 0);
                    entManager.RemoveComponent<BarotraumaComponent>(bodyUid);
                    entManager.RemoveComponent<RespiratorComponent>(bodyUid);
                    factionSystem.ClearFactions(bodyUid);
                    factionSystem.AddFaction(bodyUid, hostileFaction);
                    var blunt = new DamageSpecifier(prototypeManager.Index(Blunt), FixedPoint2.New(300));
                    entManager.System<DamageableSystem>().TryChangeDamage(bodyUid, blunt, ignoreResistances: true);

                    Assert.That(entManager.System<MobStateSystem>().IsDead(bodyUid), "the body should be dead");
                    Assert.That(entManager.System<StandingStateSystem>().IsDown(bodyUid), "the body should be lying down");
                    Assert.That(factionSystem.IsEntityFriendly(scene.TurretUid, bodyUid), Is.False,
                        "the body should not be the turret's friend, which the heavy machine gun would ignore anyway");
                    scene.BlockerUid = bodyUid;
                    break;
            }

            scene.StartAmmo = AmmoCount(entManager, scene.TurretUid);
        });

        Assert.That(scene.StartAmmo, Is.GreaterThan(0));
        return scene;
    }

    /// <summary>
    ///     Ticks until the turret has fired, or null if it had not by <paramref name="maxTicks"/>.
    /// </summary>
    private async Task<int?> RunUntilFired(Scene scene, int maxTicks)
    {
        for (var tick = 1; tick <= maxTicks; tick++)
        {
            await Pair.RunTicksSync(1);

            var fired = false;
            await Pair.Server.WaitPost(() => fired = AmmoCount(scene.EntManager, scene.TurretUid) < scene.StartAmmo);
            if (fired)
                return tick;
        }

        return null;
    }

    /// <summary>
    ///     Puts <paramref name="uid"/> at <paramref name="offset"/> from the middle of the turret's tile.
    /// </summary>
    private static void MoveTo(Scene scene, EntityUid uid, Vector2 offset)
    {
        scene.EntManager.System<SharedTransformSystem>()
            .SetCoordinates(uid, new EntityCoordinates(scene.GridUid, new Vector2(0.5f, 0.5f) + offset));
    }

    /// <summary>
    ///     The damage a bullet or a laser does, and nothing a body takes lying there.
    /// </summary>
    private static FixedPoint2 ShotDamageOf(IEntityManager entManager, EntityUid uid)
    {
        var damageable = entManager.GetComponent<DamageableComponent>(uid);
        var damage = entManager.System<DamageableSystem>().GetPositiveDamage((uid, damageable)).DamageDict;
        return (damage.TryGetValue(Piercing, out var piercing) ? piercing : FixedPoint2.Zero) +
            (damage.TryGetValue(Heat, out var heat) ? heat : FixedPoint2.Zero);
    }

    private static int AmmoCount(IEntityManager entManager, EntityUid gunUid)
    {
        var ev = new GetAmmoCountEvent();
        entManager.EventBus.RaiseLocalEvent(gunUid, ref ev);
        return ev.Count;
    }
}
