#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Execution;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Grenades;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Interactions;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Orders;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Squad;
using Content.Server._KS14.NPC.Grenades;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Suppression;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Recovery;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat.Ranged;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators.Interactions;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.NPC;
using Content.Shared.Tools.Systems;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Plans through the real operative HTN root, so a branch that is unreachable in practice - behind an
///         earlier branch that always plans - fails a test instead of passing unnoticed. The operative HTN lives
///         in the private _KsModule submodule, so these are ignored when it is absent.
///
///     Also covers sensor data forcing an immediate replan, which of the perception- and order-driven branches
///         (search, orders, callouts) may interrupt which, and an order change cutting the running order short.
/// </summary>
public sealed class KsOperativeHtnRootTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // Not static: the YAML linter validates every static ProtoId field against the client's prototypes too, and
    //      the client has no HTN prototypes at all, so a static one fails the lint.
    private readonly ProtoId<HTNCompoundPrototype> _operativeRoot = "KsOperativeCombatCompound";
    private readonly ProtoId<HTNCompoundPrototype> _operativeNtRoot = "KsOperativeNtWrapperCombatCompound";

    private const string HandsMob = "KsOperativeRootTestMobWithHands";
    private const string FakerMob = "KsOperativeRootTestMobFaker";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsOperativeRootTestMobWithHands
  components:
  - type: Hands
  - type: CombatMode

- type: npcMeter
  id: KsOperativeRootTestCaution
  max: 100
  decayPerSecond: 0

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsOperativeRootTestMobFaker
  components:
  - type: NpcPlayDead
    meter: KsOperativeRootTestCaution
    threshold: 95
    chance: 1

- type: entity
  parent: KsOperativeRootTestMobWithHands
  id: KsOperativeRootTestMobRecovering
  components:
  - type: NpcRecovery

# Somewhere to carry things, and none of the things only some NPCs do.
- type: entity
  parent: KsOperativeRootTestMobWithHands
  id: KsOperativeRootTestMobCarrying
  components:
  - type: Inventory
    templateId: human
  - type: ContainerContainer

- type: entity
  parent: KsOperativeRootTestMobCarrying
  id: KsOperativeRootTestMobAllOut
  components:
  - type: NpcRecovery
  - type: NpcExecutioner
  - type: NpcGrenadeThrower
  - type: NpcGrenadePusher
  - type: NpcBuddyAid
  - type: NpcSuppressor

- type: entity
  id: KsOperativeRootTestGrenade
  components:
  - type: Item
  - type: TimerTrigger
    delay: 1000

- type: entity
  parent: CombatKnife
  id: KsOperativeRootTestKnife

- type: entity
  parent: EmergencyMedipen
  id: KsOperativeRootTestMedipen
  components:
  - type: NpcHealingItem

# A small blast, primed for longer than any test runs.
- type: entity
  id: KsOperativeRootTestExplosive
  components:
  - type: TimerTrigger
    delay: 1000
  - type: Explosive
    explosionType: Default
    totalIntensity: 10
    intensitySlope: 3
    maxIntensity: 10

# Primed, with nothing to say what it does.
- type: entity
  id: KsOperativeRootTestPrimed
  components:
  - type: TimerTrigger
    delay: 1000

- type: entity
  parent: KsOperativeRootTestMobWithHands
  id: KsOperativeRootTestMobWalking
  components:
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier

- type: entity
  parent: KsOperativeRootTestMobCarrying
  id: KsOperativeRootTestMobWalkingCarrying
  components:
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier

# Shut, and seen through, like a glass firelock, without one's need for air on the map.
- type: entity
  id: KsOperativeRootTestGlassDoor
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
        - GlassAirlockLayer
  - type: Door
    state: Closed
    bumpOpen: false
    clickOpen: false

- type: entity
  parent: KsOperativeRootTestMobWithHands
  id: KsOperativeRootTestMobForfeit
  components:
  - type: Flammable
    canResistFire: true
    damage:
      types:
        Heat: 1.5
  - type: StandingState
  - type: StatusEffectContainer
  - type: Stamina
  - type: Cuffable
  - type: Implanted

- type: entity
  parent: KsOperativeRootTestMobWalking
  id: KsOperativeRootTestMobPlanner
  components:
  - type: DoAfter
  - type: NpcSquadMember
    tactics:
      planning: true
      planGrace: 1s
      stageTimeout: 3s
      planChargeMinPressure: 0 # a test map has no air in it

# Somewhere to carry a charge, and in no mood to wait.
- type: entity
  parent: KsOperativeRootTestMobPlanner
  id: KsOperativeRootTestMobCharging
  components:
  - type: Inventory
    templateId: human
  - type: ContainerContainer
  - type: NpcSquadMember
    tactics:
      planHoldCost: 100

# An ordinary charge, as a squad might be handed, on a timer short enough to wait for.
- type: entity
  parent: C4
  id: KsOperativeRootTestTimedCharge
  components:
  - type: TimerTrigger
    delay: 3

# Says what it has to say, and can be put on the floor.
- type: entity
  parent: KsOperativeRootTestMobWalking
  id: KsOperativeRootTestMobSpeaking
  components:
  - type: Speech
  - type: StandingState
  - type: StatusEffectContainer
  - type: Crawler
  - type: DoAfter
  - type: Inventory
    templateId: human
  - type: ContainerContainer

- type: entity
  parent: KsOperativeRootTestMobFaker
  id: KsOperativeRootTestMobFireShy
  components:
  - type: NpcAvoidsTileDanger
    dangers: Fire

- type: entity
  id: KsOperativeRootTestFire
  components:
  - type: NpcTileDanger
    dangers: Fire
";

    private const string RecoveringMob = "KsOperativeRootTestMobRecovering";
    private const string CarryingMob = "KsOperativeRootTestMobCarrying";
    private const string WalkingCarryingMob = "KsOperativeRootTestMobWalkingCarrying";
    private const string AllOutMob = "KsOperativeRootTestMobAllOut";
    private const string TestGrenade = "KsOperativeRootTestGrenade";
    private const string TestKnife = "KsOperativeRootTestKnife";
    private const string TestMedipen = "KsOperativeRootTestMedipen";
    private const string TestExplosive = "KsOperativeRootTestExplosive";
    private const string TestPrimed = "KsOperativeRootTestPrimed";
    private const string FireShyMob = "KsOperativeRootTestMobFireShy";
    private const string TestFire = "KsOperativeRootTestFire";
    private const string ForfeitMob = "KsOperativeRootTestMobForfeit";
    private const string SpeakingMob = "KsOperativeRootTestMobSpeaking";

    private static readonly ProtoId<Content.Shared.Damage.Prototypes.DamageTypePrototype> BluntDamage = "Blunt";

    private const string CombatMarker = "OpInCombat";
    private const string CombatTimeKey = "TimeOfLastCombat";
    private const string ThreatKey = "LastKnownThreatCoordinates";

    /// <summary>
    ///     In combat, in a room, shortly after a fight, the operative covers the room.
    /// </summary>
    [Test]
    public async Task TestInCombatOperativeHoldsRoom()
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true);

        Assert.That(plan.Tasks.Any(task => task.Operator is SquadCoverOperator),
            $"expected a squad hold, got: {Describe(plan)}");
    }

    /// <summary>
    ///     In combat, but with nothing seen or fought for a long time, the operative stands down.
    /// </summary>
    [Test]
    public async Task TestQuietOperativeDisengages()
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: false);

        Assert.That(plan.Tasks.Any(task => task.Operator is RemoveVirtualMarkerOperator),
            $"expected a stand-down, got: {Describe(plan)}");
    }

    /// <summary>
    ///     An operative that has just heard a disturbance - in combat, with the threat's position known, but no
    ///         fight on record yet - goes to look into it rather than standing straight back down.
    /// </summary>
    [Test]
    public async Task TestDisturbanceIsInvestigated()
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: false, knownThreat: true);

        Assert.That(plan.Tasks.Any(task => task.Operator is SquadCoverOperator),
            $"expected the operative to go and hold, got: {Describe(plan)}");
    }

    /// <summary>
    ///     An operative not in combat, whose squad is, falls in with it.
    /// </summary>
    [Test]
    public async Task TestIdleSquadmateJoinsEngagedSquad()
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: false, inCombat: false, squadThreat: true);

        Assert.That(plan.Tasks.Any(task => task.Operator is EnsureVirtualMarkerOperator),
            $"expected the operative to join its squad in combat, got: {Describe(plan)}");
    }

    /// <summary>
    ///     Pending sensor data takes priority over a hold, so a disturbance is handled straight away.
    /// </summary>
    [Test]
    public async Task TestPendingSensorDataIsHandledBeforeHold()
    {
        var plan = await PlanOperative(pendingSensorData: true, recentlyFought: true);

        Assert.That(plan.Tasks.Any(task => task.Operator is HandleSensorsOperator),
            $"expected the sensor data to be pulled in, got: {Describe(plan)}");
    }

    /// <summary>
    ///     A hostile seen hiding in a locker is searched for: the operative goes and opens it.
    /// </summary>
    [Test]
    public async Task TestConcealedHostileIsSearched()
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (entManager, mobUid, gridUid) => StageHidden(entManager, mobUid, gridUid, welded: false));

        Assert.That(plan.Tasks.Any(task => task.Operator is OpenEntityStorageOperator),
            $"expected the locker to be opened, got: {Describe(plan)}");
    }

    /// <summary>
    ///     A locker that cannot be opened - welded shut - is covered instead.
    /// </summary>
    [Test]
    public async Task TestWeldedHidingPlaceIsCovered()
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (entManager, mobUid, gridUid) => StageHidden(entManager, mobUid, gridUid, welded: true));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks.Any(task => task.Operator is OpenEntityStorageOperator), Is.False,
                $"a welded locker cannot be opened, got: {Describe(plan)}");
            Assert.That(plan.Tasks.Any(task => task.Operator is TacticalPositionOperator),
                $"expected the locker to be covered, got: {Describe(plan)}");
        });
    }

    /// <summary>
    ///     Each kind of order plans its own branch of the order compound, through the real root: nothing above it
    ///         gets in the way of an order in an ordinary in-combat lull. Each ends waiting for the next order, and
    ///         only a locker search opens anything.
    /// </summary>
    [TestCase(NpcOrderKind.Investigate)]
    [TestCase(NpcOrderKind.Watch)]
    [TestCase(NpcOrderKind.Stage)]
    [TestCase(NpcOrderKind.Breach)]
    [TestCase(NpcOrderKind.Enter)]
    [TestCase(NpcOrderKind.Search)]
    [TestCase(NpcOrderKind.HoldArea)]
    [TestCase(NpcOrderKind.Regroup)]
    public async Task TestOrderIsCarriedOut(NpcOrderKind kind)
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true,
            stage: (entManager, mobUid, gridUid) => StageOrder(entManager, mobUid, gridUid, kind));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks.Any(task => task.Operator is GetOrderOperator), $"expected the order to be read, got: {Describe(plan)}");
            Assert.That(plan.Tasks.Any(task => task.Operator is WaitForOrderChangeOperator), $"expected the order to be carried out, got: {Describe(plan)}");
            Assert.That(plan.Tasks.Any(task => task.Operator is OpenEntityStorageOperator), Is.False, $"only a locker search opens anything, got: {Describe(plan)}");
        });
    }

    /// <summary>
    ///     A breach order forces the door it names: the plan includes forcing it, and still ends waiting for the next
    ///         order.
    /// </summary>
    [Test]
    public async Task TestBreachOrderForcesTheDoor()
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true,
            stage: (entManager, mobUid, gridUid) => StageOrder(entManager, mobUid, gridUid, NpcOrderKind.Breach));

        Assert.That(plan.Tasks.Any(task => task.Operator is Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Doors.BreachDoorOperator),
            $"expected the door to be forced, got: {Describe(plan)}");
    }

    /// <summary>
    ///     An operative a door has just refused says so: above orders, so a hunt does not swallow the line, but below a
    ///         fight, which it never interrupts.
    /// </summary>
    [Test]
    public async Task TestRefusedDoorIsCalledOut()
    {
        static void Refuse(IEntityManager entManager, EntityUid mobUid, EntityUid gridUid)
        {
            var doorUid = SpawnAt(entManager, "Airlock", gridUid, 2, 2);
            entManager.System<Content.Server._KS14.NPC.Doors.NpcDoorSystem>().ReportRefused(mobUid, doorUid);
        }

        var refused = await PlanOperative(pendingSensorData: false, recentlyFought: true, stage: Refuse);
        var refusedWithOrder = await PlanOperative(pendingSensorData: false, recentlyFought: true,
            stage: (entManager, mobUid, gridUid) =>
            {
                Refuse(entManager, mobUid, gridUid);
                StageOrder(entManager, mobUid, gridUid, NpcOrderKind.Stage);
            });
        var order = await PlanOperative(pendingSensorData: false, recentlyFought: true,
            stage: (entManager, mobUid, gridUid) => StageOrder(entManager, mobUid, gridUid, NpcOrderKind.Stage));
        var search = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (entManager, mobUid, gridUid) => StageHidden(entManager, mobUid, gridUid, welded: false));

        Assert.Multiple(() =>
        {
            Assert.That(refused.Tasks.Any(task => task.Operator is Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Doors.AcknowledgeDoorRefusedOperator),
                $"expected the refusal to be called out, got: {Describe(refused)}");
            Assert.That(refusedWithOrder.Tasks.Any(task => task.Operator is Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Doors.AcknowledgeDoorRefusedOperator),
                $"with an order too, the callout should come first, got: {Describe(refusedWithOrder)}");
            Assert.That(Replaces(refused, order), "a refusal should interrupt an order, to be said at the time");
            Assert.That(Replaces(refused, search), Is.False, "but not a search, let alone a fight");
        });
    }

    /// <summary>
    ///     An operative that has just killed a hostile says so, even mid-fight with another: the line interrupts the
    ///         fight for a moment, rather than coming after it, by which time it would mean nothing.
    /// </summary>
    [Test]
    public async Task TestTargetDownIsCalledOut()
    {
        static void Kill(IEntityManager entManager, EntityUid mobUid, EntityUid gridUid)
        {
            var hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 3, 3);
            var now = Robust.Shared.IoC.IoCManager.Resolve<IGameTiming>().CurTime;
            entManager.System<NpcPerceptionSystem>().SetContact(mobUid, hostileUid, new NpcContact(NpcContactState.Lost,
                now, now, now, entManager.GetComponent<TransformComponent>(hostileUid).Coordinates, default, null, Reacted: true));
            entManager.System<Content.Shared.Mobs.Systems.MobStateSystem>().ChangeMobState(hostileUid, Content.Shared.Mobs.MobState.Dead, origin: mobUid);
        }

        var downMidFight = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageVisibleHostile(entManager, mobUid, gridUid);
                Kill(entManager, mobUid, gridUid);
            });
        var fight = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageVisibleHostile(entManager, mobUid, gridUid);
            });

        Assert.Multiple(() =>
        {
            Assert.That(downMidFight.Tasks.Any(task => task.Operator is Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Perception.AcknowledgeTargetDownOperator),
                $"expected the kill to be called out, got: {Describe(downMidFight)}");
            Assert.That(Replaces(downMidFight, fight), "the callout should interrupt the fight, to be said at the time");
        });
    }

    /// <summary>
    ///     An order to search a locker ends in opening it.
    /// </summary>
    [Test]
    public async Task TestLockerSearchOrderOpensIt()
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                var lockerUid = SpawnAt(entManager, "ClosetSteelBase", gridUid, 2, 2);
                var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
                tacticsSystem.UpdatesPaused = true;
                tacticsSystem.IssueOrder(mobUid,
                    NpcOrderKind.Search,
                    entManager.GetComponent<TransformComponent>(lockerUid).Coordinates,
                    lockerUid);
            });

        Assert.That(plan.Tasks.Any(task => task.Operator is OpenEntityStorageOperator), $"expected the locker to be opened, got: {Describe(plan)}");
    }

    /// <summary>
    ///     An operative playing dead does nothing else: its branch comes before healing and combat, so not even a target in
    ///         reach gets it up early.
    /// </summary>
    [Test]
    public async Task TestPlayingDeadComesFirst()
    {
        var plan = await PlanOperative(pendingSensorData: true, recentlyFought: true, mobPrototype: FakerMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                entManager.System<Content.Server._KS14.NPC.Meters.NpcMeterSystem>().Set(mobUid, "KsOperativeRootTestCaution", 100f);
                var playDeadSystem = entManager.System<Content.Server._KS14.NPC.PlayDead.NpcPlayDeadSystem>();
                playDeadSystem.UpdateNow(mobUid);
                Assert.That(playDeadSystem.IsPlayingDead(mobUid), "a rattled operative on its own should be playing dead");
            });

        Assert.That(plan.Tasks.Single().Operator, Is.TypeOf<Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.PlayDead.PlayDeadOperator>(),
            $"expected only playing dead, got: {Describe(plan)}");
    }

    /// <summary>
    ///     An operative that finds itself in a fire gets out before anything else, even playing dead. Only what it avoids
    ///         counts.
    /// </summary>
    [TestCase(FireShyMob, true)]
    [TestCase(SyndicateMob, false)]
    public async Task TestFireUnderItComesFirst(string mobPrototype, bool leaves)
    {
        var plan = await PlanOperative(pendingSensorData: true, recentlyFought: true, mobPrototype: mobPrototype,
            stage: (entManager, mobUid, gridUid) =>
            {
                var tile = entManager.GetComponent<TransformComponent>(mobUid).LocalPosition.Floored();
                SpawnAt(entManager, TestFire, gridUid, tile.X, tile.Y);

                entManager.System<PathfindingSystem>().RefreshTileDangersNow();

                if (!leaves)
                    return;

                entManager.System<Content.Server._KS14.NPC.Meters.NpcMeterSystem>().Set(mobUid, "KsOperativeRootTestCaution", 100f);
                var playDeadSystem = entManager.System<Content.Server._KS14.NPC.PlayDead.NpcPlayDeadSystem>();
                playDeadSystem.UpdateNow(mobUid);
                Assert.That(playDeadSystem.IsPlayingDead(mobUid), "the test needs it playing dead");
            });

        var pickedAnExit = plan.Tasks.Any(task => task.Operator is TacticalPositionOperator { Key: "DangerExit" });

        Assert.Multiple(() =>
        {
            Assert.That(pickedAnExit, Is.EqualTo(leaves), $"got: {Describe(plan)}");

            if (leaves)
                Assert.That(plan.BranchTraversalRecord[0], Is.EqualTo(1), "getting out of a fire should come first, with being stunned the only thing before it");
        });
    }

    /// <summary>
    ///     A live grenade in sight and in reach is run from, in combat or out of it - and so is anything else primed and
    ///         ticking, like a C4 charge, which is no hand grenade, or something that does not say what it does.
    /// </summary>
    [TestCase(true, TestExplosive)]
    [TestCase(false, TestExplosive)]
    [TestCase(true, TestPrimed)]
    public async Task TestLiveGrenadeIsFled(bool inCombat, string explosive)
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: inCombat, inCombat: inCombat,
            stage: (entManager, _, gridUid) =>
            {
                var grenadeUid = SpawnAt(entManager, explosive, gridUid, 3, 3);
                Assert.That(entManager.System<Content.Shared.Trigger.Systems.TriggerSystem>().ActivateTimerTrigger(grenadeUid),
                    "the grenade should be primed");
            });

        Assert.That(plan.Tasks.Any(task => task.Operator is UtilityOperator { Prototype.Id: "KsOperativeNearbyGrenades" }),
            $"expected the grenade to be fled, got: {Describe(plan)}");
    }

    /// <summary>
    ///     A live grenade in plain view that cannot reach the operative is left alone: it is not run from, the fight is not
    ///         dropped for it, and it does not make anyone jumpy.
    /// </summary>
    [Test]
    public async Task TestLiveGrenadeOutOfReachIsNotFled()
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true,
            stage: (entManager, mobUid, gridUid) =>
            {
                // The far end of the room, in plain view, and much further than this small blast goes.
                var grenadeUid = SpawnAt(entManager, TestExplosive, gridUid, 0, 3);
                Assert.That(entManager.System<Content.Shared.Trigger.Systems.TriggerSystem>().ActivateTimerTrigger(grenadeUid),
                    "the grenade should be primed");

                var explosiveComponent = entManager.GetComponent<Content.Shared.Explosion.Components.ExplosiveComponent>(grenadeUid);
                var reach = entManager.System<Content.Server.Explosion.EntitySystems.ExplosionSystem>().IntensityToRadius(
                    explosiveComponent.TotalIntensity, explosiveComponent.IntensitySlope, explosiveComponent.MaxIntensity);
                var transformSystem = entManager.System<SharedTransformSystem>();
                var distance = (transformSystem.GetWorldPosition(grenadeUid) - transformSystem.GetWorldPosition(mobUid)).Length();

                Assert.That(distance, Is.GreaterThan(reach * 2f), "the test needs the grenade well out of reach");
            });

        Assert.That(plan.Tasks.Any(task => task.Operator is UtilityOperator { Prototype.Id: "KsOperativeNearbyGrenades" }), Is.False,
            $"a grenade that cannot reach it is no reason to run, got: {Describe(plan)}");
    }

    /// <summary>
    ///     Waiting for meds to work, an operative stays put: its orders wait.
    /// </summary>
    [Test]
    public async Task TestRecoveringHoldsInsteadOfFollowingOrders()
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: RecoveringMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageOrder(entManager, mobUid, gridUid, NpcOrderKind.Search);
                entManager.System<NpcRecoverySystem>().StartRecovery(mobUid);
            });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks.Any(task => task.Operator is StaticWaitOperator), $"expected it to hold, got: {Describe(plan)}");
            Assert.That(plan.Tasks.Any(task => task.Operator is GetOrderOperator), Is.False, $"orders should wait, got: {Describe(plan)}");
        });
    }

    /// <summary>
    ///     Nor does it go to back up a squadmate.
    /// </summary>
    [Test]
    public async Task TestRecoveringDoesNotAnswerCallouts()
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: RecoveringMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageCalledOutRoundTheCorner(entManager, mobUid, gridUid);
                entManager.System<NpcRecoverySystem>().StartRecovery(mobUid);
            });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks.Any(task => task.Operator is StaticWaitOperator), $"expected it to hold, got: {Describe(plan)}");
            Assert.That(plan.Tasks.Any(task => task.Operator is MoveToOperator or TacticalPositionOperator), Is.False,
                $"it should stay where it is, got: {Describe(plan)}");
        });
    }

    /// <summary>
    ///     It shoots what it sees, and backs away from it, but never closes in. Getting its health back ends that at once.
    /// </summary>
    [Test]
    public async Task TestRecoveringShootsButDoesNotCloseIn()
    {
        var recovering = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: RecoveringMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageVisibleHostile(entManager, mobUid, gridUid);
                StageRecoveringAndSettled(entManager, mobUid);
            });

        var recovered = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: RecoveringMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageVisibleHostile(entManager, mobUid, gridUid);
            });

        Assert.Multiple(() =>
        {
            Assert.That(recovering.Tasks.Any(task => task.Operator is GunOperator), $"it should shoot, got: {Describe(recovering)}");
            Assert.That(recovering.Tasks.Any(task => task.Operator is JukeOperator), $"and back away, got: {Describe(recovering)}");
            Assert.That(recovering.Tasks.Any(task => task.Operator is MoveToOperator or TacticalPositionOperator), Is.False,
                $"but go nowhere, got: {Describe(recovering)}");
            Assert.That(recovered.Tasks.Any(task => task.Operator is MoveToOperator), $"recovered, it should close in again, got: {Describe(recovered)}");
            Assert.That(Replaces(recovered, recovering), "and getting its health back should end the wait at once");
        });
    }

    /// <summary>
    ///     A hostile finding it there means the spot is no good: it moves, breaking off the shooting to do it.
    /// </summary>
    [Test]
    public async Task TestRecoveringFallsBackFromAHostile()
    {
        var shooting = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: RecoveringMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageVisibleHostile(entManager, mobUid, gridUid);
                StageRecoveringAndSettled(entManager, mobUid);
            });

        var fallingBack = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: RecoveringMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageVisibleHostile(entManager, mobUid, gridUid);
                entManager.System<NpcRecoverySystem>().StartRecovery(mobUid);
            });

        Assert.Multiple(() =>
        {
            Assert.That(fallingBack.Tasks.Any(task => task.Operator is TacticalPositionOperator), $"expected it to look for a new spot, got: {Describe(fallingBack)}");
            Assert.That(Replaces(fallingBack, shooting), "falling back should break off the shooting");
        });
    }

    /// <summary>
    ///     An NPC that does this walks up to a hostile left down and executes it, rather than shooting it where it lies. Every
    ///         other NPC goes on shooting.
    /// </summary>
    [TestCase(AllOutMob, true)]
    [TestCase(CarryingMob, false)]
    public async Task TestDownedHostileIsExecutedByThoseThatDoThat(string mobPrototype, bool executes)
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: mobPrototype,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageCarried(entManager, mobUid, TestKnife);
                StageDownedHostile(entManager, mobUid, gridUid, 1, 3);
            });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks.Any(task => task.Operator is ExecuteOperator), Is.EqualTo(executes), Describe(plan));
            Assert.That(plan.Tasks.Any(task => task.Operator is GunOperator), Is.Not.EqualTo(executes), Describe(plan));
        });
    }

    /// <summary>
    ///     Something still on its feet comes first, and drops an execution under way.
    /// </summary>
    [Test]
    public async Task TestHostileOnItsFeetInterruptsAnExecution()
    {
        var execution = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: AllOutMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageCarried(entManager, mobUid, TestKnife);
                StageDownedHostile(entManager, mobUid, gridUid, 1, 3);
            });

        var fight = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: AllOutMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageCarried(entManager, mobUid, TestKnife);
                StageDownedHostile(entManager, mobUid, gridUid, 1, 3);
                StageVisibleHostile(entManager, mobUid, gridUid);
            });

        Assert.Multiple(() =>
        {
            Assert.That(fight.Tasks.Any(task => task.Operator is GunOperator), $"expected it to shoot, got: {Describe(fight)}");
            Assert.That(fight.Tasks.Any(task => task.Operator is ExecuteOperator), Is.False, $"not to walk over, got: {Describe(fight)}");
            Assert.That(Replaces(fight, execution), "a hostile on its feet should interrupt an execution");
        });
    }

    /// <summary>
    ///     A hostile down where nobody can get to it still has to die: as they leave those for walking up to, nothing else
    ///         would ever shoot it.
    /// </summary>
    [Test]
    public async Task TestUnreachableDownedHostileIsShot()
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: AllOutMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageCarried(entManager, mobUid, TestKnife);
                StageDownedHostile(entManager, mobUid, gridUid, 0, 0);
            }, withPen: true);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks.Any(task => task.Operator is GunOperator), $"expected it to be shot, got: {Describe(plan)}");
            Assert.That(plan.Tasks.Any(task => task.Operator is ExecuteOperator), Is.False, Describe(plan));
        });
    }

    /// <summary>
    ///     A grenade goes after a hostile out of sight, from those that do that.
    /// </summary>
    [TestCase(AllOutMob, true)]
    [TestCase(CarryingMob, false)]
    public async Task TestHiddenHostileGetsAGrenade(string mobPrototype, bool throws)
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: mobPrototype,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageCarried(entManager, mobUid, TestGrenade);
                StageContact(entManager, mobUid, gridUid, NpcContactState.Lost, new System.Numerics.Vector2(0.5f, 0.5f));
            });

        Assert.That(plan.Tasks.Any(task => task.Operator is ThrowGrenadeOperator), Is.EqualTo(throws), Describe(plan));
    }

    /// <summary>
    ///     Memory is not the same as news. A hostile long gone is remembered for a good while yet, but a grenade only goes
    ///         after one lost a moment ago: going by the memory alone, they lobbed one into a room with nobody in it.
    /// </summary>
    [TestCase(NpcContactState.Lost, 1, true)]
    [TestCase(NpcContactState.Lost, 30, false)]
    [TestCase(NpcContactState.Reported, 1, true)]
    [TestCase(NpcContactState.Reported, 30, false)]
    public async Task TestOnlyAFreshContactGetsAGrenade(NpcContactState state, int secondsAgo, bool throws)
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: AllOutMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageCarried(entManager, mobUid, TestGrenade);
                StageContact(entManager, mobUid, gridUid, state, new System.Numerics.Vector2(0.5f, 0.5f), System.TimeSpan.FromSeconds(secondsAgo));
            });

        Assert.That(plan.Tasks.Any(task => task.Operator is ThrowGrenadeOperator), Is.EqualTo(throws), Describe(plan));
    }

    /// <summary>
    ///     Glass can be seen through, and a grenade thrown at it lands at the thrower's feet. So one in sight behind glass gets
    ///         none, nor does one lost behind it.
    /// </summary>
    [TestCase(NpcContactState.Visible, false, true)]
    [TestCase(NpcContactState.Visible, true, false)]
    [TestCase(NpcContactState.Lost, false, true)]
    [TestCase(NpcContactState.Lost, true, false)]
    public async Task TestHostileBehindGlassGetsNoGrenade(NpcContactState state, bool glass, bool throws)
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: AllOutMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageCarried(entManager, mobUid, TestGrenade);

                // In line with the operative, at the far side of its room.
                var hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 0, 3);
                entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(hostileUid);

                if (glass)
                    SpawnAt(entManager, "Window", gridUid, 2, 3);

                var now = Robust.Shared.IoC.IoCManager.Resolve<IGameTiming>().CurTime;
                entManager.System<NpcPerceptionSystem>().SetContact(mobUid, hostileUid, new NpcContact(state, now, now, now,
                    entManager.GetComponent<TransformComponent>(hostileUid).Coordinates, default, null, Reacted: true));
            });

        Assert.That(plan.Tasks.Any(task => task.Operator is ThrowGrenadeOperator), Is.EqualTo(throws), Describe(plan));
    }

    /// <summary>
    ///     A grenade goes into the fight too, from those that do that, interrupting the shooting for the throw. Every other
    ///         NPC goes on shooting.
    /// </summary>
    [TestCase(AllOutMob, true)]
    [TestCase(CarryingMob, false)]
    public async Task TestHostileInTheFightGetsAGrenade(string mobPrototype, bool throws)
    {
        var fight = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: mobPrototype,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageVisibleHostile(entManager, mobUid, gridUid);
            });

        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: mobPrototype,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageCarried(entManager, mobUid, TestGrenade);
                StageVisibleHostile(entManager, mobUid, gridUid);
            });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks.Any(task => task.Operator is ThrowGrenadeOperator), Is.EqualTo(throws), Describe(plan));
            Assert.That(Replaces(plan, fight), Is.EqualTo(throws), $"the throw should interrupt the shooting: {Describe(plan)}");
        });
    }

    /// <summary>
    ///     A hostile already down is not worth one.
    /// </summary>
    [Test]
    public async Task TestDownedHostileGetsNoGrenade()
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: AllOutMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageCarried(entManager, mobUid, TestGrenade);
                StageDownedHostile(entManager, mobUid, gridUid, 1, 3);
            });

        Assert.That(plan.Tasks.Any(task => task.Operator is ThrowGrenadeOperator), Is.False, Describe(plan));
    }

    /// <summary>
    ///     Too far to shoot from here and no firing spot due, an NPC that sticks to cover makes for a wall on the way. Every
    ///         other NPC walks straight at the target.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestClosingInGoesFromWallToWall(bool coverOnly)
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageVisibleHostile(entManager, mobUid, gridUid);

                var blackboard = entManager.GetComponent<HTNComponent>(mobUid).Blackboard;
                blackboard.SetValue("RangedRange", 1f);
                blackboard.SetValue("RepositionDueAt", Robust.Shared.IoC.IoCManager.Resolve<IGameTiming>().CurTime + System.TimeSpan.FromHours(1));

                if (coverOnly)
                    blackboard.SetValue("CoverOnly", true);
            });

        Assert.That(plan.Tasks.Any(task => task.Operator is TacticalPositionOperator { Key: "BoundPosition" }), Is.EqualTo(coverOnly), Describe(plan));
    }

    /// <summary>
    ///     Anywhere not recognisably a room, with no threat to hide from, an NPC that sticks to cover holds at a wall. Every other
    ///         NPC takes a spot at random, a new one every hold.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestHoldOnOpenGroundGoesToAWall(bool coverOnly)
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true,
            stage: (entManager, mobUid, gridUid) =>
            {
                entManager.System<SharedTransformSystem>().SetCoordinates(mobUid, new EntityCoordinates(gridUid, new System.Numerics.Vector2(9.5f, 3.5f)));

                if (coverOnly)
                    entManager.GetComponent<HTNComponent>(mobUid).Blackboard.SetValue("CoverOnly", true);
            });

        var search = plan.Tasks.Select(task => task.Operator).OfType<TacticalPositionOperator>().FirstOrDefault();

        Assert.That(search, Is.Not.Null, Describe(plan));
        Assert.That(search!.WallCurve is not null, Is.EqualTo(coverOnly), Describe(plan));
    }

    /// <summary>
    ///     A hostile that has only just ducked out of sight may get a burst after it, from those that do that. If the
    ///         chance does not come up the moment passes: the plan still starts the cooldown, or the next plan would just
    ///         roll again.
    /// </summary>
    [TestCase(AllOutMob, 100f, true)]
    [TestCase(AllOutMob, 0f, false)]
    [TestCase(CarryingMob, 100f, false)]
    public async Task TestHostileThatJustDuckedIsSuppressed(string mobPrototype, float chance, bool suppresses)
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: mobPrototype,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageContact(entManager, mobUid, gridUid, NpcContactState.Lost);
                entManager.GetComponent<HTNComponent>(mobUid).Blackboard.SetValue("SuppressionChance", chance);
            });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks.Any(task => task.Operator is SuppressOperator), Is.EqualTo(suppresses), Describe(plan));
            Assert.That(plan.Tasks.Any(task => task.Operator is CooldownOperator), Is.EqualTo(mobPrototype == AllOutMob), Describe(plan));
        });
    }

    /// <summary>
    ///     A grenade from its squad having gone off, those that do that go in on it, ahead of whatever order they had. Not
    ///         while waiting for meds to work, and a hostile in sight comes first.
    /// </summary>
    [TestCase(AllOutMob, false, false, true)]
    [TestCase(CarryingMob, false, false, false)]
    [TestCase(AllOutMob, true, false, false)]
    [TestCase(AllOutMob, false, true, false)]
    public async Task TestGrenadeBlastIsPushedOn(string mobPrototype, bool recovering, bool hostileInSight, bool pushes)
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: mobPrototype,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StagePush(entManager, mobUid, gridUid);

                if (recovering)
                    StageRecoveringAndSettled(entManager, mobUid);

                if (hostileInSight)
                    StageVisibleHostile(entManager, mobUid, gridUid);
            });

        Assert.That(plan.Tasks.Any(task => task.Operator is EndGrenadePushOperator), Is.EqualTo(pushes), Describe(plan));
    }

    [Test]
    public async Task TestGrenadeBlastInterruptsAnOrder()
    {
        var order = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: AllOutMob,
            stage: (entManager, mobUid, gridUid) => StageOrder(entManager, mobUid, gridUid, NpcOrderKind.Stage));
        var push = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: AllOutMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageOrder(entManager, mobUid, gridUid, NpcOrderKind.Stage);
                StagePush(entManager, mobUid, gridUid);
            });

        Assert.That(Replaces(push, order), "going in should interrupt an order, so that nobody waits for the next phase of a hunt");
    }

    private static void StagePush(IEntityManager entManager, EntityUid mobUid, EntityUid gridUid)
    {
        entManager.GetComponent<HTNComponent>(mobUid).Blackboard.SetValue("GrenadePushRange", 1f);
        entManager.System<NpcGrenadeSystem>().StartPush(mobUid, new EntityCoordinates(gridUid, new System.Numerics.Vector2(1.5f, 1.5f)));
    }

    /// <summary>
    ///     A squadmate that is down gets the meds, from those that do that.
    /// </summary>
    [TestCase(AllOutMob, true)]
    [TestCase(CarryingMob, false)]
    public async Task TestDownedSquadmateIsSeenTo(string mobPrototype, bool seesToIt)
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: mobPrototype, withSquadmate: true,
            stage: (entManager, mobUid, _) =>
            {
                StageHands(entManager, mobUid);
                StageCarried(entManager, mobUid, TestMedipen);

                var squadmates = new HashSet<EntityUid>();
                entManager.System<NpcSquadSystem>().GetSquadmates(mobUid, squadmates);
                Assert.That(squadmates, Has.Count.EqualTo(1), "the test needs the two in a squad");

                entManager.System<Content.Shared.Mobs.Systems.MobStateSystem>().ChangeMobState(squadmates.Single(), Content.Shared.Mobs.MobState.Critical);
            });

        Assert.That(plan.Tasks.Any(task => task.Operator is ClaimTargetOperator), Is.EqualTo(seesToIt), Describe(plan));
    }

    /// <summary>
    ///     With nothing in sight and a magazine half gone, it swaps it where it stands, if it is the sort to.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestHalfEmptyMagazineIsToppedUp(bool tacticalReload)
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: CarryingMob,
            stage: (entManager, mobUid, _) =>
            {
                StageArmed(entManager, mobUid);
                entManager.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().AddHand(mobUid, "left", Content.Shared.Hands.Components.HandLocation.Left);
                StageHalfEmptyMagazine(entManager, mobUid);
                StageCarried(entManager, mobUid, "MagazinePistol");

                if (tacticalReload)
                    entManager.GetComponent<HTNComponent>(mobUid).Blackboard.SetValue("TacticalReload", true);
            });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks.Any(task => task.Operator is SwapToFreeHandOperator), Is.EqualTo(tacticalReload), Describe(plan));

            if (tacticalReload)
                Assert.That(plan.Tasks.Any(task => task.Operator is TacticalPositionOperator or MoveToOperator), Is.False, $"it should not go anywhere, got: {Describe(plan)}");
        });
    }

    /// <summary>
    ///     Live: a reload under fire is done from cover, and the retreat to it ends with the reload. Whatever waits for a
    ///         retreat to end - backing up a squadmate above all - can go ahead then, though it is still walking there. It
    ///         used to wait for it to get there: up to ten tiles off, out of the fight, with nothing planned the whole way.
    /// </summary>
    [Test]
    public async Task TestRetreatToReloadEndsWithTheReload()
    {
        // The hostile behind a wall right across the grid: nothing in sight to fight, which would take over the steering
        //      and clear the retreat by itself.
        var (entManager, mobUid, hostileUid) = await SetUpBehindGlass(_ => "WallSolid", mob: WalkingCarryingMob,
            stage: blackboard =>
            {
                blackboard.SetValue("RetreatRange", 10f);
                blackboard.SetValue("ReloadActDelay", 0f);
            });

        await Pair.Server.WaitPost(() =>
        {
            var htnComponent = entManager.GetComponent<HTNComponent>(mobUid);
            htnComponent.Blackboard.SetValue(ThreatKey, entManager.GetComponent<TransformComponent>(hostileUid).Coordinates);

            entManager.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().AddHand(mobUid, "left", Content.Shared.Hands.Components.HandLocation.Left);
            StageEmptyGun(entManager, mobUid);
            StageCarried(entManager, mobUid, "MagazinePistol");
            entManager.System<HTNSystem>().Replan(htnComponent);
        });

        var reloaded = false;
        EntityUid gunUid = default;

        await Pair.Server.WaitPost(() =>
        {
            Assert.That(entManager.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().TryGetHeldItem(mobUid, "right", out var heldUid));
            gunUid = heldUid!.Value;
        });

        for (var i = 0; i < 300 && !reloaded; i++)
        {
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() => reloaded = GetAmmo(entManager, gunUid) > 0);
        }

        Assert.That(reloaded, "the test needs it to have reloaded");

        await Pair.RunTicksSync(10);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<Content.Server.NPC.Components.NPCSteeringComponent>(mobUid, out var steeringComponent) &&
                steeringComponent.Status == Content.Server.NPC.Components.SteeringStatus.Moving,
                "the test needs it still walking to the cover it went for to reload");

            var htnComponent = entManager.GetComponent<HTNComponent>(mobUid);
            Assert.That(htnComponent.Blackboard.ContainsKey("RetreatTargetPathfind"), Is.False,
                $"reloaded, it should no longer be retreating, but is doing: {(htnComponent.Plan is { } plan ? Describe(plan) : "nothing")}");
        });
    }

    /// <summary>
    ///     Empties the gun the operative holds, chamber and all.
    /// </summary>
    private static void StageEmptyGun(IEntityManager entManager, EntityUid mobUid)
    {
        var handsSystem = entManager.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
        var containerSystem = entManager.System<Robust.Shared.Containers.SharedContainerSystem>();
        Assert.That(handsSystem.TryGetHeldItem(mobUid, "right", out var gunUid));

        Assert.That(containerSystem.TryGetContainer(gunUid!.Value, "gun_magazine", out var magazineContainer));
        var magazineUid = magazineContainer!.ContainedEntities[0];
        var ballisticComponent = entManager.GetComponent<Content.Shared.Weapons.Ranged.Components.BallisticAmmoProviderComponent>(magazineUid);
        entManager.System<Content.Shared.Weapons.Ranged.Systems.SharedGunSystem>().SetBallisticUnspawned((magazineUid, ballisticComponent), 0);

        if (containerSystem.TryGetContainer(gunUid.Value, "gun_chamber", out var chamberContainer))
        {
            foreach (var roundUid in chamberContainer.ContainedEntities.ToArray())
            {
                entManager.DeleteEntity(roundUid);
            }
        }

        Assert.That(GetAmmo(entManager, gunUid.Value), Is.Zero, "the test needs the gun empty");
    }

    /// <summary>
    ///     As <see cref="TestLiveGrenadeIsFled"/>, but live: an operative already carrying out a plan (holding its room)
    ///         drops it for the grenade within a replan or two.
    /// </summary>
    [Test]
    public async Task TestLiveGrenadeInterruptsRunningPlan()
    {
        var protoManager = Pair.Server.ResolveDependency<IPrototypeManager>();
        if (!protoManager.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var (entManager, mobUid) = await SetUpOperativeInRoom();
        var htnSystem = entManager.System<HTNSystem>();
        var npcSystem = entManager.System<NPCSystem>();
        var timing = Pair.Server.ResolveDependency<IGameTiming>();
        EntityUid grenadeUid = default;

        await Pair.Server.WaitPost(() =>
        {
            var htnComponent = entManager.GetComponent<HTNComponent>(mobUid);
            htnComponent.RootTask = new HTNCompoundTask { Task = _operativeRoot };
            htnComponent.Blackboard.SetValue(EnsureVirtualMarkerOperator.MarkerSet, new HashSet<string> { CombatMarker });
            htnComponent.Blackboard.SetValue(CombatTimeKey, timing.CurTime);
            htnSystem.SetHTNEnabled((mobUid, htnComponent), true);
            npcSystem.WakeNPC(mobUid, htnComponent);
        });

        await Pair.RunTicksSync(60);

        await Pair.Server.WaitPost(() =>
        {
            Assert.That(entManager.GetComponent<HTNComponent>(mobUid).Plan, Is.Not.Null, "the operative should be busy with something");

            var gridUid = entManager.GetComponent<TransformComponent>(mobUid).ParentUid;
            grenadeUid = SpawnAt(entManager, TestExplosive, gridUid, 3, 3);
            entManager.System<Content.Shared.Trigger.Systems.TriggerSystem>().ActivateTimerTrigger(grenadeUid);
        });

        await Pair.RunTicksSync(45);

        await Pair.Server.WaitAssertion(() =>
        {
            var htnComponent = entManager.GetComponent<HTNComponent>(mobUid);
            var describe = htnComponent.Plan is { } plan ? Describe(plan) : "nothing";
            entManager.DeleteEntity(grenadeUid);

            Assert.That(htnComponent.Blackboard.ContainsKey("GrenadeTarget"),
                $"the operative should have run from the grenade, but is doing: {describe}");
        });
    }

    /// <summary>
    ///     Live: a hostile seen through a window that stops its gun is gone round to, however far that is, for the shot it
    ///         cannot get from here. It used to shuffle from spot to spot beside the glass, each of which could see the hostile.
    /// </summary>
    [Test]
    public async Task TestHostileBehindGlassIsGoneRoundTo()
    {
        var (entManager, mobUid, _) = await SetUpBehindGlass(y => y is >= -4 and <= 4 ? "Window" : y == 11 ? null : "WallSolid");
        var position = System.Numerics.Vector2.Zero;

        for (var i = 0; i < 900 && position.X < 4f; i += 15)
        {
            await Pair.RunTicksSync(15);
            await Pair.Server.WaitPost(() => position = entManager.GetComponent<TransformComponent>(mobUid).LocalPosition);
        }

        Assert.That(position.X, Is.GreaterThanOrEqualTo(4f), "it never went round");
    }

    /// <summary>
    ///     Live: with no way round either, there is no shot to wait for, and it gets on with something else. It used to stand
    ///         there behind its gun for as long as it could see the hostile.
    /// </summary>
    [TestCase("Window", "Window")]
    [TestCase("KsOperativeRootTestGlassDoor", "WallSolid")]
    public async Task TestHostileSealedBehindGlassIsNotWaitedOn(string glass, string rest)
    {
        var (entManager, mobUid, _) = await SetUpBehindGlass(y => y == 0 ? glass : rest);

        await Pair.RunTicksSync(90);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = entManager.GetComponent<HTNComponent>(mobUid).Plan;
            Assert.That(plan?.Tasks.Any(task => task.Operator is Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat.Ranged.GunOperator), Is.Not.True,
                plan is null ? "no plan" : $"it is waiting on a shot it cannot take: {Describe(plan)}");
        });
    }

    /// <summary>
    ///     Live: glass is nothing to a laser. It stays this side and fires, rather than walking round to do so, whether it has
    ///         a spot to fight from or is only closing in.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestLaserShootsThroughGlass(bool spotDue)
    {
        var timing = Pair.Server.ResolveDependency<IGameTiming>();
        var (entManager, mobUid, _) = await SetUpBehindGlass(y => y is >= -4 and <= 4 ? "Window" : y == 11 ? null : "WallSolid", "WeaponLaserCarbine",
            blackboard =>
            {
                if (!spotDue)
                    blackboard.SetValue("RepositionDueAt", timing.CurTime + System.TimeSpan.FromHours(1));
            });

        var ammoAtFirst = 0;
        EntityUid gunUid = default;

        await Pair.Server.WaitPost(() =>
        {
            Assert.That(entManager.System<Content.Shared.Weapons.Ranged.Systems.SharedGunSystem>().TryGetGun(mobUid, out var gunEntity), "it should be holding its gun");
            gunUid = gunEntity.Owner;
            ammoAtFirst = GetAmmo(entManager, gunUid);
        });

        await Pair.RunTicksSync(150);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(GetAmmo(entManager, gunUid), Is.LessThan(ammoAtFirst), "it should have been firing");

            var position = entManager.GetComponent<TransformComponent>(mobUid).LocalPosition;
            Assert.That(position.X < 3f && position.Y < 7f, $"it should not be going round, but is at {position}");
        });
    }

    private static int GetAmmo(IEntityManager entManager, EntityUid gunUid)
    {
        var ammoEvent = new Content.Shared.Weapons.Ranged.Events.GetAmmoCountEvent();
        entManager.EventBus.RaiseLocalEvent(gunUid, ref ammoEvent);
        return ammoEvent.Count;
    }

    /// <summary>
    ///     An NT operative blows itself up rather than be taken: beaten down, or cuffed. Not for going down of its own accord
    ///         to put itself out, nor for a moment's stun, which only hands a player an easy way to set it off.
    /// </summary>
    [TestCase("nothing", false)]
    [TestCase("rolling", false)]
    [TestCase("brief stun", false)]
    [TestCase("stamina crit", true)]
    [TestCase("cuffed", true)]
    public async Task TestForfeitsOnlyWhenTaken(string state, bool forfeits)
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: ForfeitMob, root: _operativeNtRoot,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageHands(entManager, mobUid);

                var implantSystem = entManager.System<Content.Shared.Implants.SharedSubdermalImplantSystem>();
                Assert.That(implantSystem.AddImplant(mobUid, "KsOperativeNtVoluntaryMicroBombImplant"), Is.Not.Null, "the test needs the implant in it");

                switch (state)
                {
                    case "rolling":
                        var flammableSystem = entManager.System<Content.Server.Atmos.EntitySystems.FlammableSystem>();
                        var flammableComponent = entManager.GetComponent<Content.Shared.Atmos.Components.FlammableComponent>(mobUid);
                        flammableSystem.AdjustFireStacks(mobUid, 2f, flammableComponent, ignite: true);
                        flammableSystem.Resist(mobUid, flammableComponent, keepHold: true);
                        Assert.That(flammableComponent.Resisting, "the test needs it rolling");
                        break;
                    case "brief stun":
                        entManager.System<Content.Shared.Stunnable.SharedStunSystem>().TryUpdateParalyzeDuration(mobUid, System.TimeSpan.FromSeconds(1));
                        break;
                    case "stamina crit":
                        entManager.System<Content.Shared.Damage.Systems.SharedStaminaSystem>().TakeStaminaDamage(mobUid, 1000f);
                        break;
                    case "cuffed":
                        var cuffsUid = entManager.SpawnEntity("Handcuffs", entManager.GetComponent<TransformComponent>(mobUid).Coordinates);
                        Assert.That(entManager.System<Content.Shared.Cuffs.SharedCuffableSystem>().TryAddNewCuffs(mobUid, mobUid, cuffsUid), "the test needs it cuffed");
                        break;
                }
            });

        Assert.That(plan.Tasks.Any(task => task.Operator is Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Actions.DoActionOperator),
            Is.EqualTo(forfeits),
            Describe(plan));
    }

    /// <summary>
    ///     Live: a stunned operative mid-fight says nothing. Nearly everything it could plan starts with a line and goes on to
    ///         something it cannot do, so with nothing to stop it, it says the line, fails and plans again, for as long as
    ///         it lasts. Knocked down is not stunned: it can crawl and use its hands, so it may speak, but never over and over.
    /// </summary>
    [TestCase("stunned")]
    [TestCase("knocked down")]
    public async Task TestOperativeOnTheFloorDoesNotRepeatItself(string state)
    {
        var (entManager, mobUid, _) = await SetUpBehindGlass(_ => null, mob: SpeakingMob);
        var speechProbeSystem = entManager.System<KsNpcSpeechProbeSystem>();
        var stunSystem = entManager.System<Content.Shared.Stunnable.SharedStunSystem>();

        await Pair.RunTicksSync(90); // into the fight

        await Pair.Server.WaitPost(() =>
        {
            // Something to want to do about it, which starts with saying so.
            var protoManager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var blunt = new Content.Shared.Damage.DamageSpecifier(protoManager.Index(BluntDamage), 40);
            entManager.System<Content.Shared.Damage.Systems.DamageableSystem>().SetDamage(mobUid, blunt);
            entManager.GetComponent<HTNComponent>(mobUid).Blackboard.SetValue("HealDamageThreshold", 10f);
            StageCarried(entManager, mobUid, TestMedipen);

            speechProbeSystem.Lines.Clear();

            if (state == "stunned")
                Assert.That(stunSystem.TryUpdateParalyzeDuration(mobUid, System.TimeSpan.FromSeconds(6), drop: false), "the test needs it stunned");
            else
                Assert.That(stunSystem.TryKnockdown(mobUid, System.TimeSpan.FromSeconds(6), drop: false, force: true), "the test needs it down");
        });

        await Pair.RunTicksSync(150); // 5s, and it lasts 6

        await Pair.Server.WaitAssertion(() =>
        {
            var lines = speechProbeSystem.Lines.Where(line => line.Source == mobUid).Select(line => line.Message).ToList();

            if (state == "stunned")
                Assert.That(lines, Is.Empty);
            else
                Assert.That(lines, Is.Unique);
        });
    }

    /// <summary>
    ///     A plan's order comes before fighting, or an operative with a gun in its hand would never get to it. An ordinary
    ///         order does not: a hostile in sight is shot first.
    /// </summary>
    [TestCase(true, true)]
    [TestCase(false, false)]
    public async Task TestPlanOrderComesBeforeFighting(bool overCombat, bool ordered)
    {
        var fighting = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageVisibleHostile(entManager, mobUid, gridUid);
            });

        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageVisibleHostile(entManager, mobUid, gridUid);

                var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
                tacticsSystem.UpdatesPaused = true;
                tacticsSystem.IssueOrder(mobUid, NpcOrderKind.Stage, new EntityCoordinates(gridUid, new System.Numerics.Vector2(2.5f, 2.5f)), overCombat: overCombat);
            });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks.Any(task => task.Operator is GetOrderOperator), Is.EqualTo(ordered), Describe(plan));
            Assert.That(Replaces(plan, fighting), Is.EqualTo(ordered), "it has to be able to break off the shooting");
        });
    }

    /// <summary>
    ///     Live, the whole thing through the real root: a squad that can see a hostile through a window in a sealed room, with
    ///         one of them carrying a way through its locked door, goes and forces it, and gets a shot.
    /// </summary>
    [Test]
    public async Task TestSquadForcesItsWayToAHostileBehindGlass()
    {
        var carried = false;

        var (entManager, gunUids, _, doorUid, _, _) = await SetUpSquadOutsideGlassRoom("ReinforcedWindow", "AirlockSecurityLocked",
            (stagedEntManager, mobUid) =>
            {
                if (carried)
                    return;

                carried = true;

                var handsSystem = stagedEntManager.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
                handsSystem.AddHand(mobUid, "left", Content.Shared.Hands.Components.HandLocation.Left);
                var toolUid = stagedEntManager.SpawnEntity(TestJawsOfLife, stagedEntManager.GetComponent<TransformComponent>(mobUid).Coordinates);
                Assert.That(handsSystem.TryPickup(mobUid, toolUid, "left"), "the test needs it carrying the tool");
            });

        var ammoAtFirst = 0;
        await Pair.Server.WaitPost(() => ammoAtFirst = gunUids.Sum(gunUid => GetAmmo(entManager, gunUid)));

        var doorForced = false;

        for (var i = 0; i < 1800 && !doorForced; i += 15)
        {
            await Pair.RunTicksSync(15);
            await Pair.Server.WaitPost(() =>
                doorForced = entManager.GetComponent<Content.Shared.Doors.Components.DoorComponent>(doorUid).State is
                    Content.Shared.Doors.Components.DoorState.Open or Content.Shared.Doors.Components.DoorState.Opening);
        }

        Assert.That(doorForced, "nobody forced the door");

        var fired = false;

        for (var i = 0; i < 900 && !fired; i += 15)
        {
            await Pair.RunTicksSync(15);
            await Pair.Server.WaitPost(() => fired = gunUids.Sum(gunUid => GetAmmo(entManager, gunUid)) < ammoAtFirst);
        }

        Assert.That(fired, "through the door, somebody should have opened fire");
    }

    /// <summary>
    ///     Live, the longest plan there is: a squad behind glass, an armed hostile inside, and among them jaws of life and a
    ///         flashbang or a grenade. It forces the door and puts it through. Without it the first half of this is
    ///         <see cref="TestSquadForcesItsWayToAHostileBehindGlass"/>.
    /// </summary>
    [TestCase("GrenadeFlashBang")]
    [TestCase("KsGrenadeCompact")]
    public async Task TestSquadSoftensUpARoomItForcedItsWayInto(string grenade)
    {
        var carried = false;
        EntityUid grenadeUid = default;

        var room = await SetUpSquadOutsideGlassRoom("ReinforcedWindow", "AirlockSecurityLocked",
            (stagedEntManager, mobUid) =>
            {
                if (carried)
                    return;

                carried = true;

                var handsSystem = stagedEntManager.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
                var coordinates = stagedEntManager.GetComponent<TransformComponent>(mobUid).Coordinates;

                handsSystem.AddHand(mobUid, "left", Content.Shared.Hands.Components.HandLocation.Left);
                Assert.That(handsSystem.TryPickup(mobUid, stagedEntManager.SpawnEntity(TestJawsOfLife, coordinates), "left"), "the test needs it carrying the tool");

                handsSystem.AddHand(mobUid, "belt", Content.Shared.Hands.Components.HandLocation.Middle);
                grenadeUid = stagedEntManager.SpawnEntity(grenade, coordinates);
                Assert.That(handsSystem.TryPickup(mobUid, grenadeUid, "belt"), "the test needs it carrying the grenade");
            },
            hostile: "KsSquadPlanTestArmedHostile");

        var entManager = room.EntManager;
        var thrown = false;

        for (var i = 0; i < 2100 && !thrown; i += 15)
        {
            await Pair.RunTicksSync(15);
            await Pair.Server.WaitPost(() =>
                thrown = entManager.Deleted(grenadeUid) || entManager.HasComponent<NpcThrownGrenadeComponent>(grenadeUid));
        }

        Assert.That(thrown, "it never went in");

        // Nobody on their side should be the worse for it, by the time it has gone off.
        await Pair.RunTicksSync(150);

        await Pair.Server.WaitAssertion(() =>
        {
            foreach (var mobUid in room.MobUids)
            {
                Assert.That(entManager.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsIncapacitated(mobUid), Is.False, "nobody should have been caught in it");
            }
        });
    }

    /// <summary>
    ///     Live: behind glass their bullets cannot pass, with a laser on them through it and no way in, they get out of its
    ///         sight instead of standing there being shot.
    /// </summary>
    [Test]
    public async Task TestSquadTakesCoverFromALaserThroughGlass()
    {
        var room = await SetUpSquadOutsideGlassRoom("ReinforcedWindow", "WallSolid", hostile: "KsSquadPlanTestLaserHostile");
        var entManager = room.EntManager;
        var lineOfSightSystem = entManager.System<NpcLineOfSightSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var hidden = false;

        for (var i = 0; i < 600 && !hidden; i += 15)
        {
            await Pair.RunTicksSync(15);
            await Pair.Server.WaitPost(() =>
            {
                hidden = true;

                foreach (var mobUid in room.MobUids)
                {
                    hidden &= !lineOfSightSystem.InLineOfSight(transformSystem.GetMapCoordinates(mobUid), transformSystem.GetMapCoordinates(room.HostileUid), 20f);
                }
            });
        }

        Assert.That(hidden, "they should all have got out of its sight");
    }

    /// <summary>
    ///     Live, with real charges through the real root: somebody walks up with a charge and sticks it on, everyone gets clear,
    ///         it goes off and what it was on is gone, nobody on their side is the worse for it, and they get their shot. By a
    ///         wire, on a door nothing else opens and on a wall of a room with no way in at all, and by a timer.
    /// </summary>
    [TestCase("AirlockSecurityLocked", "KsWireEndBreachingCharge")]
    [TestCase("WallSolid", "KsWireEndBreachingCharge")]
    [TestCase("AirlockSecurityLocked", "KsOperativeRootTestTimedCharge")]
    public async Task TestSquadBlowsItsWayIn(string door, string charge)
    {
        var carried = false;
        EntityUid chargeUid = default;

        var room = await SetUpSquadOutsideGlassRoom("ReinforcedWindow", door,
            (stagedEntManager, mobUid) =>
            {
                if (carried)
                    return;

                carried = true;
                stagedEntManager.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().AddHand(mobUid, "left", Content.Shared.Hands.Components.HandLocation.Left);
                chargeUid = StageCarried(stagedEntManager, mobUid, charge);

                if (charge == "KsWireEndBreachingCharge")
                    StageCarried(stagedEntManager, mobUid, "KsWireStart", "pocket2");
            },
            mob: "KsOperativeRootTestMobCharging");

        var entManager = room.EntManager;
        var ammoAtFirst = 0;
        await Pair.Server.WaitPost(() => ammoAtFirst = room.GunUids.Sum(gunUid => GetAmmo(entManager, gunUid)));

        var blown = false;

        for (var i = 0; i < 1800 && !blown; i += 15)
        {
            await Pair.RunTicksSync(15);
            await Pair.Server.WaitPost(() => blown = entManager.Deleted(chargeUid));
        }

        Assert.That(blown, "the charge never went off");

        await Pair.RunTicksSync(60);

        await Pair.Server.WaitAssertion(() =>
        {
            var mobStateSystem = entManager.System<Content.Shared.Mobs.Systems.MobStateSystem>();

            foreach (var mobUid in room.MobUids)
            {
                Assert.That(mobStateSystem.IsIncapacitated(mobUid), Is.False, "nobody should have been caught in it");
            }

            if (room.DoorUid.IsValid())
                Assert.That(entManager.Deleted(room.DoorUid), "the door should be gone");
        });

        var fired = false;

        for (var i = 0; i < 900 && !fired; i += 15)
        {
            await Pair.RunTicksSync(15);
            await Pair.Server.WaitPost(() => fired = room.GunUids.Sum(gunUid => GetAmmo(entManager, gunUid)) < ammoAtFirst);
        }

        Assert.That(fired, "through the hole, somebody should have opened fire");
    }

    /// <summary>
    ///     Live, with real operatives: something happens in a room they cannot reach, shut off by a wall or a bolted door. They go
    ///         as near as they can get and cover it, however bold they are, rather than sending one a few steps towards it
    ///         and then standing there.
    /// </summary>
    [TestCase("WallSolid", "KsRandomHumanoidSpawnerOperativeRedacted")]
    [TestCase("bolted", "KsRandomHumanoidSpawnerOperativeRedacted")]
    [TestCase("WallSolid", "KsRandomHumanoidSpawnerOperativeSynArmedPistol")]
    public async Task TestCutOffSquadGoesAsNearAsItCan(string door, string spawner)
    {
        var (entManager, gridUid, doorUid, mobs) = await SetUpOperativesOutsideRoom(door, spawner, charges: false);
        var startX = new Dictionary<EntityUid, float>();

        await Pair.Server.WaitPost(() =>
        {
            foreach (var mobUid in mobs)
            {
                startX[mobUid] = entManager.GetComponent<TransformComponent>(mobUid).LocalPosition.X;
                StageDisturbance(entManager, mobUid, gridUid);
            }
        });

        await Pair.RunTicksSync(300);

        await Pair.Server.WaitAssertion(() =>
        {
            foreach (var mobUid in mobs)
            {
                var position = entManager.GetComponent<TransformComponent>(mobUid).LocalPosition;
                Assert.That(position.X, Is.GreaterThan(startX[mobUid] + 2f), "they should have gone up to it");
                Assert.That(position.X, Is.LessThan(3f), "and it should have held them outside");
            }

            if (doorUid.IsValid())
                Assert.That(entManager.GetComponent<Content.Shared.Doors.Components.DoorBoltComponent>(doorUid).BoltsDown, "the test needs the door to have stayed bolted");
        });
    }

    /// <summary>
    ///     Live, with real Redacted and all they carry: a noise behind a bolted door, or in a room with no way in at all. They
    ///         blow their way in and go through the hole, with the activator coming out of the bag it is carried in.
    /// </summary>
    [TestCase("WallSolid")]
    [TestCase("bolted")]
    public async Task TestRedactedBlowTheirWayInAfterANoise(string door)
    {
        var (entManager, gridUid, doorUid, mobs) = await SetUpOperativesOutsideRoom(door, "KsRandomHumanoidSpawnerOperativeRedacted", charges: true);

        await Pair.Server.WaitPost(() =>
        {
            foreach (var mobUid in mobs)
            {
                StageDisturbance(entManager, mobUid, gridUid);
            }
        });

        var inside = false;

        for (var i = 0; i < 1200 && !inside; i += 15)
        {
            await Pair.RunTicksSync(15);
            await Pair.Server.WaitPost(() =>
            {
                foreach (var mobUid in mobs)
                {
                    var position = entManager.GetComponent<TransformComponent>(mobUid).LocalPosition;
                    inside |= position.X is > 4f and < 10f && position.Y is > -4f and < 4f;
                }
            });
        }

        Assert.That(inside, "somebody should have got into the room");

        if (doorUid.IsValid())
            await Pair.Server.WaitAssertion(() => Assert.That(entManager.Deleted(doorUid), "by way of the door"));
    }

    private static void StageDisturbance(IEntityManager entManager, EntityUid mobUid, EntityUid gridUid)
    {
        var sensorSystem = entManager.System<NpcSensorSystem>();
        sensorSystem.AddEffect(mobUid, "__Sensor__Disturbance.TargetCoordinates", new EntityCoordinates(gridUid, new Vector2(7.5f, 0.5f)));
        sensorSystem.RequestReplan(mobUid);
    }

    /// <summary>
    ///     Four real operatives from <paramref name="spawner"/> outside a room shut by a wall all the way round, or a bolted door.
    ///         <paramref name="charges"/> says whether they may blow it, going by air there is none of on a test map.
    /// </summary>
    private async Task<(IEntityManager EntManager, EntityUid GridUid, EntityUid DoorUid, List<EntityUid> Mobs)> SetUpOperativesOutsideRoom(string door, string spawner, bool charges)
    {
        var protoManager = Pair.Server.ResolveDependency<IPrototypeManager>();
        if (!protoManager.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, doorUid = default;
        var mobs = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            entManager.System<NpcSquadTacticsSystem>().UpdatesPaused = false;
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-12, -10), new Vector2i(16, 12)).Owner;

            for (var x = 3; x <= 11; x++)
            {
                for (var y = -5; y <= 5; y++)
                {
                    if (x is > 3 and < 11 && y is > -5 and < 5)
                        continue;

                    if (x == 3 && y == 0 && door == "bolted")
                        doorUid = SpawnPoweredDoorAt(entManager, "Airlock", gridUid, x, y);
                    else
                        SpawnAt(entManager, "WallSolid", gridUid, x, y);
                }
            }

            for (var y = -1; y <= 2; y++)
            {
                SpawnAt(entManager, spawner, gridUid, -5, y);
            }
        });

        await Pair.RunTicksSync(90);

        await server.WaitPost(() =>
        {
            var enumerator = entManager.EntityQueryEnumerator<Content.Server._KS14.NPC.Squad.NpcSquadMemberComponent, TransformComponent>();
            while (enumerator.MoveNext(out var uid, out var squadMemberComponent, out var transformComponent))
            {
                if (transformComponent.MapID != map.MapId)
                    continue;

                mobs.Add(uid);
                entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(uid); // no air on a test map
#pragma warning disable RA0002 // set for the test, as a prototype of its own cannot build on the private module's
                squadMemberComponent.Tactics.HuntCharges = charges;
                squadMemberComponent.Tactics.PlanChargeMinPressure = 0f;
#pragma warning restore RA0002
            }

            Assert.That(mobs, Is.Not.Empty, "the test needs them spawned");
        });

        await Pair.RunTicksSync(120);

        // Not before now: a door coming up to power takes its bolts off again.
        await server.WaitPost(() =>
        {
            if (doorUid.IsValid())
                entManager.System<Content.Shared.Doors.Systems.SharedDoorSystem>().SetBoltsDown((doorUid, entManager.GetComponent<Content.Shared.Doors.Components.DoorBoltComponent>(doorUid)), true);
        });

        return (entManager, gridUid, doorUid, mobs);
    }

    /// <summary>
    ///     A Redacted really does have what a plan needs: that it plans, and that a charge and activator go in its bag with the
    ///         rest of what it carries.
    /// </summary>
    [Test]
    public async Task TestTheRedactedCarryACharge()
    {
        var protoManager = Pair.Server.ResolveDependency<IPrototypeManager>();
        if (!protoManager.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitPost(() => entManager.SpawnEntity("KsRandomHumanoidSpawnerOperativeRedacted", map.GridCoords));
        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var chargeSystem = entManager.System<Content.Server._KS14.NPC.Charges.NpcChargeSystem>();
            var enumerator = entManager.EntityQueryEnumerator<Content.Server._KS14.NPC.Squad.NpcSquadMemberComponent, TransformComponent>();
            var mobUid = EntityUid.Invalid;
            Content.Server._KS14.NPC.Squad.NpcSquadMemberComponent? squadMemberComponent = null;

            // This server may have run other tests, and they may not have cleaned up after themselves.
            while (enumerator.MoveNext(out var uid, out var memberComponent, out var transformComponent))
            {
                if (transformComponent.MapID != map.MapId)
                    continue;

                mobUid = uid;
                squadMemberComponent = memberComponent;
            }

            Assert.That(mobUid.IsValid(), "it should have spawned");
            Assert.That(squadMemberComponent!.Tactics.Planning, "it should plan");
            Assert.That(squadMemberComponent.Tactics.HuntCharges, "and blow its way in after a noise");
            Assert.That(chargeSystem.TryGetCharge(mobUid, null, out var charge), "it should have a charge it can use");
            Assert.That(charge.Method, Is.EqualTo(Content.Server._KS14.NPC.Charges.NpcChargeMethod.Wired));

            // Smoke and gas: the M40 and the air tank in its suit storage put it on internals, and it sees through smoke.
            Assert.That(entManager.System<Content.Server._KS14.NPC.Internals.NpcInternalsSystem>().CanGoOnInternals(mobUid),
                "it should be able to go on internals");
            Assert.That(entManager.GetComponent<NpcPerceptionComponent>(mobUid).SeesThroughSmoke, "it should see through smoke");

            // All black: every piece of the outfit went on.
            var inventorySystem = entManager.System<Content.Shared.Inventory.InventorySystem>();
            foreach (var (slot, prototype) in new[]
                     {
                         ("mask", "KsClothingMaskGasM40"), ("head", "KsClothingHeadHelmetBlackPlate"),
                         ("outerClothing", "KsClothingOuterArmorBlackPlate"), ("jumpsuit", "KsClothingUniformJumpsuitBlackCombat"),
                         ("gloves", "KsClothingHandsGlovesBlackPadded"), ("shoes", "KsClothingShoesBootsBlackArmy"),
                         ("belt", "KsClothingBeltChestRigBlack"), ("back", "KsClothingBackpackTacticalBlack"),
                         ("neck", "KsClothingNeckCapeBlack"),
                     })
            {
                Assert.That(inventorySystem.TryGetSlotEntity(mobUid, slot, out var wornUid) &&
                    entManager.GetComponent<MetaDataComponent>(wornUid.Value).EntityPrototype?.ID == prototype,
                    $"it should wear {prototype} in {slot}");
            }
        });
    }

    /// <summary>
    ///     Live: with no door to force, an ordinary window is shot out to get at who is behind it. Armoured glass is not worth
    ///         the ammo, so nobody can drain a squad's magazines from behind it.
    /// </summary>
    [TestCase("Window", true)]
    [TestCase("ReinforcedWindow", false)]
    public async Task TestSquadShootsOutAWindow(string window, bool breaks)
    {
        var (entManager, _, _, _, windowUid, _) = await SetUpSquadOutsideGlassRoom(window, "WallSolid");

        var broken = false;

        for (var i = 0; i < 900 && !broken; i += 15)
        {
            await Pair.RunTicksSync(15);
            await Pair.Server.WaitPost(() => broken = entManager.Deleted(windowUid));
        }

        Assert.That(broken, Is.EqualTo(breaks));
    }

    /// <summary>
    ///     Three armed operatives on the real root, outside a sealed room with <paramref name="window"/> and
    ///         <paramref name="door"/> in its west wall, and a hostile inside that they can see through the window.
    /// </summary>
    private async Task<GlassRoom> SetUpSquadOutsideGlassRoom(
        string window,
        string door,
        System.Action<IEntityManager, EntityUid>? stage = null,
        string hostile = NanoTrasenMob,
        string mob = "KsOperativeRootTestMobPlanner")
    {
        var protoManager = Pair.Server.ResolveDependency<IPrototypeManager>();
        if (!protoManager.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        var mobUids = new List<EntityUid>();
        var gunUids = new List<EntityUid>();
        EntityUid doorUid = default, windowUid = default, hostileUid = default;

        await server.WaitPost(() =>
        {
            entManager.System<NpcSquadTacticsSystem>().UpdatesPaused = false; // a test before this one on this server may not have
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-8, -8), new Vector2i(14, 12)).Owner;

            for (var x = 3; x <= 11; x++)
            {
                for (var y = -5; y <= 5; y++)
                {
                    if (x is > 3 and < 11 && y is > -5 and < 5)
                        continue;

                    if (x == 3 && y == 0)
                        windowUid = SpawnAt(entManager, window, gridUid, x, y);
                    else if (x == 3 && y == -2 && door != "WallSolid")
                        doorUid = SpawnPoweredDoorAt(entManager, door, gridUid, x, y);
                    else
                        SpawnAt(entManager, "WallSolid", gridUid, x, y);
                }
            }

            hostileUid = SpawnAt(entManager, hostile, gridUid, 7, 0);
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(hostileUid); // a fight that lasts

            for (var y = -1; y <= 1; y++)
            {
                var mobUid = SpawnAt(entManager, mob, gridUid, 0, y);
                mobUids.Add(mobUid);

                // Before anything else: tactics are not paused, and a squad with nothing to shoot with has nothing to plan.
                StageArmed(entManager, mobUid);
                KeepLoaded(entManager, mobUid);
                stage?.Invoke(entManager, mobUid);

                Assert.That(entManager.System<Content.Shared.Weapons.Ranged.Systems.SharedGunSystem>().TryGetGun(mobUid, out var gunEntity), "it should be holding its gun");
                gunUids.Add(gunEntity.Owner);
            }
        });

        await Pair.RunTicksSync(150);

        await server.WaitPost(() =>
        {
            foreach (var mobUid in mobUids)
            {
                var htnComponent = entManager.GetComponent<HTNComponent>(mobUid);
                htnComponent.RootTask = new HTNCompoundTask { Task = _operativeRoot };
                entManager.System<HTNSystem>().SetHTNEnabled((mobUid, htnComponent), true);
                entManager.System<NPCSystem>().WakeNPC(mobUid, htnComponent);
            }
        });

        return new GlassRoom(entManager, gunUids, mobUids, doorUid, windowUid, hostileUid);
    }

    private sealed record GlassRoom(IEntityManager EntManager, List<EntityUid> GunUids, List<EntityUid> MobUids, EntityUid DoorUid, EntityUid WindowUid, EntityUid HostileUid);

    /// <summary>
    ///     An armed operative walking the real root at 0,0, and a hostile it can see at 6,0, past a line of whatever
    ///         <paramref name="barrier"/> says is at each y along x = 3, right across the grid.
    /// </summary>
    private async Task<(IEntityManager EntManager, EntityUid MobUid, EntityUid HostileUid)> SetUpBehindGlass(System.Func<int, string?> barrier,
        string gun = "WeaponPistolMk58",
        System.Action<NPCBlackboard>? stage = null,
        string mob = "KsOperativeRootTestMobWalking")
    {
        var protoManager = Pair.Server.ResolveDependency<IPrototypeManager>();
        if (!protoManager.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid mobUid = default, hostileUid = default;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-8, -8), new Vector2i(12, 12)).Owner;

            for (var y = -8; y <= 12; y++)
            {
                if (barrier(y) is { } what)
                    SpawnAt(entManager, what, gridUid, 3, y);
            }

            mobUid = SpawnAt(entManager, mob, gridUid, 0, 0);
            hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 6, 0);
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(hostileUid); // a fight that lasts
        });

        await Pair.RunTicksSync(150);

        await server.WaitPost(() =>
        {
            StageArmed(entManager, mobUid, gun);

            var htnComponent = entManager.GetComponent<HTNComponent>(mobUid);
            stage?.Invoke(htnComponent.Blackboard);
            htnComponent.RootTask = new HTNCompoundTask { Task = _operativeRoot };
            entManager.System<HTNSystem>().SetHTNEnabled((mobUid, htnComponent), true);
            entManager.System<NPCSystem>().WakeNPC(mobUid, htnComponent);
        });

        return (entManager, mobUid, hostileUid);
    }

    /// <summary>
    ///     An armed operative with a hostile in plain view, due a new spot to fight from, finds one; one that has just
    ///         found one shoots from where it is, closing in as before, until the next is due. Either way it shoots.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestFightsFromCoverWhenDue(bool due)
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageVisibleHostile(entManager, mobUid, gridUid);

                if (!due)
                {
                    var now = Robust.Shared.IoC.IoCManager.Resolve<IGameTiming>().CurTime;
                    entManager.GetComponent<HTNComponent>(mobUid).Blackboard.SetValue("RepositionDueAt", now + System.TimeSpan.FromSeconds(4));
                }
            });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks.Any(task => task.Operator is Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat.Ranged.GunOperator),
                $"expected the operative to shoot, got: {Describe(plan)}");
            Assert.That(plan.Tasks.Any(task => task.Operator is TacticalPositionOperator { Key: "FiringPosition" }), Is.EqualTo(due),
                due ? $"expected a spot to fight from, got: {Describe(plan)}" : $"a spot was only just found, got: {Describe(plan)}");
        });
    }

    /// <summary>
    ///     With this tick's budget for exposure checks spent, a spot that is due is not searched for: the operative
    ///         shoots from where it is, and tries again on a later replan.
    /// </summary>
    [Test]
    public async Task TestFiringSpotWaitsForBudget()
    {
        await OverrideCVar(Content.IntegrationTests.Fixtures.Attributes.Side.Server, Content.Shared._KS14.CCVar.KsCCVars.NpcExposureRayBudget, 0);

        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageVisibleHostile(entManager, mobUid, gridUid);
            });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks.Any(task => task.Operator is Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat.Ranged.GunOperator),
                $"expected the operative to shoot anyway, got: {Describe(plan)}");
            Assert.That(plan.Tasks.Any(task => task.Operator is TacticalPositionOperator { Key: "FiringPosition" }), Is.False,
                $"with no budget left, the spot search should wait, got: {Describe(plan)}");
        });
    }

    /// <summary>
    ///     Live: a fighting operative does not keep its first spot for the whole fight. Its shooting gives way when a
    ///         new spot is due, and it looks for one again - without that, a replan onto the same branch never beats
    ///         the running plan, and it would stay put until the target dropped or vanished.
    /// </summary>
    [Test]
    public async Task TestFightingOperativeLooksForNewSpots()
    {
        var protoManager = Pair.Server.ResolveDependency<IPrototypeManager>();
        if (!protoManager.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var (entManager, mobUid) = await SetUpOperativeInRoom(HandsMob);
        var htnSystem = entManager.System<HTNSystem>();
        var npcSystem = entManager.System<NPCSystem>();
        var timing = Pair.Server.ResolveDependency<IGameTiming>();

        await Pair.Server.WaitPost(() =>
        {
            var gridUid = entManager.GetComponent<TransformComponent>(mobUid).ParentUid;
            StageArmed(entManager, mobUid);
            StageVisibleHostile(entManager, mobUid, gridUid);

            var htnComponent = entManager.GetComponent<HTNComponent>(mobUid);
            htnComponent.RootTask = new HTNCompoundTask { Task = _operativeRoot };
            htnComponent.Blackboard.SetValue(EnsureVirtualMarkerOperator.MarkerSet, new HashSet<string> { CombatMarker });
            htnSystem.SetHTNEnabled((mobUid, htnComponent), true);
            npcSystem.WakeNPC(mobUid, htnComponent);
        });

        await Pair.RunTicksSync(30);

        var first = System.TimeSpan.Zero;
        await Pair.Server.WaitAssertion(() =>
        {
            var blackboard = entManager.GetComponent<HTNComponent>(mobUid).Blackboard;
            Assert.That(blackboard.TryGetValue<System.TimeSpan>("RepositionDueAt", out first, entManager),
                "the operative should have picked a spot to fight from");
        });

        // Past the five seconds at most a spot is kept for, with the magazine kept topped up: a pistol empties in about
        //      that long, and running dry ends the fight for reasons of its own.
        for (var i = 0; i < 14; i++)
        {
            await Pair.RunTicksSync(15);
            await Pair.Server.WaitPost(() => KeepLoaded(entManager, mobUid));
        }

        await Pair.Server.WaitAssertion(() =>
        {
            var blackboard = entManager.GetComponent<HTNComponent>(mobUid).Blackboard;
            Assert.That(blackboard.TryGetValue<System.TimeSpan>("RepositionDueAt", out var latest, entManager) && latest > first,
                $"the operative should have looked for a new spot when one came due at {first}; it is {timing.CurTime}");
        });
    }

    /// <summary>
    ///     Puts a loaded pistol, a round chambered, in the operative's hand.
    /// </summary>
    private static void StageArmed(IEntityManager entManager, EntityUid mobUid, string gun = "WeaponPistolMk58")
    {
        var handsSystem = entManager.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
        handsSystem.AddHand(mobUid, "right", Content.Shared.Hands.Components.HandLocation.Right);

        var gunUid = entManager.SpawnEntity(gun, entManager.GetComponent<TransformComponent>(mobUid).Coordinates);
        Assert.That(handsSystem.TryPickup(mobUid, gunUid, "right"), "the operative should be holding the pistol");

        // What the operative prototypes put on the blackboard for fighting with it, which the test mob lacks.
        var blackboard = entManager.GetComponent<HTNComponent>(mobUid).Blackboard;
        blackboard.SetValue("VisionRadius", 17.5f);
        blackboard.SetValue("AggroVisionRadius", 25f);
        blackboard.SetValue("RangedRange", 9f);
        blackboard.SetValue("MaxFiringDistance", 9f);
        blackboard.SetValue("CampingRange", 0.75f);
        blackboard.SetValue("RetreatClaimDuration", 10f);
        blackboard.SetValue("HoldTime", 6f);
        blackboard.SetValue("IdleActTime", 1f);

        // Chamber a round, as racking would.
        if (entManager.TryGetComponent<Content.Shared.Weapons.Ranged.Components.ChamberMagazineAmmoProviderComponent>(gunUid, out var chamberComponent))
            entManager.System<Content.Shared.Weapons.Ranged.Systems.SharedGunSystem>().UseChambered(gunUid, chamberComponent, mobUid);
    }

    /// <summary>
    ///     Refills the magazine of the gun the operative holds.
    /// </summary>
    private static void KeepLoaded(IEntityManager entManager, EntityUid mobUid)
    {
        var handsSystem = entManager.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
        if (!handsSystem.TryGetHeldItem(mobUid, "right", out var gunUid) ||
            !entManager.System<Robust.Shared.Containers.SharedContainerSystem>().TryGetContainer(gunUid.Value, "gun_magazine", out var magazineContainer) ||
            magazineContainer.ContainedEntities.Count == 0)
            return;

        var magazineUid = magazineContainer.ContainedEntities[0];
        var ballisticComponent = entManager.GetComponent<Content.Shared.Weapons.Ranged.Components.BallisticAmmoProviderComponent>(magazineUid);
        entManager.System<Content.Shared.Weapons.Ranged.Systems.SharedGunSystem>().SetBallisticUnspawned((magazineUid, ballisticComponent), ballisticComponent.Capacity);
    }

    /// <summary>
    ///     Waiting for meds to work, and only just moved, so that it holds rather than falling back.
    /// </summary>
    private static void StageRecoveringAndSettled(IEntityManager entManager, EntityUid mobUid)
    {
        entManager.System<NpcRecoverySystem>().StartRecovery(mobUid);

        var now = Robust.Shared.IoC.IoCManager.Resolve<IGameTiming>().CurTime;
        entManager.GetComponent<HTNComponent>(mobUid).Blackboard.SetValue("RecoveryFallBackDueAt", now + System.TimeSpan.FromHours(1));
    }

    /// <summary>
    ///     Two empty hands, for an operative with nothing else staged to give it any.
    /// </summary>
    private static void StageHands(IEntityManager entManager, EntityUid mobUid)
    {
        var handsSystem = entManager.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
        handsSystem.AddHand(mobUid, "right", Content.Shared.Hands.Components.HandLocation.Right);
        handsSystem.AddHand(mobUid, "left", Content.Shared.Hands.Components.HandLocation.Left);
    }

    /// <summary>
    ///     Puts <paramref name="prototype"/> in the operative's pocket.
    /// </summary>
    private static EntityUid StageCarried(IEntityManager entManager, EntityUid mobUid, string prototype, string pocket = "pocket1")
    {
        var itemUid = entManager.SpawnEntity(prototype, entManager.GetComponent<TransformComponent>(mobUid).Coordinates);
        Assert.That(entManager.System<Content.Shared.Inventory.InventorySystem>().TryEquip(mobUid, itemUid, pocket, silent: true, force: true),
            $"{prototype} should be in the operative's pocket");
        return itemUid;
    }

    /// <summary>
    ///     Leaves the magazine of the gun the operative holds a quarter full.
    /// </summary>
    private static void StageHalfEmptyMagazine(IEntityManager entManager, EntityUid mobUid)
    {
        var handsSystem = entManager.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
        Assert.That(handsSystem.TryGetHeldItem(mobUid, "right", out var gunUid));
        Assert.That(entManager.System<Robust.Shared.Containers.SharedContainerSystem>().TryGetContainer(gunUid!.Value, "gun_magazine", out var magazineContainer));

        var magazineUid = magazineContainer!.ContainedEntities[0];
        var ballisticComponent = entManager.GetComponent<Content.Shared.Weapons.Ranged.Components.BallisticAmmoProviderComponent>(magazineUid);
        entManager.System<Content.Shared.Weapons.Ranged.Systems.SharedGunSystem>().SetBallisticUnspawned((magazineUid, ballisticComponent), ballisticComponent.Capacity / 4);
    }

    /// <summary>
    ///     A hostile in plain view, down, seen and reacted to.
    /// </summary>
    private static void StageDownedHostile(IEntityManager entManager, EntityUid mobUid, EntityUid gridUid, int x, int y)
    {
        var hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, x, y);
        entManager.System<Content.Shared.Mobs.Systems.MobStateSystem>().ChangeMobState(hostileUid, Content.Shared.Mobs.MobState.Critical);

        var now = Robust.Shared.IoC.IoCManager.Resolve<IGameTiming>().CurTime;
        entManager.System<NpcPerceptionSystem>().SetContact(mobUid, hostileUid, new NpcContact(NpcContactState.Visible,
            now, now, now, entManager.GetComponent<TransformComponent>(hostileUid).Coordinates, default, null, Reacted: true));
    }

    /// <summary>
    ///     A hostile in the operative's room, in plain view, seen and reacted to.
    /// </summary>
    private static void StageVisibleHostile(IEntityManager entManager, EntityUid mobUid, EntityUid gridUid)
    {
        var hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 0, 0);
        entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(hostileUid); // a fight that lasts
        var now = Robust.Shared.IoC.IoCManager.Resolve<IGameTiming>().CurTime;
        entManager.System<NpcPerceptionSystem>().SetContact(mobUid, hostileUid, new NpcContact(NpcContactState.Visible,
            now, now, now, entManager.GetComponent<TransformComponent>(hostileUid).Coordinates, default, null, Reacted: true));
    }

    /// <summary>
    ///     In combat, with nothing in its own sight but a hostile a squadmate is calling out round the corner - outside
    ///         the room, seen only through its open doorway, and not from where the operative stands - it goes to peek
    ///         it: to a spot that sees where it was called out. Without this it held back behind the corner, guarding,
    ///         while the squadmate fought alone.
    /// </summary>
    [Test]
    public async Task TestCalledOutHostileIsPeeked()
    {
        IEntityManager entManager = default!;
        EntityUid gridUid = default;

        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (stagedEntManager, mobUid, stagedGridUid) =>
            {
                entManager = stagedEntManager;
                gridUid = stagedGridUid;
                StageArmed(entManager, mobUid);
                StageCalledOutRoundTheCorner(entManager, mobUid, gridUid);
            });

        var peekIndex = plan.Tasks.FindIndex(task => task.Operator is TacticalPositionOperator { Key: "PeekPosition" });
        Assert.That(peekIndex, Is.Not.Negative, $"expected the operative to look for a spot to peek from, got: {Describe(plan)}");
        Assert.That(plan.Effects[peekIndex]?.TryGetValue("PeekPositionCoordinates", out var peekObject) == true, "the peek spot should be on the blackboard");

        await Pair.Server.WaitAssertion(() =>
        {
            var transformSystem = entManager.System<SharedTransformSystem>();
            var peekCoordinates = (EntityCoordinates) plan.Effects[peekIndex]!["PeekPositionCoordinates"];
            Assert.That(entManager.System<NpcLineOfSightSystem>().InLineOfSight(
                    transformSystem.ToMapCoordinates(peekCoordinates),
                    transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, CalledOutPosition)),
                    15f),
                $"the spot it picked, {peekCoordinates.Position}, should see the hostile");
        });
    }

    /// <summary>
    ///     One to a corner, live: with a squadmate already peeking from beside the best spot, an operative peeks from
    ///         somewhere clear of the squadmate's claim, or pushes up instead. That a claim rules spots out rather than
    ///         scoring them down is <c>KsNpcExposureTest.TestExclusiveClaimRulesOutClaimedSpots</c>'s to show.
    /// </summary>
    [Test]
    public async Task TestPeekSpotIsNotShared()
    {
        Vector2 firstSpot = default;
        var first = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageCalledOutRoundTheCorner(entManager, mobUid, gridUid);
            });

        var firstIndex = first.Tasks.FindIndex(task => task.Operator is TacticalPositionOperator { Key: "PeekPosition" });
        Assert.That(firstIndex, Is.Not.Negative, $"expected a peek, got: {Describe(first)}");
        firstSpot = ((EntityCoordinates) first.Effects[firstIndex]!["PeekPositionCoordinates"]).Position;

        var second = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (entManager, mobUid, gridUid) =>
            {
                StageArmed(entManager, mobUid);
                StageCalledOutRoundTheCorner(entManager, mobUid, gridUid);

                // A squadmate already peeking from beside it.
                var squadmateUid = SpawnAt(entManager, SyndicateMob, gridUid, 4, 1);
                entManager.System<NpcTacticalPositionClaimSystem>().Claim(squadmateUid, new EntityCoordinates(gridUid, firstSpot + ClaimOffset),
                    System.TimeSpan.FromSeconds(30), clearanceRadius: 3f);
            });

        var secondIndex = second.Tasks.FindIndex(task => task.Operator is TacticalPositionOperator { Key: "PeekPosition" });
        if (secondIndex < 0)
        {
            Assert.That(second.Tasks.Any(task => task.Operator is MoveToOperator { TargetKey: "SupportTargetCoordinates" }),
                $"with the peek spot taken and no other, it should push up instead, got: {Describe(second)}");
            return;
        }

        var secondSpot = ((EntityCoordinates) second.Effects[secondIndex]!["PeekPositionCoordinates"]).Position;
        Assert.That((secondSpot - (firstSpot + ClaimOffset)).Length(), Is.GreaterThanOrEqualTo(3f),
            $"it should not peek from {secondSpot}, within the squadmate's claim on {firstSpot + ClaimOffset}");
    }

    /// <summary>
    ///     Where <see cref="TestPeekSpotIsNotShared"/>'s squadmate claims, from the best peek spot.
    /// </summary>
    private static readonly Vector2 ClaimOffset = new(1.5f, 0f);

    /// <summary>
    ///     Where <see cref="StageCalledOutRoundTheCorner"/> puts the hostile: outside the room, south-west of its airlock.
    /// </summary>
    private static readonly Vector2 CalledOutPosition = new(-3.5f, 0.5f);

    /// <summary>
    ///     A hostile a squadmate can see and is calling out, outside the room past its airlock, which is open: seen
    ///         through the doorway from spots further into the room, but not from where the operative stands.
    /// </summary>
    private static void StageCalledOutRoundTheCorner(IEntityManager entManager, EntityUid mobUid, EntityUid gridUid)
    {
        var transformSystem = entManager.System<SharedTransformSystem>();

        // The open doorway: an open door occludes nothing, and stops nothing.
        var doorEnumerator = entManager.EntityQueryEnumerator<Content.Shared.Doors.Components.DoorComponent, OccluderComponent>();
        while (doorEnumerator.MoveNext(out var doorUid, out _, out var occluderComponent))
        {
            entManager.System<OccluderSystem>().SetEnabled(doorUid, false, occluderComponent);
            entManager.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetCanCollide(doorUid, false);
        }

        var hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, -4, 0);
        var now = Robust.Shared.IoC.IoCManager.Resolve<IGameTiming>().CurTime;
        entManager.System<NpcPerceptionSystem>().SetContact(mobUid, hostileUid, new NpcContact(NpcContactState.Reported,
            now, now, default, new EntityCoordinates(gridUid, CalledOutPosition), default, null, Reacted: false));

        Assert.That(entManager.System<NpcLineOfSightSystem>().InLineOfSight(
                transformSystem.GetMapCoordinates(mobUid),
                transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, CalledOutPosition)),
                15f),
            Is.False,
            "the operative should not see the hostile from where it stands, or there is nothing to peek round");
    }

    /// <summary>
    ///     An operative not in combat that hears a squadmate call out a hostile joins the fight.
    /// </summary>
    [Test]
    public async Task TestCalloutBringsIdleOperativeIn()
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: false, inCombat: false,
            stage: (entManager, mobUid, gridUid) => StageContact(entManager, mobUid, gridUid, NpcContactState.Reported));

        Assert.That(plan.Tasks.Any(task => task.Operator is UtilityOperator { Prototype.Id: "KsOperativeReportedContacts" }),
            $"expected the callout to be acted on, got: {Describe(plan)}");
    }

    /// <summary>
    ///     Which running plan a replan may replace, by the planner's branch traversal record rule: a search is not
    ///         cut short by anything below it, a hold gives way to a search or an order, and an order is not cut
    ///         short by a hold or by sensor data - a callout does not pull a member off a hunt.
    /// </summary>
    [Test]
    public async Task TestNewBranchesInterruptOnlyWhatTheyShould()
    {
        var hold = await PlanOperative(pendingSensorData: false, recentlyFought: true);
        var sensors = await PlanOperative(pendingSensorData: true, recentlyFought: true);
        var search = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (entManager, mobUid, gridUid) => StageHidden(entManager, mobUid, gridUid, welded: false));
        var cover = await PlanOperative(pendingSensorData: false, recentlyFought: true, mobPrototype: HandsMob,
            stage: (entManager, mobUid, gridUid) => StageHidden(entManager, mobUid, gridUid, welded: true));
        var order = await PlanOperative(pendingSensorData: false, recentlyFought: true,
            stage: (entManager, mobUid, gridUid) => StageOrder(entManager, mobUid, gridUid, NpcOrderKind.Watch));
        var orderWithSensors = await PlanOperative(pendingSensorData: true, recentlyFought: true,
            stage: (entManager, mobUid, gridUid) => StageOrder(entManager, mobUid, gridUid, NpcOrderKind.Watch));

        Assert.Multiple(() =>
        {
            Assert.That(Replaces(search, hold), "a search should interrupt a hold");
            Assert.That(Replaces(order, hold), "an order should interrupt a hold");
            Assert.That(Replaces(order, sensors), "an order should interrupt handling sensor data");
            Assert.That(Replaces(hold, search), Is.False, "a hold must not interrupt a search");
            Assert.That(Replaces(order, search), Is.False, "an order must not interrupt a search");
            Assert.That(Replaces(sensors, search), Is.False, "sensor data must not interrupt a search");
            Assert.That(Replaces(hold, cover), Is.False, "a hold must not interrupt covering a locker");
            Assert.That(Replaces(order, cover), Is.False, "an order must not interrupt covering a locker");
            Assert.That(Replaces(hold, order), Is.False, "a hold must not interrupt an order");
            Assert.That(Replaces(sensors, order), Is.False, "sensor data must not interrupt an order");
            Assert.That(orderWithSensors.Tasks.Any(task => task.Operator is GetOrderOperator),
                $"with both, the order should win, got: {Describe(orderWithSensors)}");
        });
    }

    /// <summary>
    ///     HTNSystem's rule: a new plan replaces the running one if its branch traversal record is lower at any index.
    /// </summary>
    private static bool Replaces(HTNPlan newPlan, HTNPlan runningPlan)
    {
        var running = runningPlan.BranchTraversalRecord;
        var proposed = newPlan.BranchTraversalRecord;

        for (var i = 0; i < running.Count; i++)
        {
            if (i < proposed.Count && running[i] > proposed[i])
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Gives the operative an order by hand, somewhere in its room, with the tactics system paused so it is not
    ///         taken straight back.
    /// </summary>
    private static void StageOrder(IEntityManager entManager, EntityUid mobUid, EntityUid gridUid, NpcOrderKind kind)
    {
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        tacticsSystem.UpdatesPaused = true;
        tacticsSystem.IssueOrder(mobUid, kind, new EntityCoordinates(gridUid, new System.Numerics.Vector2(2.5f, 2.5f)));
    }

    /// <summary>
    ///     A running order is dropped the moment the order changes, and the operative replans onto the new one:
    ///         every task carrying out an order rechecks that it is still current. Without that, the old plan would
    ///         carry on - a replan onto the same branch never beats it.
    /// </summary>
    [Test]
    public async Task TestOrderChangeIsActedOnAtOnce()
    {
        var protoManager = Pair.Server.ResolveDependency<IPrototypeManager>();
        if (!protoManager.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var (entManager, mobUid) = await SetUpOperativeInRoom();
        var htnSystem = entManager.System<HTNSystem>();
        var npcSystem = entManager.System<NPCSystem>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        EntityUid gridUid = default;

        await Pair.Server.WaitPost(() =>
        {
            gridUid = entManager.GetComponent<TransformComponent>(mobUid).ParentUid;
            tacticsSystem.UpdatesPaused = true;
            tacticsSystem.IssueOrder(mobUid, NpcOrderKind.Watch, new EntityCoordinates(gridUid, new System.Numerics.Vector2(4.5f, 3.5f)));

            // The operative root, not the test mob's idle one; set here rather than in a prototype, which would
            //      fail to load without the private submodule.
            var htnComponent = entManager.GetComponent<HTNComponent>(mobUid);
            htnComponent.RootTask = new HTNCompoundTask { Task = _operativeRoot };
            htnComponent.Blackboard.SetValue(EnsureVirtualMarkerOperator.MarkerSet, new HashSet<string> { CombatMarker });
            htnSystem.SetHTNEnabled((mobUid, htnComponent), true);
            npcSystem.WakeNPC(mobUid, htnComponent);
        });

        // Long enough to plan and start waiting on the first order.
        await Pair.RunTicksSync(60);

        var firstId = 0;
        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(tacticsSystem.TryGetOrder(mobUid, out var firstOrder));
            firstId = firstOrder.Id;

            var blackboard = entManager.GetComponent<HTNComponent>(mobUid).Blackboard;
            Assert.That(blackboard.TryGetValue<int>("OrderId", out var plannedId, entManager) && plannedId == firstId,
                "the operative should be carrying out its first order");

            tacticsSystem.IssueOrder(mobUid, NpcOrderKind.Watch, new EntityCoordinates(gridUid, new System.Numerics.Vector2(1.5f, 1.5f)));
        });

        // A replan happens every 0.45s anyway, and lands on the same branch: only the recheck makes it give way.
        await Pair.RunTicksSync(60);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(tacticsSystem.TryGetOrder(mobUid, out var secondOrder) && secondOrder.Id != firstId);

            var blackboard = entManager.GetComponent<HTNComponent>(mobUid).Blackboard;
            blackboard.TryGetValue<int>("OrderId", out var plannedId, entManager);
            Assert.That(plannedId, Is.EqualTo(secondOrder.Id),
                $"the operative should have dropped its old order (id {firstId}) for the new one");
        });
    }

    /// <summary>
    ///     A hostile out of sight behind the room's walls, that the operative saw climb into a locker in its room.
    /// </summary>
    private static void StageHidden(IEntityManager entManager, EntityUid mobUid, EntityUid gridUid, bool welded)
    {
        var lockerUid = SpawnAt(entManager, "ClosetSteelBase", gridUid, 2, 2);
        if (welded)
            entManager.System<WeldableSystem>().SetWeldedState(lockerUid, true);

        var hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 10, 10);
        var now = Robust.Shared.IoC.IoCManager.Resolve<IGameTiming>().CurTime;
        entManager.System<NpcPerceptionSystem>().SetContact(mobUid, hostileUid, new NpcContact(NpcContactState.Concealed,
            now, now, now, entManager.GetComponent<TransformComponent>(lockerUid).Coordinates, default, lockerUid, Reacted: true));
    }

    /// <summary>
    ///     A hostile out of sight behind the room's walls, that the operative believes was last somewhere in its room.
    /// </summary>
    private static void StageContact(IEntityManager entManager, EntityUid mobUid, EntityUid gridUid, NpcContactState state, System.Numerics.Vector2? believedAt = null, System.TimeSpan age = default)
    {
        var hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 10, 10);
        var seenAt = Robust.Shared.IoC.IoCManager.Resolve<IGameTiming>().CurTime - age;
        entManager.System<NpcPerceptionSystem>().SetContact(mobUid, hostileUid, new NpcContact(state,
            seenAt, seenAt, seenAt, new EntityCoordinates(gridUid, believedAt ?? new System.Numerics.Vector2(2.5f, 2.5f)), new System.Numerics.Vector2(1f, 0f),
            null, Reacted: state != NpcContactState.Reported));
    }

    /// <summary>
    ///     New sensor data makes an awake NPC replan on its next update instead of waiting out its cooldown.
    /// </summary>
    [Test]
    public async Task TestSensorDataForcesReplan()
    {
        var (entManager, mobUid) = await SetUpOperativeInRoom();
        var htnSystem = entManager.System<HTNSystem>();
        var npcSystem = entManager.System<NPCSystem>();
        var sensorSystem = entManager.System<NpcSensorSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            var htnComponent = entManager.GetComponent<HTNComponent>(mobUid);
            htnSystem.SetHTNEnabled((mobUid, htnComponent), true);
            npcSystem.WakeNPC(mobUid, htnComponent);

            htnComponent.PlanAccumulator = 100f;
            sensorSystem.AddEffect(mobUid, "__Sensor__KsTest", true);

            Assert.That(htnComponent.PlanAccumulator, Is.LessThanOrEqualTo(0f), "new sensor data should force a replan");
        });
    }

    /// <summary>
    ///     Control for <see cref="TestSensorDataForcesReplan"/>: a sleeping NPC is left alone.
    /// </summary>
    [Test]
    public async Task TestSensorDataDoesNotWakeSleepingNpc()
    {
        var (entManager, mobUid) = await SetUpOperativeInRoom();
        var htnSystem = entManager.System<HTNSystem>();
        var npcSystem = entManager.System<NPCSystem>();
        var sensorSystem = entManager.System<NpcSensorSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            var htnComponent = entManager.GetComponent<HTNComponent>(mobUid);
            htnSystem.SetHTNEnabled((mobUid, htnComponent), true);
            npcSystem.SleepNPC(mobUid, htnComponent);

            htnComponent.PlanAccumulator = 100f;
            sensorSystem.AddEffect(mobUid, "__Sensor__KsTest", true);

            Assert.Multiple(() =>
            {
                Assert.That(npcSystem.IsAwake(mobUid, htnComponent), Is.False, "sensor data must not wake an NPC");
                Assert.That(htnComponent.PlanAccumulator, Is.EqualTo(100f));
            });
        });
    }

    /// <param name="knownThreat">Whether the operative knows where a threat is (LastKnownThreatCoordinates).</param>
    /// <param name="squadThreat">Whether its squad has a threat reported to it.</param>
    private async Task<HTNPlan> PlanOperative(
        bool pendingSensorData,
        bool recentlyFought,
        bool inCombat = true,
        bool knownThreat = false,
        bool squadThreat = false,
        string mobPrototype = SyndicateMob,
        bool withSquadmate = false,
        System.Action<IEntityManager, EntityUid, EntityUid>? stage = null,
        bool withPen = false,
        ProtoId<HTNCompoundPrototype>? root = null)
    {
        var protoManager = Pair.Server.ResolveDependency<IPrototypeManager>();
        if (!protoManager.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var (entManager, mobUid) = await SetUpOperativeInRoom(mobPrototype, withSquadmate, withPen);
        var sensorSystem = entManager.System<NpcSensorSystem>();
        var squadSystem = entManager.System<NpcSquadSystem>();
        var timing = Pair.Server.ResolveDependency<IGameTiming>();

        HTNPlanJob job = default!;

        await Pair.Server.WaitPost(() =>
        {
            if (pendingSensorData)
                sensorSystem.AddEffect(mobUid, "__Sensor__KsTest", true);

            stage?.Invoke(entManager, mobUid, entManager.GetComponent<TransformComponent>(mobUid).ParentUid);

            var blackboard = entManager.GetComponent<HTNComponent>(mobUid).Blackboard.ShallowClone();
            blackboard.SetValue(NPCBlackboard.Owner, mobUid);
            if (inCombat)
                blackboard.SetValue(EnsureVirtualMarkerOperator.MarkerSet, new HashSet<string> { CombatMarker });

            if (recentlyFought)
                blackboard.SetValue(CombatTimeKey, timing.CurTime);

            // Somewhere in the operative's own room.
            var threatCoordinates = new EntityCoordinates(entManager.GetComponent<TransformComponent>(mobUid).ParentUid, new System.Numerics.Vector2(1.5f, 1.5f));

            if (knownThreat)
                blackboard.SetValue(ThreatKey, threatCoordinates);

            if (squadThreat)
                squadSystem.ReportThreat(mobUid, threatCoordinates);

            job = new HTNPlanJob(
                maxTime: 10,
                protoManager,
                new HTNCompoundTask { Task = root ?? _operativeRoot },
                blackboard,
                branchTraversal: null);
        });

        // Resumed on the server thread; ticked in between whenever an operator is waiting on something external,
        //      such as a pathfinding request.
        for (var i = 0; i < 300 && job.Status != JobStatus.Finished; i++)
        {
            if (job.Status == JobStatus.Waiting)
                await Pair.RunTicksSync(1);
            else
                await Pair.Server.WaitPost(() => job.Run());
        }

        Assert.That(job.Status, Is.EqualTo(JobStatus.Finished));
        Assert.That(job.Exception, Is.Null);
        Assert.That(job.Result, Is.Not.Null, "the operative root planned nothing at all");

        return job.Result!;
    }

    /// <summary>
    ///     A lone operative stand-in in a small walled room with one airlock, left long enough for the navmesh
    ///         to build and its one-man squad to form.
    /// </summary>
    /// <param name="withPen">Also fences off the room's corner at 0,0 with grilles, before the navmesh builds: what is in it
    ///     can be seen, and nothing can walk to it.</param>
    private async Task<(IEntityManager EntManager, EntityUid MobUid)> SetUpOperativeInRoom(string mobPrototype = SyndicateMob,
        bool withSquadmate = false,
        bool withPen = false)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid mobUid = default;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-8, -8), new Vector2i(12, 12)).Owner;

            for (var x = -1; x <= 7; x++)
            {
                for (var y = -1; y <= 6; y++)
                {
                    if (x is > -1 and < 7 && y is > -1 and < 6)
                        continue;

                    SpawnAt(entManager, x == -1 && y == 2 ? "Airlock" : "WallSolid", gridUid, x, y);
                }
            }

            mobUid = SpawnAt(entManager, mobPrototype, gridUid, 4, 3);

            if (withSquadmate)
                SpawnAt(entManager, SyndicateMob, gridUid, 2, 1);

            if (withPen)
            {
                foreach (var (x, y) in new[] { (0, 2), (1, 2), (2, 2), (2, 1), (2, 0) })
                {
                    SpawnAt(entManager, "Grille", gridUid, x, y);
                }
            }
        });

        await Pair.RunTicksSync(150);

        return (entManager, mobUid);
    }

    private static string Describe(HTNPlan plan)
    {
        return string.Join(", ", plan.Tasks.Select(task => task.Operator.GetType().Name));
    }
}
