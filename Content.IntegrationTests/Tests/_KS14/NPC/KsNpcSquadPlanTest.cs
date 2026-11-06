#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Doors;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Shared._KS14.NPC;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     A squad's plan for a hostile it can see and nobody can hit, from <see cref="NpcSquadTacticsSystem"/>.
/// </summary>
/// <remarks>
///     Tactics are paused and driven with <see cref="NpcSquadTacticsSystem.UpdateNow"/>, and contacts are staged. The hostile
///         is in a walled room, with a door and a window side by side in its west wall and the squad outside them. Who does
///         what from there, through the real root, is <see cref="KsOperativeHtnRootTest"/>'s.
/// </remarks>
public sealed class KsNpcSquadPlanTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string PlannerMob = "KsSquadPlanTestMob";
    private const string UnplannedMob = "KsSquadPlanTestMobUnplanned";
    private const string PatientMob = "KsSquadPlanTestMobPatient";
    private const string HastyMob = "KsSquadPlanTestMobHasty";
    private const string Glass = "KsSquadPlanTestGlass";
    private const string ArmouredGlass = "KsSquadPlanTestArmouredGlass";
    private const string Gun = "KsSquadPlanTestGun";
    private const string EmptyGun = "KsSquadPlanTestEmptyGun";
    private const string SecurityDoor = "AirlockSecurityLocked";

    /// <summary>
    ///     Long enough for a search for a path to come back.
    /// </summary>
    private const int ProbeTicks = 30;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsSquadPlanTestMob
  components:
  - type: Hands
  - type: DoAfter
  - type: NpcSquadMember
    tactics:
      planning: true
      planGrace: 0s
      stageTimeout: 0s
      maxStageDistance: 40

- type: entity
  parent: KsSquadPlanTestMob
  id: KsSquadPlanTestMobUnplanned
  components:
  - type: NpcSquadMember
    tactics:
      planning: false

- type: entity
  parent: KsSquadPlanTestMob
  id: KsSquadPlanTestMobPatient
  components:
  - type: NpcSquadMember
    tactics:
      planGrace: 1000s

- type: entity
  parent: KsSquadPlanTestMob
  id: KsSquadPlanTestMobHasty
  components:
  - type: NpcSquadMember
    tactics:
      breachTimeout: 0s

- type: entity
  id: KsSquadPlanTestGlass
  components:
  - type: Transform
    anchored: true
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeAabb
          bounds: -0.5,-0.5,0.5,0.5
        layer:
        - GlassLayer
  - type: Damageable
  - type: Destructible
    thresholds:
    - trigger: !type:DamageTrigger
        damage: 50
      behaviors:
      - !type:DoActsBehavior
        acts: [ Destruction ]

- type: entity
  parent: KsSquadPlanTestGlass
  id: KsSquadPlanTestArmouredGlass
  components:
  - type: Destructible
    thresholds:
    - trigger: !type:DamageTrigger
        damage: 500
      behaviors:
      - !type:DoActsBehavior
        acts: [ Destruction ]

- type: entity
  id: KsSquadPlanTestBullet
  components:
  - type: Physics
  - type: Fixtures
    fixtures:
      projectile:
        shape: !type:PhysShapeCircle
          radius: 0.1
        mask:
        - Impassable
        - BulletImpassable

- type: entity
  id: KsSquadPlanTestGun
  components:
  - type: Item
  - type: Gun
  - type: BallisticAmmoProvider
    proto: KsSquadPlanTestBullet
    capacity: 10

- type: entity
  id: KsSquadPlanTestEmptyGun
  components:
  - type: Item
  - type: Gun
  - type: BallisticAmmoProvider
    capacity: 10

- type: entity
  parent: KsSquadPlanTestMob
  id: KsSquadPlanTestMobRecovering
  components:
  - type: NpcRecovery

- type: entity
  parent: KsSquadPlanTestMob
  id: KsSquadPlanTestMobImpatient
  components:
  - type: NpcSquadMember
    tactics:
      planTimeout: 0s

# Shut to them, and it could be forced or broken: only what it is decides whether anyone does.
- type: entity
  id: KsSquadPlanTestShutDoor
  components:
  - type: Transform
    anchored: true
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeAabb
          bounds: -0.5,-0.5,0.5,0.5
        layer:
        - AirlockLayer
  - type: Door
    state: Closed
  - type: AccessReader
    access: [ [ Security ] ]
  - type: Damageable
  - type: Destructible
    thresholds:
    - trigger: !type:DamageTrigger
        damage: 50
      behaviors:
      - !type:DoActsBehavior
        acts: [ Destruction ]

# Shut against vacuum, with no air on a test map for a real firelock to be looking at.
- type: entity
  parent: KsSquadPlanTestShutDoor
  id: KsSquadPlanTestFirelock
  components:
  - type: Firelock
    pressure: true
";

    /// <summary>
    ///     A shot, and there is nothing to plan: an ordinary fight stays one.
    /// </summary>
    [Test]
    public async Task TestNoPlanWithAShot()
    {
        var scene = await SetUp(glass: null, door: "WallSolid", hostileAt: new Vector2i(-3, 5));

        await Pair.Server.WaitAssertion(() =>
        {
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
            Assert.That(GetPlan(scene), Is.Null);
        });
    }

    /// <summary>
    ///     Only a squad whose leader does this.
    /// </summary>
    [Test]
    public async Task TestOnlyThoseThatPlan()
    {
        var scene = await SetUp(mob: UnplannedMob);

        await Pair.Server.WaitAssertion(() =>
        {
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
            Assert.That(GetPlan(scene), Is.Null);
        });
    }

    /// <summary>
    ///     Nobody having a shot for a moment is nothing to go planning about, and the moment one shows up it is forgotten.
    /// </summary>
    [Test]
    public async Task TestGraceBeforePlanning()
    {
        var scene = await SetUp(mob: PatientMob);

        await Pair.Server.WaitAssertion(() =>
        {
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
            Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Grace));

            scene.EntManager.DeleteEntity(scene.GlassUid);
            scene.EntManager.DeleteEntity(scene.DoorUid);
            scene.TacticsSystem.UpdateNow();
            Assert.That(GetPlan(scene), Is.Null, "somebody has a shot now");
        });
    }

    /// <summary>
    ///     A way that is free they walk by themselves, as they always have. In by a gap well round the side of the room, as
    ///         one level with them would be a shot, not a way.
    /// </summary>
    [Test]
    public async Task TestNoPlanForAFreeWay()
    {
        var scene = await SetUp(door: "WallSolid", gap: new Vector2i(3, 6));

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Cooldown));
            AssertNoOrders(scene);
        });
    }

    /// <summary>
    ///     With a shut door and a tool, the one with the tool forces it while the other waits, and both come before fighting.
    ///         Orders below combat would never beat a gun plan.
    /// </summary>
    [Test]
    public async Task TestToolBearerForcesTheDoor()
    {
        var scene = await SetUp();
        EntityUid toolUid = default;

        await Pair.Server.WaitPost(() => toolUid = GiveInHand(scene, scene.MemberUid, TestJawsOfLife));
        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene);
            Assert.That(plan?.State, Is.EqualTo(NpcSquadPlanState.Running));
            Assert.That(plan!.Steps[..plan.StepCount], Is.EqualTo(new[] { NpcSquadAction.StackUp, NpcSquadAction.ForceDoor, NpcSquadAction.Enter }));

            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var breach), "it should have its orders");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out var stage), "it should have its orders");

            Assert.Multiple(() =>
            {
                Assert.That(breach.Kind, Is.EqualTo(NpcOrderKind.Breach));
                Assert.That(breach.TargetUid, Is.EqualTo(scene.DoorUid));
                Assert.That(breach.ToolUid, Is.EqualTo(toolUid));
                Assert.That(breach.OverCombat, "an order that does not come before fighting is never carried out");
                Assert.That(stage.Kind, Is.EqualTo(NpcOrderKind.Stage));
                Assert.That(stage.OverCombat);
            });
        });
    }

    /// <summary>
    ///     The moment somebody has a shot the plan is over and combat has them back.
    /// </summary>
    [Test]
    public async Task TestAShotEndsThePlan()
    {
        var scene = await SetUp();

        await Pair.Server.WaitPost(() => GiveInHand(scene, scene.MemberUid, TestJawsOfLife));
        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Running));

            scene.EntManager.DeleteEntity(scene.GlassUid);
            scene.EntManager.DeleteEntity(scene.DoorUid);
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Cooldown));
            AssertNoOrders(scene);
        });
    }

    /// <summary>
    ///     Nothing to force the door with, and the glass will not break: there is nothing to be done, and it does not spend
    ///         its time working that out again.
    /// </summary>
    [Test]
    public async Task TestNothingToBeDone()
    {
        var scene = await SetUp(glass: ArmouredGlass);

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene);
            Assert.That(plan?.State, Is.EqualTo(NpcSquadPlanState.Cooldown));
            Assert.That(plan!.CooldownEndsAt, Is.GreaterThan(IoCManager.Resolve<IGameTiming>().CurTime));
            AssertNoOrders(scene);
        });
    }

    /// <summary>
    ///     A door that does not give in the time it has is banned from the plan, and with nothing left to try, it ends.
    /// </summary>
    [Test]
    public async Task TestAFailedStepEndsThePlan()
    {
        var scene = await SetUp(mob: HastyMob, glass: ArmouredGlass);

        await Pair.Server.WaitPost(() => GiveInHand(scene, scene.MemberUid, TestJawsOfLife));
        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene);
            Assert.That(plan?.State, Is.EqualTo(NpcSquadPlanState.Cooldown));
            Assert.That(plan!.BannedActions & NpcSquadGoap.GetBan(NpcSquadAction.ForceDoor), Is.Not.Zero);
        });
    }

    /// <summary>
    ///     Glass that breaks is shot out, by those it stops and who have something to do it with. The door next to it stops
    ///         the other's shot, and a door is no window.
    /// </summary>
    [Test]
    public async Task TestGlassIsShotOut()
    {
        var scene = await SetUp();

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.GlassMemberUid, Gun);
            GiveInHand(scene, scene.DoorMemberUid, Gun);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene);
            Assert.That(plan?.State, Is.EqualTo(NpcSquadPlanState.Running));
            Assert.That(plan!.Steps[..plan.StepCount], Is.EqualTo(new[] { NpcSquadAction.BreakGlass }));

            Assert.That(scene.TacticsSystem.TryGetOrder(scene.GlassMemberUid, out var order), "it should be told to shoot");
            Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.BreakBarrier));
            Assert.That(order.TargetUid, Is.EqualTo(scene.GlassUid));
            Assert.That(order.OverCombat);
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.DoorMemberUid, out _), Is.False, "a door is no window");
        });
    }

    /// <summary>
    ///     Nobody can be drained of their ammo from behind armoured glass, and with no gun, or nothing in it, there is nothing
    ///         to shoot it with.
    /// </summary>
    [TestCase(ArmouredGlass, Gun)]
    [TestCase(Glass, null)]
    [TestCase(Glass, EmptyGun)]
    public async Task TestGlassThatIsLeftAlone(string glass, string? gun)
    {
        var scene = await SetUp(glass: glass);

        await Pair.Server.WaitPost(() =>
        {
            if (gun != null)
                GiveInHand(scene, scene.GlassMemberUid, gun);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Cooldown));
            AssertNoOrders(scene);
        });
    }

    /// <summary>
    ///     A window on a grille has both to break. The window alone would be worth it, and together they are not.
    /// </summary>
    [Test]
    public async Task TestWhatElseIsOnTheTileCounts()
    {
        var scene = await SetUp();

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.GlassMemberUid, Gun);
            SpawnAt(scene.EntManager, Glass, scene.GridUid, -1, 3);
            SpawnAt(scene.EntManager, Glass, scene.GridUid, -1, 3);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() => Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Cooldown)));
    }

    /// <summary>
    ///     A door is a way in, not a window, however easily it would break: it is opened, or left alone.
    /// </summary>
    [Test]
    public async Task TestADoorIsNotShotOut()
    {
        var scene = await SetUp(glass: ArmouredGlass, door: "KsSquadPlanTestShutDoor");

        await Pair.Server.WaitPost(() => GiveInHand(scene, scene.DoorMemberUid, Gun));
        await Step(scene);

        await Pair.Server.WaitAssertion(() => AssertNoOrders(scene));
    }

    /// <summary>
    ///     A firelock shut against vacuum or fire is not a way in, even for one with the tool to force it.
    /// </summary>
    [Test]
    public async Task TestFirelockHoldingBackDangerIsLeftShut()
    {
        var scene = await SetUp(glass: ArmouredGlass, door: "KsSquadPlanTestFirelock");

        await Pair.Server.WaitPost(() => GiveInHand(scene, scene.MemberUid, TestJawsOfLife));
        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Cooldown));
            AssertNoOrders(scene);
        });
    }

    /// <summary>
    ///     A plan that is taking too long is given up on, and its orders with it.
    /// </summary>
    [Test]
    public async Task TestAPlanThatTakesTooLongIsDropped()
    {
        var scene = await SetUp(mob: "KsSquadPlanTestMobImpatient");

        await Pair.Server.WaitPost(() => GiveInHand(scene, scene.MemberUid, TestJawsOfLife));
        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Running));

            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Cooldown));
            AssertNoOrders(scene);
        });
    }

    /// <summary>
    ///     One waiting for its meds to work is left out of it, and the rest carry on without it.
    /// </summary>
    [Test]
    public async Task TestARecoveringMemberIsLeftOut()
    {
        var scene = await SetUp(mob: "KsSquadPlanTestMobRecovering");

        await Pair.Server.WaitPost(() => GiveInHand(scene, scene.LeaderUid, TestJawsOfLife));
        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out _), "the test needs it under orders");

            scene.EntManager.System<Content.Server._KS14.NPC.Recovery.NpcRecoverySystem>().StartRecovery(scene.MemberUid);
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out _), Is.False, "it should be left out");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out _), "the rest should carry on");
        });
    }

    /// <summary>
    ///     A door a member remembers as shut to it is a wall to its paths, and it must stop being one the moment it is open,
    ///         or a squad could not walk through the door it just forced.
    /// </summary>
    [Test]
    public async Task TestAnOpenDoorIsNotAWallToThoseWhoWereBlocked()
    {
        var scene = await SetUp();
        var doorSystem = scene.EntManager.System<NpcDoorSystem>();
        var blockedDoors = new List<EntityUid>();

        await Pair.Server.WaitAssertion(() =>
        {
            doorSystem.ReportBlocked(scene.LeaderUid, scene.DoorUid);
            doorSystem.GetBlockedDoors(scene.LeaderUid, blockedDoors);
            Assert.That(blockedDoors, Does.Contain(scene.DoorUid), "the test needs it remembering the door");

            scene.EntManager.System<SharedDoorSystem>().SetState(scene.DoorUid, DoorState.Open);

            blockedDoors.Clear();
            doorSystem.GetBlockedDoors(scene.LeaderUid, blockedDoors);
            Assert.That(blockedDoors, Does.Not.Contain(scene.DoorUid));
        });
    }

    /// <summary>
    ///     An update with nothing going on allocates nothing, planning or not: it runs for every squad twice a second.
    /// </summary>
    [Test]
    public async Task TestQuietUpdateDoesNotAllocate()
    {
        var scene = await SetUp(hostileAt: new Vector2i(11, -11));

        await Pair.Server.WaitAssertion(() =>
        {
            for (var i = 0; i < 20; i++)
            {
                scene.TacticsSystem.UpdateNow();
            }

            const int updates = 200;
            var before = System.GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < updates; i++)
            {
                scene.TacticsSystem.UpdateNow();
            }

            var perUpdate = (System.GC.GetAllocatedBytesForCurrentThread() - before) / (double) updates;
            Assert.That(perUpdate, Is.LessThan(16), $"a quiet update allocated {perUpdate:F1} bytes");
        });
    }

    /// <summary>
    ///     Stages the contact, and updates twice: once to ask for a way to the hostile, and once when the answer is back.
    /// </summary>
    private async Task Step(Scene scene)
    {
        await Pair.Server.WaitPost(() =>
        {
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
        });

        await Pair.RunTicksSync(ProbeTicks);

        await Pair.Server.WaitPost(() =>
        {
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
        });
    }

    private static NpcSquadPlan? GetPlan(Scene scene)
    {
        return scene.EntManager.TryGetComponent<NpcSquadPlanComponent>(scene.SquadUid, out var planComponent) ? planComponent.Plan : null;
    }

    private static void AssertNoOrders(Scene scene)
    {
        Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out _), Is.False);
        Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out _), Is.False);
    }

    private static void StageVisible(Scene scene)
    {
        var now = IoCManager.Resolve<IGameTiming>().CurTime;
        var perceptionSystem = scene.EntManager.System<NpcPerceptionSystem>();
        var coordinates = scene.EntManager.GetComponent<TransformComponent>(scene.TargetUid).Coordinates;

        foreach (var memberUid in new[] { scene.LeaderUid, scene.MemberUid })
        {
            perceptionSystem.SetContact(memberUid, scene.TargetUid, new NpcContact(NpcContactState.Visible,
                now, now, now, coordinates, default, null, Reacted: true, ReactAt: now));
        }
    }

    private static EntityUid GiveInHand(Scene scene, EntityUid mobUid, string prototype)
    {
        var handsSystem = scene.EntManager.System<SharedHandsSystem>();
        var handId = $"hand{scene.EntManager.GetComponent<HandsComponent>(mobUid).Count}";
        handsSystem.AddHand(mobUid, handId, HandLocation.Right);

        var itemUid = scene.EntManager.SpawnEntity(prototype, scene.EntManager.GetComponent<TransformComponent>(mobUid).Coordinates);
        Assert.That(handsSystem.TryPickup(mobUid, itemUid, handId), "the test needs it holding that");
        return itemUid;
    }

    /// <summary>
    ///     The room, its door and window, and a squad of two outside them, one level with each.
    /// </summary>
    private async Task<Scene> SetUp(string mob = PlannerMob,
        string? glass = Glass,
        string door = SecurityDoor,
        Vector2i? hostileAt = null,
        Vector2i? gap = null)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default, firstUid = default, secondUid = default, targetUid = default, doorUid = default, glassUid = default;

        await server.WaitPost(() =>
        {
            tacticsSystem.UpdatesPaused = true;
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-12, -12), new Vector2i(12, 12)).Owner;

            for (var x = -1; x <= 7; x++)
            {
                for (var y = -1; y <= 6; y++)
                {
                    if (x is > -1 and < 7 && y is > -1 and < 6)
                        continue;

                    if (gap == new Vector2i(x, y))
                        continue;

                    if (x == -1 && y == 2)
                    {
                        doorUid = door == SecurityDoor
                            ? SpawnPoweredDoorAt(entManager, door, gridUid, x, y)
                            : SpawnAt(entManager, door, gridUid, x, y);
                    }
                    else if (x == -1 && y == 3)
                    {
                        glassUid = SpawnAt(entManager, glass ?? "WallSolid", gridUid, x, y);
                    }
                    else
                    {
                        SpawnAt(entManager, "WallSolid", gridUid, x, y);
                    }
                }
            }

            firstUid = SpawnAt(entManager, mob, gridUid, -3, 3);
            secondUid = SpawnAt(entManager, mob, gridUid, -3, 2);

            var hostileTile = hostileAt ?? new Vector2i(3, 3);
            targetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, hostileTile.X, hostileTile.Y);
        });

        await Pair.RunTicksSync(150);

        EntityUid squadUid = default, leaderUid = default;

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(firstUid, out var squadEntity), "the two should form a squad");
            squadUid = squadEntity!.Value.Owner;
            leaderUid = squadEntity.Value.Comp.Leader!.Value;
        });

        return new Scene(entManager,
            tacticsSystem,
            gridUid,
            squadUid,
            leaderUid,
            leaderUid == firstUid ? secondUid : firstUid,
            firstUid,
            secondUid,
            targetUid,
            doorUid,
            glassUid);
    }

    /// <param name="GlassMemberUid">The member level with the window.</param>
    /// <param name="DoorMemberUid">The member level with the door.</param>
    private sealed record Scene(
        IEntityManager EntManager,
        NpcSquadTacticsSystem TacticsSystem,
        EntityUid GridUid,
        EntityUid SquadUid,
        EntityUid LeaderUid,
        EntityUid MemberUid,
        EntityUid GlassMemberUid,
        EntityUid DoorMemberUid,
        EntityUid TargetUid,
        EntityUid DoorUid,
        EntityUid GlassUid);
}
