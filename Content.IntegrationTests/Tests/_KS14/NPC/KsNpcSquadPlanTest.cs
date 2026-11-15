#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Doors;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server.Body.Systems;
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
    private static readonly Robust.Shared.Prototypes.ProtoId<Content.Shared.Damage.Prototypes.DamageTypePrototype> BluntDamage = "Blunt";

    private const string EmptyGun = "KsSquadPlanTestEmptyGun";
    private const string Flashbang = "KsSquadPlanTestFlashbang";
    private const string ArmedHostile = "KsSquadPlanTestArmedHostile";
    private const string LaserHostile = "KsSquadPlanTestLaserHostile";
    private const string SecurityDoor = TestSecurityAirlock;
    private const string SteadyMob = "KsSquadPlanTestMobSteady";
    private const string Frag = "KsSquadPlanTestFrag";
    private const string TimedCharge = "KsSquadPlanTestTimedCharge";
    private const string WiredCharge = "KsSquadPlanTestWiredCharge";
    private const string FussyCharge = "KsSquadPlanTestFussyCharge";
    private const string Activator = "KsSquadPlanTestActivator";
    private const string Meds = "KsSquadPlanTestMeds";
    private const string MaskedMob = "KsSquadPlanTestMobMasked";
    private const string BreathingHostile = "KsSquadPlanTestBreathingHostile";
    private const string TearGas = "KsSquadPlanTestTearGas";
    private const string PlainSmoke = "KsSquadPlanTestPlainSmoke";
    private const string SmokeSightedMob = "KsSquadPlanTestMobSmokeSighted";
    private const string SmokeSightedHostile = "KsSquadPlanTestSmokeSightedHostile";

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
      planChargeMinPressure: 0 # a test map has no air in it

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
      planHoldTime: 0s

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
  id: KsSquadPlanTestFlashbang
  components:
  - type: Item
  - type: FlashOnTrigger
    range: 7
  - type: TimerTrigger
    delay: 1000

- type: entity
  parent: KsSquadTestMobNanoTrasen
  id: KsSquadPlanTestArmedHostile
  components:
  - type: Gun
  - type: BallisticAmmoProvider
    proto: KsSquadPlanTestBullet
    capacity: 10

- type: entity
  parent: KsSquadTestMobNanoTrasen
  id: KsSquadPlanTestLaserHostile
  components:
  - type: Gun
  - type: Battery
    maxCharge: 1000
    startingCharge: 1000
  - type: BatteryAmmoProvider
    proto: KsLineOfFireTestBeam

- type: entity
  parent: KsSquadPlanTestMob
  id: KsSquadPlanTestMobWithdrawing
  components:
  - type: NpcSquadMember
    tactics:
      planWithdrawHealth: 0.4

- type: entity
  parent: KsSquadPlanTestMob
  id: KsSquadPlanTestMobRecovering
  components:
  - type: NpcRecovery

# Waits for everyone to be on their spot, however long that takes.
- type: entity
  parent: KsSquadPlanTestMobRecovering
  id: KsSquadPlanTestMobRecoveringPatient
  components:
  - type: NpcSquadMember
    tactics:
      stageTimeout: 1000s

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

# Goes by the air there is, of which a test map has none.
- type: entity
  parent: KsSquadPlanTestMobSteady
  id: KsSquadPlanTestMobAirMinded
  components:
  - type: NpcSquadMember
    tactics:
      planChargeMinPressure: 50

# Hunts for what it loses, the moment it does.
- type: entity
  parent: KsSquadPlanTestMob
  id: KsSquadPlanTestMobHunter
  components:
  - type: NpcSquadMember
    tactics:
      planning: false
      watchTime: 0s

- type: entity
  parent: KsSquadPlanTestMobHunter
  id: KsSquadPlanTestMobBlaster
  components:
  - type: NpcSquadMember
    tactics:
      huntCharges: true

# Would rather do anything than wait for a hostile to come out.
- type: entity
  parent: KsSquadPlanTestMob
  id: KsSquadPlanTestMobSteady
  components:
  - type: NpcSquadMember
    tactics:
      planHoldCost: 100

# The way round the far side of the room is some 17 tiles more than the straight line and the near side some 6, which a
#   limit of 10 keeps well apart, however the two shuffle about while their plan is worked out.
- type: entity
  parent: KsSquadPlanTestMobSteady
  id: KsSquadPlanTestMobShortDetour
  components:
  - type: NpcSquadMember
    tactics:
      maxDetour: 10

- type: entity
  parent: KsSquadPlanTestMob
  id: KsSquadPlanTestMobNoPatience
  components:
  - type: NpcSquadMember
    tactics:
      planHoldTime: 0s

- type: entity
  parent: KsSquadPlanTestArmedHostile
  id: KsSquadPlanTestFlashProofHostile
  components:
  - type: FlashImmunity

- type: entity
  id: KsSquadPlanTestFrag
  components:
  - type: Item
  - type: Explosive
    explosionType: Default
    totalIntensity: 10
    intensitySlope: 3
    maxIntensity: 10
  - type: TimerTrigger
    delay: 1000

- type: entity
  parent: KsNpcTestMedipen
  id: KsSquadPlanTestMeds

- type: entity
  id: KsSquadPlanTestTimedCharge
  components:
  - type: Item
  - type: Sticky
  - type: Explosive
    explosionType: Default
    totalIntensity: 10
    intensitySlope: 3
    maxIntensity: 10
  - type: TimerTrigger
    delay: 4

- type: entity
  parent: BaseItem
  id: KsSquadPlanTestWiredCharge
  components:
  - type: Sticky
  - type: LinkedChainEnd
  - type: Explosive
    explosionType: Default
    totalIntensity: 10
    intensitySlope: 3
    maxIntensity: 10

# Goes on walls and on nothing else, as the real one goes on walls and airlocks.
- type: entity
  parent: KsSquadPlanTestWiredCharge
  id: KsSquadPlanTestFussyCharge
  components:
  - type: Sticky
    whitelist:
      tags:
      - Wall

- type: entity
  parent: BaseItem
  id: KsSquadPlanTestActivator
  components:
  - type: LinkedChainStart
    segmentId: KsWireSegment
    segmentOffset: 0.125
    segments: 11

# Shut against vacuum, with no air on a test map for a real firelock to be looking at.
- type: entity
  parent: KsSquadPlanTestShutDoor
  id: KsSquadPlanTestFirelock
  components:
  - type: Firelock
    pressure: true

# Goes on internals when told to, with a mask and a tank given it by the test.
- type: entity
  parent: KsSquadPlanTestMobSteady
  id: KsSquadPlanTestMobMasked
  components:
  - type: Inventory
    templateId: human
  - type: ContainerContainer
  - type: Internals
  - type: NpcInternalsUser

- type: entity
  parent: KsSquadPlanTestArmedHostile
  id: KsSquadPlanTestBreathingHostile
  components:
  - type: SolutionContainerManager
  - type: Bloodstream
    bloodlossDamage:
      types:
        Bloodloss: 1
    bloodlossHealDamage:
      types:
        Bloodloss: -1

- type: entity
  id: KsSquadPlanTestMask
  components:
  - type: Item
  - type: BreathMask

- type: entity
  parent: KsNpcTestOxygenTank
  id: KsSquadPlanTestTank

- type: entity
  id: KsSquadPlanTestTearGas
  components:
  - type: Item
  - type: NpcRoomSmoke
  - type: TimerTrigger
    delay: 1000
  - type: SmokeOnTrigger
    keysIn:
    - timer
    spreadAmount: 5
    smokePrototype: KsNpcTestSmoke
    solution:
      reagents:
      - ReagentId: TearGas
        Quantity: 20

# Sees through smoke, as the Redacted do.
- type: entity
  parent: KsSquadPlanTestMobMasked
  id: KsSquadPlanTestMobSmokeSighted
  components:
  - type: NpcPerception
    seesThroughSmoke: true

- type: entity
  parent: KsSquadPlanTestMobSteady
  id: KsSquadPlanTestMobSoftener
  components:
  - type: NpcSquadMember
    tactics:
      watchTime: 0s

- type: entity
  parent: KsSquadPlanTestMobSmokeSighted
  id: KsSquadPlanTestMobSmokeFirst
  components:
  - type: NpcSquadMember
    tactics:
      planSmokeOverFragChance: 1

- type: entity
  parent: KsSquadPlanTestMobSmokeSighted
  id: KsSquadPlanTestMobFragFirst
  components:
  - type: NpcSquadMember
    tactics:
      planSmokeOverFragChance: 0

- type: entity
  parent: KsSquadPlanTestArmedHostile
  id: KsSquadPlanTestSmokeSightedHostile
  components:
  - type: NpcPerception
    seesThroughSmoke: true

- type: entity
  id: KsSquadPlanTestPlainSmoke
  components:
  - type: Item
  - type: NpcRoomSmoke
  - type: TimerTrigger
    delay: 1000
  - type: SmokeOnTrigger
    keysIn:
    - timer
    spreadAmount: 5
";

    /// <summary>
    ///     A shot, and there is nothing to plan: an ordinary fight stays one.
    /// </summary>
    [Test]
    public async Task TestNoPlanWithAShot()
    {
        var scene = await SetUp(glass: null, door: TestWall, hostileAt: new Vector2i(-3, 5));

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
        var scene = await SetUp(door: TestWall, gap: new Vector2i(3, 6));

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Cooldown));
            AssertNoOrders(scene);
        });
    }

    /// <summary>
    ///     With a shut door and a tool, the one with the tool forces it while the other stays where it can see the hostile, so
    ///         that the squad does not lose it. Both come before fighting: orders below combat would never beat a gun plan.
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
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out var watch), "it should have its orders");

            Assert.Multiple(() =>
            {
                Assert.That(breach.Kind, Is.EqualTo(NpcOrderKind.Breach));
                Assert.That(breach.TargetUid, Is.EqualTo(scene.DoorUid));
                Assert.That(breach.ToolUid, Is.EqualTo(toolUid));
                Assert.That(breach.OverCombat, "an order that does not come before fighting is never carried out");
                Assert.That(watch.Kind, Is.EqualTo(NpcOrderKind.Watch));
                Assert.That(watch.OverCombat);
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
            Assert.That(plan!.BannedActions & NpcSquadGoap.GetBan(NpcSquadAction.ForceDoor), Is.Not.Zero);

            // Waiting for it to come out is all that is left, and it does not wait long either.
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(plan.State, Is.EqualTo(NpcSquadPlanState.Cooldown), plan.Reason);
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
    ///     What a squad does depends on what it can see. The same door, the same tool, the same flashbang: it goes on a hostile
    ///         with a gun in its hand, and is saved from one without.
    /// </summary>
    [TestCase(ArmedHostile, true)]
    [TestCase(NanoTrasenMob, false)]
    public async Task TestAnArmedHostileGetsAFlashbang(string hostile, bool flashed)
    {
        var scene = await SetUp(glass: ArmouredGlass, hostile: hostile);

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.MemberUid, TestJawsOfLife);
            GiveInHand(scene, scene.MemberUid, Flashbang);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene);
            Assert.That(plan?.State, Is.EqualTo(NpcSquadPlanState.Running));
            Assert.That(plan!.Steps[..plan.StepCount], flashed
                ? Is.EqualTo(new[] { NpcSquadAction.StackUp, NpcSquadAction.ForceDoor, NpcSquadAction.Flashbang, NpcSquadAction.EnterSoftened })
                : Is.EqualTo(new[] { NpcSquadAction.StackUp, NpcSquadAction.ForceDoor, NpcSquadAction.Enter }));
        });
    }

    /// <summary>
    ///     The door they meant to flash through coming open gives them a shot, and the flashbang still goes first. Dropping the
    ///         plan for that shot would have them stand in a doorway trading fire with what they came to blind.
    /// </summary>
    [Test]
    public async Task TestTheDoorOpeningDoesNotCallOffTheFlashbang()
    {
        var scene = await SetUp(glass: ArmouredGlass, hostile: ArmedHostile);

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.MemberUid, TestJawsOfLife);
            GiveInHand(scene, scene.MemberUid, Flashbang);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Running));

            scene.EntManager.DeleteEntity(scene.DoorUid);
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Running), "the shot should wait for the flashbang");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order), "it should be told to throw");
            Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.ThrowGrenade));
            Assert.That(order.OverCombat);
        });
    }

    /// <summary>
    ///     A hostile that moves between the plan being made and the throw: it goes where the hostile is now, not where it was.
    /// </summary>
    [Test]
    public async Task TestTheThrowFollowsTheHostile()
    {
        var scene = await SetUp(glass: ArmouredGlass, hostile: ArmedHostile);

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.MemberUid, TestJawsOfLife);
            GiveInHand(scene, scene.MemberUid, Flashbang);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Running));

            // Both in line with the doorway, so that nothing in the way sends it just inside instead.
            var transformSystem = scene.EntManager.System<SharedTransformSystem>();
            transformSystem.SetCoordinates(scene.TargetUid, new EntityCoordinates(scene.GridUid, 5.5f, 2.5f));
            transformSystem.SetCoordinates(scene.MemberUid, new EntityCoordinates(scene.GridUid, -2.5f, 2.5f));
            scene.EntManager.DeleteEntity(scene.DoorUid);
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order), "it should be told to throw");
            Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.ThrowGrenade));
            var miss = transformSystem.ToMapCoordinates(order.Coordinates).Position - transformSystem.GetMapCoordinates(scene.TargetUid).Position;
            Assert.That(miss.Length(), Is.LessThan(0.01f), "it should be thrown at where the hostile is now");
        });
    }

    /// <summary>
    ///     A grenade goes in where it leaves them out of the blast. Against a hostile just inside the door, that is nowhere,
    ///         and the same squad goes in without. They are clear of tile 1 where they stand now: it is where they will be
    ///         standing to throw it that counts.
    /// </summary>
    [TestCase(3, true)]
    [TestCase(1, false)]
    public async Task TestAGrenadeGoesWhereItIsSafe(int hostileX, bool thrown)
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass, hostile: ArmedHostile, hostileAt: new Vector2i(hostileX, 2));

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.MemberUid, TestJawsOfLife);
            GiveInHand(scene, scene.MemberUid, Frag);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            Assert.That(plan.State, Is.EqualTo(NpcSquadPlanState.Running));
            Assert.That(plan.Steps[..plan.StepCount].Contains(NpcSquadAction.FragRoom), Is.EqualTo(thrown), plan.Facts.ToString());
        });
    }

    /// <summary>
    ///     A flashbang is wasted on what a flash does nothing to, and they can see that. With a grenade as well, that goes in.
    /// </summary>
    [Test]
    public async Task TestAFlashProofHostileGetsAGrenade()
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass, hostile: "KsSquadPlanTestFlashProofHostile");

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.MemberUid, TestJawsOfLife);
            GiveInHand(scene, scene.MemberUid, Flashbang);
            GiveInHand(scene, scene.MemberUid, Frag);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            Assert.That(plan.Steps[..plan.StepCount], Does.Contain(NpcSquadAction.FragRoom));
            Assert.That(plan.Steps[..plan.StepCount], Does.Not.Contain(NpcSquadAction.Flashbang));
        });
    }

    /// <summary>
    ///     Gas goes in on a hostile that would breathe it, and only once every member is on internals: none of them takes a
    ///         breath of it.
    /// </summary>
    [Test]
    public async Task TestGasGoesInOnInternals()
    {
        var scene = await SetUp(MaskedMob, glass: ArmouredGlass, hostile: BreathingHostile);

        await Pair.Server.WaitPost(() =>
        {
            Mask(scene, scene.LeaderUid);
            Mask(scene, scene.MemberUid);
            GiveInHand(scene, scene.MemberUid, TestJawsOfLife);
            GiveInHand(scene, scene.MemberUid, TearGas);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            Assert.That(plan.Steps[..plan.StepCount],
                Is.EqualTo(new[] { NpcSquadAction.StackUp, NpcSquadAction.ForceDoor, NpcSquadAction.SmokeRoom, NpcSquadAction.EnterSoftened }),
                plan.Facts.ToString());
            Assert.That(plan.SmokeGasses, "tear gas should be taken for gas");

            var internalsSystem = scene.EntManager.System<InternalsSystem>();
            Assert.That(internalsSystem.AreInternalsWorking(scene.LeaderUid), Is.False, "nobody needs them yet");

            scene.EntManager.DeleteEntity(scene.DoorUid);
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order), "it should be told to throw");
            Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.ThrowGrenade));
            Assert.That(internalsSystem.AreInternalsWorking(scene.LeaderUid), "the leader should be on internals before it goes in");
            Assert.That(internalsSystem.AreInternalsWorking(scene.MemberUid), "and so should the thrower");
        });
    }

    /// <summary>
    ///     Gas is not put in where it would do nothing, or where one of them would breathe it: not on a hostile that breathes
    ///         nothing, and not by a squad with a member that cannot go on internals.
    /// </summary>
    [TestCase(BreathingHostile, false)]
    [TestCase(ArmedHostile, true)]
    public async Task TestGasOnlyWhereItWorks(string hostile, bool leaderHasTank)
    {
        var scene = await SetUp(MaskedMob, glass: ArmouredGlass, hostile: hostile);

        await Pair.Server.WaitPost(() =>
        {
            Mask(scene, scene.LeaderUid, leaderHasTank);
            Mask(scene, scene.MemberUid);
            GiveInHand(scene, scene.MemberUid, TestJawsOfLife);
            GiveInHand(scene, scene.MemberUid, TearGas);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            Assert.That(plan.State, Is.EqualTo(NpcSquadPlanState.Running));
            Assert.That(plan.Steps[..plan.StepCount], Does.Not.Contain(NpcSquadAction.SmokeRoom), plan.Facts.ToString());
        });
    }

    /// <summary>
    ///     Smoke that only blocks sight goes in on a hostile it would blind, by a squad it would not: it is no use against
    ///         what sees through it, and a squad that does not would blind itself.
    /// </summary>
    [TestCase(SmokeSightedMob, ArmedHostile, true)]
    [TestCase(MaskedMob, ArmedHostile, false)]
    [TestCase(SmokeSightedMob, SmokeSightedHostile, false)]
    public async Task TestASmokeScreen(string mob, string hostile, bool screened)
    {
        var scene = await SetUp(mob, glass: ArmouredGlass, hostile: hostile);

        await Pair.Server.WaitPost(() =>
        {
            Mask(scene, scene.LeaderUid);
            Mask(scene, scene.MemberUid);
            GiveInHand(scene, scene.MemberUid, TestJawsOfLife);
            GiveInHand(scene, scene.MemberUid, PlainSmoke);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            Assert.That(plan.State, Is.EqualTo(NpcSquadPlanState.Running));
            Assert.That(plan.Steps[..plan.StepCount].Contains(NpcSquadAction.SmokeRoom), Is.EqualTo(screened), plan.Facts.ToString());
            Assert.That(plan.SmokeGasses, Is.False, "plain smoke carries nothing");
        });
    }

    /// <summary>
    ///     A squad that plans, going in after a hostile it lost, puts something in ahead of itself, if what it last saw was a
    ///         hostile with a gun: going by that, never by the hostile itself, which here is the other way about. The throw goes
    ///         where they believe it is, not where it went, and once it has gone off they go in.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestAHuntSoftensTheRoomOnWhatItLastSaw(bool seenArmed)
    {
        // Armed if it was seen unarmed, and the other way about: only what they saw may count.
        var scene = await SetUp("KsSquadPlanTestMobSoftener", glass: ArmouredGlass, hostile: seenArmed ? "KsSquadTestMobNanoTrasen" : ArmedHostile);

        await Pair.Server.WaitAssertion(() =>
        {
            scene.EntManager.DeleteEntity(scene.DoorUid);
            GiveInHand(scene, scene.MemberUid, Flashbang);

            var now = IoCManager.Resolve<IGameTiming>().CurTime;
            var transformSystem = scene.EntManager.System<SharedTransformSystem>();
            var believed = transformSystem.GetMoverCoordinates(scene.TargetUid);
            var traits = NpcContactTraits.Seen | NpcContactTraits.Flashable | (seenArmed ? NpcContactTraits.Armed : NpcContactTraits.None);

            foreach (var memberUid in new[] { scene.LeaderUid, scene.MemberUid })
            {
                scene.EntManager.System<NpcPerceptionSystem>().SetContact(memberUid, scene.TargetUid, new NpcContact(NpcContactState.Lost,
                    now, now, now, believed, default, null, Reacted: true, ReactAt: now, ObserverWasMoving: false, Traits: traits));
            }

            // Somewhere else in the room by now, unseen.
            transformSystem.SetCoordinates(scene.TargetUid, new EntityCoordinates(scene.GridUid, 5.5f, 0.5f));

            Assert.That(scene.TacticsSystem.IsHoldingThrows(scene.MemberUid), Is.False, "no hunt, nothing to hold throws for");
            scene.TacticsSystem.UpdateNow();
            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid);
            Assert.That(hunt, Is.Not.Null, "the test needs them hunting");
            Assert.That(hunt!.StagedMembers, Is.Not.Empty, "the test needs them stacked up on the room");

            for (var i = 0; i < 3 && hunt.Phase is NpcHuntPhase.Stage or NpcHuntPhase.Breach; i++)
            {
                scene.TacticsSystem.UpdateNow();
            }

            if (!seenArmed)
            {
                // Past the point where something would have gone in: still stacking up, nothing has been decided yet.
                Assert.That(hunt.Phase, Is.Not.AnyOf(NpcHuntPhase.Stage, NpcHuntPhase.Breach), "the test needs them past stacking up");
                Assert.That(hunt.Phase, Is.Not.EqualTo(NpcHuntPhase.Soften), "nothing goes in ahead of them on a hostile last seen unarmed");
                Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var walkIn) && walkIn.Kind == NpcOrderKind.ThrowGrenade, Is.False);
                return;
            }

            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Soften), "a hostile last seen armed gets something in ahead of them");
            Assert.That(scene.TacticsSystem.IsHoldingThrows(scene.LeaderUid), "nobody throws of their own accord while the squad does");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order), "the one with the flashbang should be told to throw it");
            Assert.Multiple(() =>
            {
                Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.ThrowGrenade));
                Assert.That(order.OverCombat, "a throw, once begun, is not cut short");

                var aimed = transformSystem.ToMapCoordinates(order.Coordinates).Position;
                var live = transformSystem.GetMapCoordinates(scene.TargetUid).Position;
                Assert.That((aimed - live).Length(), Is.GreaterThan(1f), "it should be thrown where they believe it is, not where it went");
            });

            // In the air, and gone off: they go in.
            scene.EntManager.DeleteEntity(order.ToolUid!.Value);
            scene.TacticsSystem.UpdateNow();
            hunt.Soften!.ThrownAt = IoCManager.Resolve<IGameTiming>().CurTime - TimeSpan.FromSeconds(10);
            scene.TacticsSystem.UpdateNow();

            Assert.That(hunt.Phase, Is.Not.EqualTo(NpcHuntPhase.Soften), "once it has gone off, they go in");
            Assert.That(hunt.Soften, Is.Null);
            Assert.That(scene.TacticsSystem.IsHoldingThrows(scene.LeaderUid), "with something already gone in, nothing more is thrown on the way in");
        });
    }

    /// <summary>
    ///     A squad that does not plan goes straight in after a hostile it lost, armed or not.
    /// </summary>
    [Test]
    public async Task TestAHuntThatDoesNotPlanDoesNotSoften()
    {
        var scene = await SetUp("KsSquadPlanTestMobHunter", glass: ArmouredGlass, hostile: ArmedHostile);

        await Pair.Server.WaitAssertion(() =>
        {
            scene.EntManager.DeleteEntity(scene.DoorUid);
            GiveInHand(scene, scene.MemberUid, Flashbang);

            var now = IoCManager.Resolve<IGameTiming>().CurTime;
            var coordinates = scene.EntManager.GetComponent<TransformComponent>(scene.TargetUid).Coordinates;
            const NpcContactTraits traits = NpcContactTraits.Seen | NpcContactTraits.Flashable | NpcContactTraits.Armed;

            foreach (var memberUid in new[] { scene.LeaderUid, scene.MemberUid })
            {
                scene.EntManager.System<NpcPerceptionSystem>().SetContact(memberUid, scene.TargetUid, new NpcContact(NpcContactState.Lost,
                    now, now, now, coordinates, default, null, Reacted: true, ReactAt: now, ObserverWasMoving: false, Traits: traits));
            }

            for (var i = 0; i < 4; i++)
            {
                scene.TacticsSystem.UpdateNow();
            }

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid);
            Assert.That(hunt, Is.Not.Null, "the test needs them hunting");
            // Softening is only ever tried on the way in: a hunt still stacking up has tried nothing, planning or not.
            Assert.That(hunt!.Phase, Is.Not.AnyOf(NpcHuntPhase.Stage, NpcHuntPhase.Breach), "the test needs them past stacking up");
            Assert.That(hunt.SoftenTried, Is.False);
            Assert.That(hunt.Phase, Is.Not.EqualTo(NpcHuntPhase.Soften));
        });
    }

    /// <summary>
    ///     With a grenade and smoke both to hand, which goes in is down to the plan's roll, and only one of them does.
    /// </summary>
    [TestCase("KsSquadPlanTestMobSmokeFirst", NpcSquadAction.SmokeRoom, NpcSquadAction.FragRoom)]
    [TestCase("KsSquadPlanTestMobFragFirst", NpcSquadAction.FragRoom, NpcSquadAction.SmokeRoom)]
    public async Task TestSmokeOrGrenadeIsRolled(string mob, NpcSquadAction used, NpcSquadAction unused)
    {
        var scene = await SetUp(mob, glass: ArmouredGlass, hostile: ArmedHostile);

        await Pair.Server.WaitPost(() =>
        {
            Mask(scene, scene.LeaderUid);
            Mask(scene, scene.MemberUid);
            GiveInHand(scene, scene.MemberUid, TestJawsOfLife);
            GiveInHand(scene, scene.MemberUid, Frag);
            GiveInHand(scene, scene.MemberUid, PlainSmoke);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            Assert.That(plan.State, Is.EqualTo(NpcSquadPlanState.Running));
            Assert.That(plan.Steps[..plan.StepCount], Does.Contain(used), plan.Facts.ToString());
            Assert.That(plan.Steps[..plan.StepCount], Does.Not.Contain(unused), plan.Facts.ToString());
        });
    }

    /// <summary>
    ///     What screening a room rests on: smoke blocks sight as a wall does, a player's (the occluder tree) and an NPC's,
    ///         save for an eye that sees through it.
    /// </summary>
    [Test]
    public async Task TestSmokeBlocksSightSaveForThoseThatSeeThroughIt()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid fromUid = default, toUid = default;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid, new Vector2i(-3, -3), new Vector2i(3, 3)).Owner;
            fromUid = SpawnAt(entManager, "KsSquadPlanTestMask", gridUid, -2, 0);
            toUid = SpawnAt(entManager, "KsSquadPlanTestMask", gridUid, 2, 0);

            for (var y = -2; y <= 2; y++)
            {
                SpawnAt(entManager, TestSmoke, gridUid, 0, y);
            }
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var transformSystem = entManager.System<SharedTransformSystem>();
            var from = transformSystem.GetMapCoordinates(fromUid);
            var to = transformSystem.GetMapCoordinates(toUid);
            var lineOfSightSystem = entManager.System<NpcLineOfSightSystem>();

            Assert.That(lineOfSightSystem.InLineOfSightByOccluderTree(from, to, 10f), Is.False, "smoke should block a player's view");
            Assert.That(lineOfSightSystem.InLineOfSight(from, to, 10f), Is.False, "and an NPC's");
            Assert.That(lineOfSightSystem.InLineOfSight(from, to, 10f, throughSmoke: true), "but not one that sees through it");
        });
    }

    /// <summary>
    ///     With nothing to soften up a gun with, they wait for it to come out rather than walk in on it.
    /// </summary>
    [Test]
    public async Task TestAGunIsWaitedOutRatherThanRushed()
    {
        var scene = await SetUp(glass: ArmouredGlass, hostile: ArmedHostile);

        await Pair.Server.WaitPost(() => GiveInHand(scene, scene.MemberUid, TestJawsOfLife));
        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            Assert.That(plan.Steps[..plan.StepCount], Is.EqualTo(new[] { NpcSquadAction.StackUp, NpcSquadAction.HoldExits }));
        });
    }

    /// <summary>
    ///     And when it does not come out, they go in after it after all.
    /// </summary>
    [Test]
    public async Task TestWaitingGivesWayToGoingIn()
    {
        var scene = await SetUp("KsSquadPlanTestMobNoPatience", glass: ArmouredGlass, hostile: ArmedHostile);

        await Pair.Server.WaitPost(() => GiveInHand(scene, scene.MemberUid, TestJawsOfLife));
        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            var plan = GetPlan(scene)!;
            Assert.That(plan.State, Is.EqualTo(NpcSquadPlanState.Running), plan.Reason);
            Assert.That(plan.BannedActions & NpcSquadGoap.GetBan(NpcSquadAction.HoldExits), Is.Not.Zero, "it should be done waiting");
            Assert.That(plan.Steps[..plan.StepCount], Does.Contain(NpcSquadAction.ForceDoor));
        });
    }

    /// <summary>
    ///     Somebody hurt uses its meds before going in on a gun, and on orders that come before fighting. Not for anyone unarmed,
    ///         which is not worth what they would use.
    /// </summary>
    [TestCase(ArmedHostile, true)]
    [TestCase(NanoTrasenMob, false)]
    public async Task TestPatchesUpBeforeGoingInOnAGun(string hostile, bool patches)
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass, hostile: hostile);

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.MemberUid, TestJawsOfLife);
            GiveInHand(scene, scene.LeaderUid, Meds);
            Hurt(scene, scene.LeaderUid, 50);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            Assert.That(plan.Steps[..plan.StepCount].Contains(NpcSquadAction.PatchUp), Is.EqualTo(patches));

            if (!patches)
                return;

            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(plan.Steps[plan.CurrentStep], Is.EqualTo(NpcSquadAction.PatchUp));
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out var order), "it should be told to");
            Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.PatchUp));
            Assert.That(order.OverCombat);
        });
    }

    /// <summary>
    ///     Waiting on meds that were used is not losing a member: nobody has to be sent anywhere over it.
    /// </summary>
    [Test]
    public async Task TestWaitingOnMedsDoesNotStackThemUpAgain()
    {
        var scene = await SetUp("KsSquadPlanTestMobRecoveringPatient", glass: ArmouredGlass);

        await Pair.Server.WaitPost(() => GiveInHand(scene, scene.LeaderUid, TestJawsOfLife));
        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            var hunt = plan.Hunt!;
            var transformSystem = scene.EntManager.System<SharedTransformSystem>();

            foreach (var (memberUid, staging) in hunt.StagedMembers)
            {
                transformSystem.SetCoordinates(memberUid, hunt.Entrances[staging.EntranceIndex].StageCoordinates);
            }

            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
            Assert.That(plan.Stacked, "the test needs them stacked up");

            // Off its spot, so that anything sending them back there would have to wait for it.
            transformSystem.SetCoordinates(scene.LeaderUid, scene.EntManager.GetComponent<TransformComponent>(scene.LeaderUid).Coordinates.Offset(new Vector2(-4f, 0f)));

            scene.EntManager.System<Content.Server._KS14.NPC.Recovery.NpcRecoverySystem>().StartRecovery(scene.MemberUid);
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(plan.Stacked, "nobody should be sent back to stack up");
        });
    }

    /// <summary>
    ///     Somebody low changes its magazine before going in, on orders that come before fighting. Nothing to change it for, and
    ///         nothing gets in the way of going in.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestTopsUpBeforeGoingIn(bool spare)
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass);

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.MemberUid, TestJawsOfLife);

            var gunUid = GiveInHand(scene, scene.LeaderUid, TestPistol);

            var containerSystem = scene.EntManager.System<Robust.Shared.Containers.SharedContainerSystem>();
            Assert.That(containerSystem.TryGetContainer(gunUid, "gun_magazine", out var magazineContainer), "the test needs its magazine");

            var magazineUid = magazineContainer!.ContainedEntities[0];
            var ballisticComponent = scene.EntManager.GetComponent<Content.Shared.Weapons.Ranged.Components.BallisticAmmoProviderComponent>(magazineUid);
            scene.EntManager.System<Content.Shared.Weapons.Ranged.Systems.SharedGunSystem>().SetBallisticUnspawned((magazineUid, ballisticComponent), 1);

            if (spare)
                GiveInHand(scene, scene.LeaderUid, TestMagazine);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            Assert.That(plan.Steps[..plan.StepCount].Contains(NpcSquadAction.TopUp), Is.EqualTo(spare), plan.Facts.ToString());

            if (!spare)
                return;

            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out var order), "it should be told to");
            Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.TopUp));
            Assert.That(order.OverCombat);
        });
    }

    /// <summary>
    ///     A door shut to all of them is no end of it for a squad with a charge, and the member with it goes to put it on.
    /// </summary>
    [Test]
    public async Task TestADoorNothingOpensGetsACharge()
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass);

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.MemberUid, WiredCharge);
            GiveInHand(scene, scene.MemberUid, Activator);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            Assert.That(plan.Steps[..plan.StepCount], Is.EqualTo(new[] { NpcSquadAction.StackUp, NpcSquadAction.BlowDoor, NpcSquadAction.Enter }), plan.Facts.ToString());

            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(plan.Charge?.Pick.Site.TargetUid, Is.EqualTo(scene.DoorUid));
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order), "it should be told to");
            Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.PlantCharge));
            Assert.That(order.TargetUid, Is.EqualTo(scene.DoorUid));
            Assert.That(order.OverCombat);
        });
    }

    /// <summary>
    ///     A way in round the far side of the room is a long way round next to blowing the door or wall in front of them
    ///         (more than <c>maxDetour</c> further than the straight line), and loses to it: it is no free way to leave them to
    ///         walk, nor one to spread out to. One round the near side is not, and is walked.
    /// </summary>
    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task TestALongWayRoundLosesToACharge(bool door, bool farSide)
    {
        // Far side: round the room and in level with the hostile at (3, 3). Near side: in through the top, by the corner.
        var scene = await SetUp("KsSquadPlanTestMobShortDetour", glass: ArmouredGlass, door: door ? SecurityDoor : TestWall, gap: farSide ? new Vector2i(7, 3) : new Vector2i(1, 6));

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.MemberUid, WiredCharge);
            GiveInHand(scene, scene.MemberUid, Activator);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            var steps = plan.Steps[..plan.StepCount];

            if (!farSide)
            {
                // Walked by themselves, or walked to as a plan: either way, not blown.
                if (plan.Reason != "a free way: they walk it themselves")
                    Assert.That(steps, Is.EqualTo(new[] { NpcSquadAction.StackUp, NpcSquadAction.Enter }), $"{plan.Facts} {plan.Reason}");

                return;
            }

            Assert.That(steps,
                Is.EqualTo(door
                    ? new[] { NpcSquadAction.StackUp, NpcSquadAction.BlowDoor, NpcSquadAction.Enter }
                    : new[] { NpcSquadAction.BlowWall, NpcSquadAction.Enter }),
                $"{plan.Facts} {plan.Reason}");

            if (!door)
                return;

            // Nor does anybody set off round the room to spread out, with the door to go through.
            Assert.That(plan.Hunt!.StagedMembers, Is.Not.Empty);
            foreach (var staging in plan.Hunt.StagedMembers.Values)
            {
                Assert.That(plan.Hunt.Entrances[staging.EntranceIndex].DoorUid, Is.EqualTo(scene.DoorUid));
            }
        });
    }

    /// <summary>
    ///     No activator, no way of setting off a charge with no timer. A wired charge is no use to anyone then.
    /// </summary>
    /// <summary>
    ///     One member kept back to watch plants nothing. With the only charge on whichever of the two would be kept back, it is
    ///         the other that watches, and the wall still comes down. It used to keep the carrier back, and the squad walked round.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestTheOnlyChargeIsNotKeptBackToWatch(bool glassMemberCarries)
    {
        var scene = await SetUp("KsSquadPlanTestMobShortDetour", glass: ArmouredGlass, door: TestWall, gap: new Vector2i(7, 3));
        var carrierUid = glassMemberCarries ? scene.GlassMemberUid : scene.DoorMemberUid;

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, carrierUid, WiredCharge);
            GiveInHand(scene, carrierUid, Activator);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            Assert.That(plan.Steps[..plan.StepCount], Is.EqualTo(new[] { NpcSquadAction.BlowWall, NpcSquadAction.Enter }), $"{plan.Facts} {plan.Reason}");
            Assert.That(plan.WatcherUid, Is.Not.EqualTo(carrierUid));
            Assert.That(plan.WallCharge!.Value.PlanterUid, Is.EqualTo(carrierUid));
        });
    }

    [Test]
    public async Task TestAWiredChargeNeedsItsActivator()
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass);

        await Pair.Server.WaitPost(() => GiveInHand(scene, scene.MemberUid, WiredCharge));
        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Cooldown), "there is nothing it can do"));
    }

    /// <summary>
    ///     A charge goes where its own rules let it, for them as for anyone: this one goes on walls and not on doors.
    /// </summary>
    [Test]
    public async Task TestAChargeOnlyGoesWhereItSticks()
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass);

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.MemberUid, FussyCharge);
            GiveInHand(scene, scene.MemberUid, Activator);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            Assert.That(plan.DoorCharge, Is.Null, "the door is not for this charge");
            Assert.That(plan.WallCharge, Is.Not.Null, "the wall is");
        });
    }

    /// <summary>
    ///     With both, the wired one goes on: it goes off when it suits them, and not when it suits a timer.
    /// </summary>
    [TestCase(WiredCharge, Content.Server._KS14.NPC.Charges.NpcChargeMethod.Wired)]
    [TestCase(TimedCharge, Content.Server._KS14.NPC.Charges.NpcChargeMethod.Timed)]
    public async Task TestWiredBeforeTimed(string only, Content.Server._KS14.NPC.Charges.NpcChargeMethod expected)
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass);

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.MemberUid, TimedCharge);

            if (only == WiredCharge)
            {
                GiveInHand(scene, scene.MemberUid, WiredCharge);
                GiveInHand(scene, scene.MemberUid, Activator);
            }
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() => Assert.That(GetPlan(scene)!.DoorCharge?.Charge.Method, Is.EqualTo(expected)));
    }

    /// <summary>
    ///     Anyone can cut a wire. A charge with nothing to set it off is a charge wasted, and they find another way.
    /// </summary>
    [Test]
    public async Task TestACutWireCallsOffTheCharge()
    {
        var (scene, _, activatorUid) = await SetUpChargeOn();

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            scene.EntManager.System<Content.Shared._KS14.Chain.ChainSystem>().TryBreakChainFrom(activatorUid, removeJoints: true);
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(plan.BannedActions & NpcSquadGoap.GetBan(NpcSquadAction.BlowDoor), Is.Not.Zero, "that should be the end of that");
            Assert.That(plan.Charge, Is.Null);
        });
    }

    /// <summary>
    ///     Gone off, there is nothing left to press: while the dust settles the planter waits with the rest, rather than
    ///         keep its order to set it off and press a spent activator.
    /// </summary>
    [Test]
    public async Task TestThePlanterStandsDownOnceItHasGoneOff()
    {
        var (scene, chargeUid, _) = await SetUpChargeOn();

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            scene.EntManager.DeleteEntity(chargeUid);
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(plan.Charge?.Phase, Is.EqualTo(NpcSquadChargePhase.Blasted), "the test needs it gone off");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order), "it should still have an order");
            Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.Watch));
        });
    }

    /// <summary>
    ///     A squad whose wired charge is on the door, its wire whole, and everyone getting clear.
    /// </summary>
    private async Task<(Scene Scene, EntityUid ChargeUid, EntityUid ActivatorUid)> SetUpChargeOn()
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass);
        EntityUid chargeUid = default, activatorUid = default;

        await Pair.Server.WaitPost(() =>
        {
            chargeUid = GiveInHand(scene, scene.MemberUid, WiredCharge);
            activatorUid = GiveInHand(scene, scene.MemberUid, Activator);
        });

        await Step(scene);

        await Pair.Server.WaitPost(() =>
        {
            var coordinates = scene.EntManager.GetComponent<TransformComponent>(scene.MemberUid).Coordinates;
            scene.EntManager.EventBus.RaiseLocalEvent(chargeUid, new Content.Shared.Interaction.InteractUsingEvent(scene.MemberUid, activatorUid, chargeUid, coordinates));

            var stickyComponent = scene.EntManager.GetComponent<Content.Shared.Sticky.Components.StickyComponent>(chargeUid);
            scene.EntManager.System<Content.Shared.Sticky.Systems.StickySystem>().StickToEntity((chargeUid, stickyComponent), scene.DoorUid, scene.MemberUid);

            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
        });

        await Pair.RunTicksSync(3);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(GetPlan(scene)!.Charge?.Phase, Is.EqualTo(NpcSquadChargePhase.Clearing), "the test needs the charge on, and the wire whole"));

        return (scene, chargeUid, activatorUid);
    }

    /// <summary>
    ///     A squad that can do nothing about a hostile it can see and cannot shoot holds where it is, watching it. Left to
    ///         itself, each member would go after a shot it will never get: a firing spot, closing in, giving up, again.
    ///         With the cooldown up it looks for a way again at once, still holding, rather than leave them to it for the
    ///         grace first.
    /// </summary>
    [Test]
    public async Task TestNothingToBeDoneHoldsAndLooksAgain()
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass);

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            Assert.That(plan.State, Is.EqualTo(NpcSquadPlanState.Cooldown), plan.Reason);

            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
            AssertHoldingOff(scene, "while it cools down");

            plan.CooldownEndsAt = System.TimeSpan.Zero;
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Probing), "it should look again at once");
            AssertHoldingOff(scene, "while it looks again");
        });
    }

    private static void AssertHoldingOff(Scene scene, string when)
    {
        foreach (var memberUid in new[] { scene.LeaderUid, scene.MemberUid })
        {
            Assert.That(scene.TacticsSystem.TryGetOrder(memberUid, out var order), $"each should be told to hold, {when}");
            Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.Watch), when);
            Assert.That(order.OverCombat, $"over trying for a shot of its own, {when}");
        }
    }

    /// <summary>
    ///     A plan that ends with a charge on, whatever ends it, leaves the charge where it is and lets go of the wire, or the
    ///         planter would walk off still tied to it.
    /// </summary>
    [Test]
    public async Task TestAPlanThatEndsLetsGoOfTheWire()
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass);
        EntityUid chargeUid = default, activatorUid = default;

        await Pair.Server.WaitPost(() =>
        {
            chargeUid = GiveInHand(scene, scene.MemberUid, WiredCharge);
            activatorUid = GiveInHand(scene, scene.MemberUid, Activator);
        });

        await Step(scene);

        await Pair.Server.WaitPost(() =>
        {
            var coordinates = scene.EntManager.GetComponent<TransformComponent>(scene.MemberUid).Coordinates;
            scene.EntManager.EventBus.RaiseLocalEvent(chargeUid, new Content.Shared.Interaction.InteractUsingEvent(scene.MemberUid, activatorUid, chargeUid, coordinates));

            var stickyComponent = scene.EntManager.GetComponent<Content.Shared.Sticky.Components.StickyComponent>(chargeUid);
            scene.EntManager.System<Content.Shared.Sticky.Systems.StickySystem>().StickToEntity((chargeUid, stickyComponent), scene.DoorUid, scene.MemberUid);

            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
        });

        await Pair.RunTicksSync(3);

        await Pair.Server.WaitAssertion(() =>
        {
            var chargeSystem = scene.EntManager.System<Content.Server._KS14.NPC.Charges.NpcChargeSystem>();
            var plan = GetPlan(scene)!;
            Assert.That(plan.Charge?.Phase, Is.EqualTo(NpcSquadChargePhase.Clearing), "the test needs the charge on");
            Assert.That(chargeSystem.IsWireIntact(chargeUid, activatorUid), "and the wire whole");

            scene.EntManager.DeleteEntity(scene.TargetUid);
            scene.TacticsSystem.UpdateNow();

            Assert.That(plan.State, Is.EqualTo(NpcSquadPlanState.Cooldown), "the test needs the plan over");
            Assert.That(plan.Charge, Is.Null);
            Assert.That(chargeSystem.IsWireIntact(chargeUid, activatorUid), Is.False, "it should have let go");
            Assert.That(scene.EntManager.EntityExists(chargeUid), "the charge stays where it is");
        });
    }

    /// <summary>
    ///     With a charge on, everyone gets out of sight of the floor in front of it, as nothing can see into a wall. And a shot that
    ///         turns up meanwhile waits: going back to the fight would have them stand there as it goes off.
    /// </summary>
    [Test]
    public async Task TestAChargeThatIsOnComesFirst()
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass);
        EntityUid chargeUid = default;

        await Pair.Server.WaitPost(() => chargeUid = GiveInHand(scene, scene.MemberUid, TimedCharge));
        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            var plan = GetPlan(scene)!;
            Assert.That(plan.Charge, Is.Not.Null, "the test needs a charge going on");

            var site = plan.Charge!.Pick.Site;
            var stickyComponent = scene.EntManager.GetComponent<Content.Shared.Sticky.Components.StickyComponent>(chargeUid);
            scene.EntManager.System<Content.Shared.Sticky.Systems.StickySystem>().StickToEntity((chargeUid, stickyComponent), site.TargetUid, scene.MemberUid);

            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(plan.Charge?.Phase, Is.EqualTo(NpcSquadChargePhase.Clearing), plan.Reason);
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order), "it should be told to get clear");
            Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.TakeCover));
            Assert.That(order.Coordinates.Position, Is.EqualTo(site.StandCoordinates.Position));

            scene.EntManager.DeleteEntity(scene.GlassUid);
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(plan.State, Is.EqualTo(NpcSquadPlanState.Running), "the shot should wait for the charge");
            Assert.That(plan.Charge, Is.Not.Null);
        });
    }

    /// <summary>
    ///     The furthest a planter stands from a wired charge has to be on the wire, or it would pull it off its feet to get there.
    /// </summary>
    [Test]
    public async Task TestTheWireReachesTheStandOff()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid startUid = default, endUid = default;

        await server.WaitPost(() =>
        {
            startUid = entManager.SpawnEntity("KsWireStart", map.GridCoords);
            endUid = entManager.SpawnEntity("KsWireEnd", map.GridCoords.Offset(new Vector2(1f, 0f)));
            entManager.EventBus.RaiseLocalEvent(endUid, new Content.Shared.Interaction.InteractUsingEvent(startUid, startUid, endUid, map.GridCoords));
        });

        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            var linkComponent = entManager.GetComponent<Content.Shared._KS14.Chain.ChainLinkComponent>(startUid);
            var jointComponent = entManager.GetComponent<Robust.Shared.Physics.JointComponent>(startUid);
            var joint = (Robust.Shared.Physics.Dynamics.Joints.DistanceJoint) jointComponent.GetJoints[linkComponent.StretchJointId!];

            Assert.That(joint.MaxLength, Is.GreaterThan(NpcSquadTacticsSystem.MaxStandOff));

            // What a planter's spot is held to, round corners as well, is how far it really reaches.
            Assert.That(entManager.System<Content.Server._KS14.NPC.Charges.NpcChargeSystem>().GetWireReach(startUid),
                Is.EqualTo(joint.MaxLength).Within(0.001f));
        });
    }

    /// <summary>
    ///     A wired charge that is on is given up on for its planter getting nowhere, not for how long it has been on: getting to
    ///         where it sets it off from can be slow, past the rest of the squad, and pressing it can wait on them being clear.
    ///         It used to be six seconds from going on, after which the squad let go of the wire and held where it was.
    /// </summary>
    [Test]
    public async Task TestAWiredChargeIsOnlyGivenUpOnForGettingNowhere()
    {
        var (scene, _, _) = await SetUpChargeOn();

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            var charge = plan.Charge!;
            var now = IoCManager.Resolve<IGameTiming>().CurTime;
            var timeout = plan.Hunt!.Settings.PlanChargeTimeout;

            // Long on, but getting nearer where it sets it off from.
            charge.PlantedAt = now - TimeSpan.FromSeconds(10);
            charge.ProgressAt = now - TimeSpan.FromSeconds(1);
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
            Assert.That(plan.Charge, Is.SameAs(charge), "on for a while is no reason to give it up");

            // Getting nowhere.
            charge.ProgressAt = now - timeout - TimeSpan.FromSeconds(1);
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
            Assert.That(plan.Charge, Is.Null, "a planter getting nowhere is");
        });
    }

    /// <summary>
    ///     A squad that has only lost someone does not go blowing doors in, whatever it carries. That takes a plan.
    /// </summary>
    [Test]
    public async Task TestAHuntUsesNoCharges()
    {
        var scene = await SetUp("KsSquadPlanTestMobHunter", glass: ArmouredGlass);

        await Pair.Server.WaitAssertion(() =>
        {
            GiveInHand(scene, scene.MemberUid, WiredCharge);
            GiveInHand(scene, scene.MemberUid, Activator);

            var now = IoCManager.Resolve<IGameTiming>().CurTime;
            var coordinates = scene.EntManager.GetComponent<TransformComponent>(scene.TargetUid).Coordinates;

            foreach (var memberUid in new[] { scene.LeaderUid, scene.MemberUid })
            {
                scene.EntManager.System<NpcPerceptionSystem>().SetContact(memberUid, scene.TargetUid, new NpcContact(NpcContactState.Lost,
                    now, now, now, coordinates, default, null, Reacted: true, ReactAt: now, ObserverWasMoving: false));
            }

            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid);
            Assert.That(hunt, Is.Not.Null, "the test needs them hunting");
            Assert.That(hunt!.Entrances, Has.Count.EqualTo(1), "the test needs them to know of the door");
            Assert.That(hunt.Phase, Is.Not.EqualTo(NpcHuntPhase.Blast), "shut out, it does not blow its way in either");

            // Nobody staged at all is fine: with no way in but the charge, nobody can lead.
            foreach (var staging in hunt.StagedMembers.Values)
            {
                Assert.That(staging.Method, Is.Not.EqualTo(NpcBreachMethod.Charge));
            }
        });
    }

    /// <summary>
    ///     A hunt that is to blow its way in, and whose charge comes to nothing: it is shut out after all, and holds outside.
    /// </summary>
    [Test]
    public async Task TestABlastThatComesToNothingHoldsOutside()
    {
        var scene = await SetUp("KsSquadPlanTestMobBlaster", glass: ArmouredGlass);

        await Pair.Server.WaitAssertion(() =>
        {
            var chargeUid = GiveInHand(scene, scene.MemberUid, TimedCharge);
            var now = IoCManager.Resolve<IGameTiming>().CurTime;
            var coordinates = scene.EntManager.GetComponent<TransformComponent>(scene.TargetUid).Coordinates;

            foreach (var memberUid in new[] { scene.LeaderUid, scene.MemberUid })
            {
                scene.EntManager.System<NpcPerceptionSystem>().SetContact(memberUid, scene.TargetUid, new NpcContact(NpcContactState.Lost,
                    now, now, now, coordinates, default, null, Reacted: true, ReactAt: now, ObserverWasMoving: false));
            }

            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid)!;
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Blast), "with a charge, a door nobody can open is no end of it");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order) && order.Kind == NpcOrderKind.PlantCharge, "it should go to put it on");
            Assert.That(order.OverCombat);

            scene.EntManager.DeleteEntity(chargeUid);
            scene.TacticsSystem.UpdateNow();

            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Exhausted));
            Assert.That(hunt.ShutOut, "that leaves it shut out");
        });
    }

    /// <summary>
    ///     A squad shut out after a noise blows its way in together. Nobody can stack up on a door nobody can open, so the rest
    ///         come along behind the planter to the edge of the blast, not wait wherever they heard it from.
    /// </summary>
    [Test]
    public async Task TestTheRestComeAlongToABlast()
    {
        var scene = await SetUp("KsSquadPlanTestMobBlaster", glass: ArmouredGlass);

        await Pair.Server.WaitAssertion(() =>
        {
            GiveInHand(scene, scene.MemberUid, TimedCharge);
            var now = IoCManager.Resolve<IGameTiming>().CurTime;
            var coordinates = scene.EntManager.GetComponent<TransformComponent>(scene.TargetUid).Coordinates;

            foreach (var memberUid in new[] { scene.LeaderUid, scene.MemberUid })
            {
                scene.EntManager.System<NpcPerceptionSystem>().SetContact(memberUid, scene.TargetUid, new NpcContact(NpcContactState.Lost,
                    now, now, now, coordinates, default, null, Reacted: true, ReactAt: now, ObserverWasMoving: false));
            }

            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid)!;
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Blast), "the test needs a blast");
            Assert.That(hunt.StagedMembers, Is.Empty, "the test needs nobody stacked up");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var planterOrder) && planterOrder.Kind == NpcOrderKind.PlantCharge,
                "the test needs the member planting");

            var site = hunt.Blast!.Charge!.Pick.Site;
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out var order), "the leader should be given something to do");
            Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.Stage), "it should come along, not hold where it is");
            Assert.That(order.Coordinates, Is.EqualTo(site.StandCoordinates));
            Assert.That(order.OverCombat, "nor drop it for the hostile it cannot get at");
        });
    }

    /// <summary>
    ///     As above, but with the squad near the planter's spot only through a wall: in the next corridor over, which joins this
    ///         one far to the north. Close enough in a straight line to count as just out of the blast already, so the rest used
    ///         to stand where they heard it while the planter walked round alone. They walk round too, behind the planter, then to
    ///         the edge of the blast, and keep going once the charge is on, rather than stop wherever they are and be left behind.
    /// </summary>
    [Test]
    public async Task TestTheRestWalkRoundToABlastBeyondAWall()
    {
        // A wall west of the corridor in front of the door, open only at the far north end.
        var scene = await SetUp("KsSquadPlanTestMobBlaster",
            glass: ArmouredGlass,
            squadAt: new Vector2i(-7, 3),
            extra: (entManager, gridUid) =>
            {
                for (var y = -12; y <= 10; y++)
                {
                    SpawnAt(entManager, TestWall, gridUid, -5, y);
                }
            });

        await WaitForWallNavmesh(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            GiveInHand(scene, scene.MemberUid, TimedCharge);
            var now = IoCManager.Resolve<IGameTiming>().CurTime;
            var coordinates = scene.EntManager.GetComponent<TransformComponent>(scene.TargetUid).Coordinates;

            foreach (var memberUid in new[] { scene.LeaderUid, scene.MemberUid })
            {
                scene.EntManager.System<NpcPerceptionSystem>().SetContact(memberUid, scene.TargetUid, new NpcContact(NpcContactState.Lost,
                    now, now, now, coordinates, default, null, Reacted: true, ReactAt: now, ObserverWasMoving: false));
            }

            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid)!;
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Blast), "the test needs a blast");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var planterOrder) && planterOrder.Kind == NpcOrderKind.PlantCharge,
                "the test needs the member planting");

            var transformSystem = scene.EntManager.System<SharedTransformSystem>();
            var site = hunt.Blast!.Charge!.Pick.Site;
            var leaderCoordinates = scene.EntManager.GetComponent<TransformComponent>(scene.LeaderUid).Coordinates;
            Assert.That(leaderCoordinates.TryDistance(scene.EntManager, transformSystem, site.StandCoordinates, out var throughWall) && throughWall < 8f,
                "the test needs the leader near the spot in a straight line");

            var chargeCoordinates = new EntityCoordinates(scene.GridUid, new Vector2(site.Tile.X + 0.5f, site.Tile.Y + 0.5f));

            // The planter leads, the long way round, and the leader with it, behind it: not off ahead to the far side.
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out var order) && order.Kind == NpcOrderKind.Stage,
                "the leader should be coming along");
            Assert.That(order.Coordinates.TryDistance(scene.EntManager, transformSystem, scene.EntManager.GetComponent<TransformComponent>(scene.MemberUid).Coordinates, out var fromPlanter) &&
                fromPlanter < 1f,
                "after the planter, while it has the way round still to go");

            // The planter round the wall and at the charge: the rest are past being in its way at the edge of the blast.
            transformSystem.SetCoordinates(scene.MemberUid, site.StandCoordinates);
            scene.TacticsSystem.UpdateNow();

            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out order) && order.Kind == NpcOrderKind.Stage,
                "the leader should still be coming along");
            var followCoordinates = order.Coordinates;
            Assert.That(followCoordinates.Position.X, Is.GreaterThan(-5f), "to the planter's side of the wall");
            Assert.That(followCoordinates.TryDistance(scene.EntManager, transformSystem, chargeCoordinates, out var fromCharge) && fromCharge >= 7f + order.Range,
                "to the edge of the blast, not into it");
            Assert.That(leaderCoordinates.TryDistance(scene.EntManager, transformSystem, followCoordinates, out var toGo) && toGo > order.Range,
                "near only through a wall is not there yet: an order it is already in range of has it stand still");

            // The charge goes on while it is still on its way.
            hunt.Blast.Charge.Phase = NpcSquadChargePhase.Clearing;
            hunt.Blast.Charge.PlantedAt = now;
            scene.TacticsSystem.UpdateNow();

            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out var clearingOrder) && clearingOrder.Kind == NpcOrderKind.Stage,
                "it should keep coming, not hold where it is");
            Assert.That(clearingOrder.Id, Is.EqualTo(order.Id), "to the same spot, with no replan for it");

            // Round the wall, at the spot it was sent to.
            transformSystem.SetCoordinates(scene.LeaderUid, followCoordinates);
            scene.TacticsSystem.UpdateNow();

            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out order), "it should still have something to do");
            Assert.That(order.Kind, Is.Not.EqualTo(NpcOrderKind.TakeCover), "it is out of the blast already");
            Assert.That(scene.EntManager.GetComponent<TransformComponent>(scene.LeaderUid).Coordinates
                    .TryDistance(scene.EntManager, transformSystem, order.Coordinates, out var roundTheWall) && roundTheWall <= order.Range,
                "once round, it should stop just out of the blast rather than walk up to the charge");
        });
    }

    /// <summary>
    ///     Runs ticks until the floor all round the wall of the tests above is on the navmesh, which the way round to the charge
    ///         is worked out from. On a busy server it can still be missing once the squad has formed.
    /// </summary>
    private async Task WaitForWallNavmesh(Scene scene)
    {
        await WaitForNavmesh(Pair, scene.GridUid, new Vector2i(-12, -12), new Vector2i(-6, 12));
        await WaitForNavmesh(Pair, scene.GridUid, new Vector2i(-12, 11), new Vector2i(12, 12));
        await WaitForNavmesh(Pair, scene.GridUid, new Vector2i(-4, -12), new Vector2i(-2, 10));
    }

    /// <summary>
    ///     A planter is given up on for getting nowhere, not for taking long: getting nearer by the way it has to walk, round a
    ///         wall and away from the charge in a straight line, keeps it going however long it has been at it, and so does being
    ///         busy with its hands. Standing about past <see cref="NpcSquadTacticsSettings.PlanChargeTimeout"/> does not.
    /// </summary>
    [Test]
    public async Task TestAPlanterIsOnlyGivenUpOnForGettingNowhere()
    {
        var scene = await SetUp("KsSquadPlanTestMobBlaster",
            glass: ArmouredGlass,
            squadAt: new Vector2i(-7, 3),
            extra: (entManager, gridUid) =>
            {
                for (var y = -12; y <= 10; y++)
                {
                    SpawnAt(entManager, TestWall, gridUid, -5, y);
                }
            });

        await WaitForWallNavmesh(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            GiveInHand(scene, scene.MemberUid, TimedCharge);
            var now = IoCManager.Resolve<IGameTiming>().CurTime;
            var coordinates = scene.EntManager.GetComponent<TransformComponent>(scene.TargetUid).Coordinates;

            foreach (var memberUid in new[] { scene.LeaderUid, scene.MemberUid })
            {
                scene.EntManager.System<NpcPerceptionSystem>().SetContact(memberUid, scene.TargetUid, new NpcContact(NpcContactState.Lost,
                    now, now, now, coordinates, default, null, Reacted: true, ReactAt: now, ObserverWasMoving: false));
            }

            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid)!;
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Blast), "the test needs a blast");
            var blast = hunt.Blast!;
            var charge = blast.Charge!;
            var timeout = hunt.Settings.PlanChargeTimeout;
            var transformSystem = scene.EntManager.System<SharedTransformSystem>();

            // Long at it, and a step nearer since last time: north, the way round, though further from the charge as the crow flies.
            //      From wherever it is: which of the two is the planter is up to how the squad formed.
            var planterPosition = scene.EntManager.GetComponent<TransformComponent>(scene.MemberUid).LocalPosition;
            var planterTile = new Vector2i((int) MathF.Floor(planterPosition.X), (int) MathF.Floor(planterPosition.Y));
            var nearerTile = planterTile + new Vector2i(0, 1);
            Assert.That(charge.RouteSteps.TryGetValue(nearerTile, out var nearerSteps) && charge.RouteSteps.TryGetValue(planterTile, out var planterSteps) &&
                nearerSteps < planterSteps, "the test needs north to be nearer by the way round");

            blast.StepStartedAt = now - timeout * 5;
            charge.ProgressAt = now - timeout - TimeSpan.FromSeconds(1);
            transformSystem.SetCoordinates(scene.MemberUid, new EntityCoordinates(scene.GridUid, new Vector2(nearerTile.X + 0.5f, nearerTile.Y + 0.5f)));
            scene.TacticsSystem.UpdateNow();
            Assert.That(hunt.Blast, Is.SameAs(blast), "a planter getting nearer is not given up on, however long it has been at it");
            Assert.That(charge.ProgressAt, Is.EqualTo(now), "and getting nearer is getting somewhere");

            // Standing about, busy with its hands: a door on the way, or the charge itself.
            charge.ProgressAt = now - timeout - TimeSpan.FromSeconds(1);
            scene.EntManager.AddComponent<Content.Shared.DoAfter.ActiveDoAfterComponent>(scene.MemberUid);
            scene.TacticsSystem.UpdateNow();
            scene.EntManager.RemoveComponent<Content.Shared.DoAfter.ActiveDoAfterComponent>(scene.MemberUid);
            Assert.That(hunt.Blast, Is.SameAs(blast), "nor is one busy with its hands");

            // Standing about, and nothing more.
            charge.ProgressAt = now - timeout - TimeSpan.FromSeconds(1);
            scene.TacticsSystem.UpdateNow();
            Assert.That(hunt.Blast, Is.Null, "one getting nowhere is");
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Exhausted));
        });
    }

    /// <summary>
    ///     The other way round: the squad walled in and the noise outside, in the open. The floor out there goes on and on, so
    ///         nothing is sealed off from them, but they are from it, and their own wall nearest the noise is what gets blown.
    ///         Then they go after it like any other hunt. They used to search, for spots they could never walk to.
    /// </summary>
    [Test]
    public async Task TestASquadWalledInBlowsItsWayOut()
    {
        var scene = await SetUp("KsSquadPlanTestMobBlaster", glass: null, door: TestWall);
        NpcHunt hunt = default!;

        await Pair.Server.WaitAssertion(() =>
        {
            var transformSystem = scene.EntManager.System<SharedTransformSystem>();
            scene.EntManager.DeleteEntity(scene.TargetUid);
            transformSystem.SetCoordinates(scene.LeaderUid, new EntityCoordinates(scene.GridUid, new Vector2(2.5f, 2.5f)));
            transformSystem.SetCoordinates(scene.MemberUid, new EntityCoordinates(scene.GridUid, new Vector2(2.5f, 3.5f)));
            GiveInHand(scene, scene.MemberUid, TimedCharge);

            scene.TacticsSystem.NoteDisturbance(scene.MemberUid, new EntityCoordinates(scene.GridUid, new Vector2(-5.5f, 3.5f)));
            scene.TacticsSystem.UpdateNow();

            hunt = scene.TacticsSystem.GetHunt(scene.SquadUid)!;
            Assert.That(hunt, Is.Not.Null, "a noise they cannot get to is no noise to just walk to");
            Assert.That(hunt.SealedIn, "it is they who are shut in");
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Blast), "and the charge is the way out");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order) && order.Kind == NpcOrderKind.PlantCharge,
                "the member with it should go to put it on");

            var site = hunt.Blast!.Charge!.Pick.Site;
            Assert.That(site.Tile.X, Is.EqualTo(-1), "on the wall towards the noise");
            Assert.That(site.StandTile.X, Is.InRange(0, 6), "from inside");
            Assert.That(site.StandTile.Y, Is.InRange(0, 5), "from inside");

            // Gone off, and the wall with it.
            hunt.Blast.Charge.Phase = NpcSquadChargePhase.Blasted;
            hunt.Blast.Charge.BlastedAt = IoCManager.Resolve<IGameTiming>().CurTime;
            scene.EntManager.DeleteEntity(site.TargetUid);
        });

        // For the dust to settle, and the floor where the wall was to be floor to walk on.
        await Pair.RunTicksSync(40);

        await Pair.Server.WaitAssertion(() =>
        {
            scene.TacticsSystem.UpdateNow();

            Assert.That(hunt.SealedIn, Is.False, "out now");
            Assert.That(hunt.ShutOut, Is.False);
            Assert.That(hunt.Phase, Is.AnyOf(NpcHuntPhase.Stage, NpcHuntPhase.Search), "and after it");
        });
    }

    /// <summary>
    ///     The same for a plan: a hostile seen through the window of the room they are walled in. Their own wall comes down.
    /// </summary>
    [Test]
    public async Task TestAPlanWalledInBlowsItsWayOut()
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass, door: TestWall);

        await Pair.Server.WaitPost(() =>
        {
            var transformSystem = scene.EntManager.System<SharedTransformSystem>();
            transformSystem.SetCoordinates(scene.TargetUid, new EntityCoordinates(scene.GridUid, new Vector2(-4.5f, 3.5f)));
            transformSystem.SetCoordinates(scene.LeaderUid, new EntityCoordinates(scene.GridUid, new Vector2(3.5f, 2.5f)));
            transformSystem.SetCoordinates(scene.MemberUid, new EntityCoordinates(scene.GridUid, new Vector2(3.5f, 3.5f)));
            GiveInHand(scene, scene.MemberUid, WiredCharge);
            GiveInHand(scene, scene.MemberUid, Activator);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            Assert.That(plan.Steps[..plan.StepCount], Is.EqualTo(new[] { NpcSquadAction.BlowWall, NpcSquadAction.Enter }), $"{plan.Reason} {plan.Facts}");
            Assert.That(plan.Hunt!.SealedIn);

            var site = plan.WallCharge!.Value.Site;
            Assert.That(scene.EntManager.GetComponent<MetaDataComponent>(site.TargetUid).EntityPrototype!.ID, Is.EqualTo(TestWall));
            Assert.That(site.Tile.X, Is.EqualTo(-1), "their wall towards the hostile");
            Assert.That(site.StandTile.X, Is.InRange(0, 6), "from inside");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order) && order.Kind == NpcOrderKind.PlantCharge);
        });
    }

    /// <summary>
    ///     A squad walled in with nothing to blow its way out holds as near as it can get, not search for what it cannot reach.
    /// </summary>
    [Test]
    public async Task TestASquadWalledInWithNoChargeHolds()
    {
        var scene = await SetUp("KsSquadPlanTestMobBlaster", glass: null, door: TestWall);

        await Pair.Server.WaitAssertion(() =>
        {
            var transformSystem = scene.EntManager.System<SharedTransformSystem>();
            scene.EntManager.DeleteEntity(scene.TargetUid);
            transformSystem.SetCoordinates(scene.LeaderUid, new EntityCoordinates(scene.GridUid, new Vector2(4.5f, 2.5f)));
            transformSystem.SetCoordinates(scene.MemberUid, new EntityCoordinates(scene.GridUid, new Vector2(4.5f, 3.5f)));

            scene.TacticsSystem.NoteDisturbance(scene.MemberUid, new EntityCoordinates(scene.GridUid, new Vector2(-5.5f, 3.5f)));
            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid);
            Assert.That(hunt, Is.Not.Null);
            Assert.That(hunt!.Phase, Is.EqualTo(NpcHuntPhase.Exhausted));
            Assert.That(hunt.ShutOut);
            Assert.That(hunt.ShutOutCoordinates, Is.Not.Null, "somewhere to wait");

            var spot = hunt.ShutOutCoordinates!.Value.Position;
            Assert.That(spot.X, Is.LessThan(1f), "inside, by the wall nearest the noise");
            Assert.That(spot.X, Is.GreaterThan(0f));
        });
    }

    /// <summary>
    ///     With no way in at all, a wall of the room makes one. A wall: the window is nearer to the member with the charge, and a
    ///         window, or a machine, or anything else that happens to be in the way is not what this is for.
    /// </summary>
    [Test]
    public async Task TestAWallMakesAWayIn()
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass, door: TestWall);

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.GlassMemberUid, WiredCharge);
            GiveInHand(scene, scene.GlassMemberUid, Activator);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene)!;
            Assert.That(plan.Steps[..plan.StepCount], Is.EqualTo(new[] { NpcSquadAction.BlowWall, NpcSquadAction.Enter }), plan.Facts.ToString());

            var site = plan.WallCharge!.Value.Site;
            Assert.That(site.IsDoor, Is.False);
            Assert.That(scene.EntManager.GetComponent<MetaDataComponent>(site.TargetUid).EntityPrototype!.ID, Is.EqualTo(TestWall));
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.GlassMemberUid, out var order), "it should be on its way");
            Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.PlantCharge));
        });
    }

    /// <summary>
    ///     Nobody opens the station to space to get at someone. Here the one way to the room's wall is a strip of floor with space
    ///         right beside it.
    /// </summary>
    [Test]
    public async Task TestNothingNearTheHullIsBlown()
    {
        var scene = await SetUpByTheHull();

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(GetPlan(scene)?.WallCharge, Is.Null);
            Assert.That(GetPlan(scene)?.State, Is.EqualTo(NpcSquadPlanState.Cooldown));
        });
    }

    /// <summary>
    ///     Outside is what the map's atmosphere is. On a planet with air, a hole by the edge of the base lets in no worse than a
    ///         door would. On a map whose atmosphere is space, it is the same as no atmosphere at all.
    /// </summary>
    [TestCase(false, true)]
    [TestCase(true, false)]
    public async Task TestOutsideIsWhatTheMapHas(bool space, bool blown)
    {
        var moles = new float[Content.Shared.Atmos.Atmospherics.AdjustedNumberOfGases];
        moles[(int) Content.Shared.Atmos.Gas.Oxygen] = 21.824779f;
        moles[(int) Content.Shared.Atmos.Gas.Nitrogen] = 82.10312f;

        var scene = await SetUpByTheHull((space, new Content.Shared.Atmos.GasMixture(moles, Content.Shared.Atmos.Atmospherics.T20C)));

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene);
            Assert.That(plan?.WallCharge != null, Is.EqualTo(blown), plan?.Reason);
        });
    }

    /// <summary>
    ///     A room whose only way in is a wall, reached by a strip of floor with nothing beside it, from a member carrying a wired
    ///         charge, with the map's atmosphere outside if given. Steps once.
    /// </summary>
    private async Task<Scene> SetUpByTheHull((bool Space, Content.Shared.Atmos.GasMixture Mixture)? mapAtmosphere = null)
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass, door: TestWall);

        await Pair.Server.WaitPost(() =>
        {
            var mapSystem = scene.EntManager.System<Robust.Shared.GameObjects.SharedMapSystem>();
            var gridComponent = scene.EntManager.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(scene.GridUid);

            if (mapAtmosphere is { } atmosphere)
            {
                scene.EntManager.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>().SetMapAtmosphere(
                    scene.EntManager.GetComponent<TransformComponent>(scene.GridUid).MapUid!.Value, atmosphere.Space, atmosphere.Mixture);
            }

            for (var x = -2; x <= 8; x++)
            {
                for (var y = -2; y <= 7; y++)
                {
                    var ring = x is -2 or 8 || y is -2 or 7;

                    if (ring && !(x == -2 && y is 2 or 3))
                        mapSystem.SetTile(scene.GridUid, gridComponent, new Vector2i(x, y), Robust.Shared.Map.Tile.Empty);
                }
            }

            GiveInHand(scene, scene.MemberUid, WiredCharge);
            GiveInHand(scene, scene.MemberUid, Activator);
        });

        await Pair.RunTicksSync(30);
        await Step(scene);
        return scene;
    }

    /// <summary>
    ///     Nor to get at someone in a room with nothing to breathe, or from one: what is on either side comes through.
    /// </summary>
    [Test]
    public async Task TestNothingIsBlownIntoAVacuum()
    {
        var scene = await SetUp("KsSquadPlanTestMobAirMinded", glass: ArmouredGlass);

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.MemberUid, WiredCharge);
            GiveInHand(scene, scene.MemberUid, Activator);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(GetPlan(scene)?.DoorCharge, Is.Null);
            Assert.That(GetPlan(scene)?.WallCharge, Is.Null);
        });
    }

    /// <summary>
    ///     A door a plan cannot blow, as what it holds back is worse than what it keeps them from.
    /// </summary>
    [Test]
    public async Task TestAFirelockHoldingBackDangerIsNotBlown()
    {
        var scene = await SetUp(SteadyMob, glass: ArmouredGlass, door: "KsSquadPlanTestFirelock");

        await Pair.Server.WaitPost(() =>
        {
            GiveInHand(scene, scene.MemberUid, TimedCharge);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() => Assert.That(GetPlan(scene)?.DoorCharge, Is.Null));
    }

    /// <summary>
    ///     A door just through a wall can be a long way round. A plan goes by the walk and not how far off it looks.
    /// </summary>
    [Test]
    public async Task TestAWalkCostsTheWalk()
    {
        var scene = await SetUp(glass: ArmouredGlass, doorAt: new Vector2i(7, 3));

        await Pair.Server.WaitPost(() => GiveInHand(scene, scene.MemberUid, TestJawsOfLife));
        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene);
            Assert.That(plan?.State, Is.EqualTo(NpcSquadPlanState.Running));

            // Twelve tiles off in a straight line, and a good deal further round the room.
            Assert.That(plan!.Cost, Is.GreaterThan(12f / 4.5f + 6f + 2f + 1f));
        });
    }

    /// <summary>
    ///     With no way of getting at it, what happens depends on whether it can get at them. A laser goes through the glass
    ///         that stops their bullets, so they get out of its sight. Behind glass that stops its gun too, there is no hurry.
    /// </summary>
    [TestCase(LaserHostile, true)]
    [TestCase(ArmedHostile, false)]
    public async Task TestCoverFromWhatItCannotHit(string hostile, bool takesCover)
    {
        var scene = await SetUp(glass: Glass, hostile: hostile);

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = GetPlan(scene);
            Assert.That(plan?.State, Is.EqualTo(takesCover ? NpcSquadPlanState.Running : NpcSquadPlanState.Cooldown));

            if (!takesCover)
                return;

            Assert.That(plan!.Steps[..plan.StepCount], Is.EqualTo(new[] { NpcSquadAction.TakeCover }));
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order), "it should be told to get out of there");
            Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.TakeCover));
            Assert.That(order.OverCombat);
        });
    }

    /// <summary>
    ///     A squad too hurt to fight breaks contact, even with a shot, if it is one that does: the plan is not only for when
    ///         it is stuck. A healthy one fights on, and so does one that never pulls back.
    /// </summary>
    [TestCase("KsSquadPlanTestMobWithdrawing", 90, true)]
    [TestCase("KsSquadPlanTestMobWithdrawing", 10, false)]
    [TestCase(PlannerMob, 90, false)]
    public async Task TestHurtSquadBreaksContact(string mob, int damage, bool withdraws)
    {
        var scene = await SetUp(mob: mob, glass: null, door: TestWall, hostileAt: new Vector2i(-3, 5));

        await Pair.Server.WaitAssertion(() =>
        {
            var protoManager = Pair.Server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>();
            var blunt = new Content.Shared.Damage.DamageSpecifier(protoManager.Index(BluntDamage), damage);
            var damageSystem = scene.EntManager.System<Content.Shared.Damage.Systems.DamageableSystem>();

            damageSystem.SetDamage(scene.LeaderUid, blunt);
            damageSystem.SetDamage(scene.MemberUid, blunt);

            var squadSystem = scene.EntManager.System<NpcSquadSystem>();
            Assert.That(squadSystem.GetHealthFraction(scene.MemberUid), Is.EqualTo(1f - damage / 100f).Within(0.01f), "the test needs them hurt");

            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(GetPlan(scene)?.Goal == NpcSquadFact.OutOfSight, Is.EqualTo(withdraws));
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order), Is.EqualTo(withdraws));

            if (withdraws)
                Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.TakeCover));
        });
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

    private static void Hurt(Scene scene, EntityUid mobUid, int damage)
    {
        var protoManager = IoCManager.Resolve<Robust.Shared.Prototypes.IPrototypeManager>();
        var blunt = new Content.Shared.Damage.DamageSpecifier(protoManager.Index(BluntDamage), damage);
        scene.EntManager.System<Content.Shared.Damage.Systems.DamageableSystem>().SetDamage(mobUid, blunt);
    }

    /// <summary>
    ///     A mask connected for internals, and a tank in hand to go with it unless <paramref name="tank"/> is false.
    /// </summary>
    private static void Mask(Scene scene, EntityUid mobUid, bool tank = true)
    {
        var maskUid = scene.EntManager.SpawnEntity("KsSquadPlanTestMask", scene.EntManager.GetComponent<TransformComponent>(mobUid).Coordinates);
        var internalsComponent = scene.EntManager.GetComponent<Content.Shared.Body.Components.InternalsComponent>(mobUid);
        scene.EntManager.System<InternalsSystem>().ConnectBreathTool((mobUid, internalsComponent), maskUid);

        if (tank)
            GiveInHand(scene, mobUid, "KsSquadPlanTestTank");
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
        Vector2i? gap = null,
        string hostile = NanoTrasenMob,
        Vector2i? doorAt = null,
        Vector2i? squadAt = null,
        Action<IEntityManager, EntityUid>? extra = null)
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

                    if (new Vector2i(x, y) == (doorAt ?? new Vector2i(-1, 2)))
                    {
                        doorUid = door == SecurityDoor
                            ? SpawnPoweredDoorAt(entManager, door, gridUid, x, y)
                            : SpawnAt(entManager, door, gridUid, x, y);
                    }
                    else if (x == -1 && y == 3)
                    {
                        glassUid = SpawnAt(entManager, glass ?? TestWall, gridUid, x, y);
                    }
                    else
                    {
                        SpawnAt(entManager, TestWall, gridUid, x, y);
                    }
                }
            }

            extra?.Invoke(entManager, gridUid);

            var squadTile = squadAt ?? new Vector2i(-3, 3);
            firstUid = SpawnAt(entManager, mob, gridUid, squadTile.X, squadTile.Y);
            secondUid = SpawnAt(entManager, mob, gridUid, squadTile.X, squadTile.Y - 1);

            var hostileTile = hostileAt ?? new Vector2i(3, 3);
            targetUid = SpawnAt(entManager, hostile, gridUid, hostileTile.X, hostileTile.Y);
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
