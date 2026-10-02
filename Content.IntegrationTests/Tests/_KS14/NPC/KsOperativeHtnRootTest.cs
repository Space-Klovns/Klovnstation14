#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Interactions;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Orders;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Squad;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators;
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
";

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
    ///     An operative playing dead does nothing else: its branch is the first in the root, so not even healing or a
    ///         target in reach gets it up early.
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

        Assert.Multiple(() =>
        {
            Assert.That(plan.BranchTraversalRecord[0], Is.Zero, "playing dead should be the root's first branch");
            Assert.That(plan.Tasks.Single().Operator, Is.TypeOf<Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.PlayDead.PlayDeadOperator>(),
                $"expected only playing dead, got: {Describe(plan)}");
        });
    }

    /// <summary>
    ///     A live grenade in sight is run from, in combat or out of it - and so is anything else primed and ticking,
    ///         like a C4 charge, which is no hand grenade.
    /// </summary>
    [TestCase(true, "ExGrenade")]
    [TestCase(false, "ExGrenade")]
    [TestCase(true, "C4")]
    public async Task TestLiveGrenadeIsFled(bool inCombat, string explosive)
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: inCombat, inCombat: inCombat,
            stage: (entManager, _, gridUid) =>
            {
                var grenadeUid = SpawnAt(entManager, explosive, gridUid, 2, 3);
                Assert.That(entManager.System<Content.Shared.Trigger.Systems.TriggerSystem>().ActivateTimerTrigger(grenadeUid),
                    "the grenade should be primed");
            });

        Assert.That(plan.Tasks.Any(task => task.Operator is UtilityOperator { Prototype.Id: "KsOperativeNearbyGrenades" }),
            $"expected the grenade to be fled, got: {Describe(plan)}");
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
            grenadeUid = SpawnAt(entManager, "ExGrenade", gridUid, 2, 3);
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
                    entManager.GetComponent<HTNComponent>(mobUid).Blackboard.SetValue("RepositionedAt", now);
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
            Assert.That(blackboard.TryGetValue<System.TimeSpan>("RepositionedAt", out first, entManager),
                "the operative should have picked a spot to fight from");
        });

        // Past the four seconds a spot is kept for, with the magazine kept topped up: a pistol empties in about that
        //      long, and running dry ends the fight for reasons of its own.
        for (var i = 0; i < 10; i++)
        {
            await Pair.RunTicksSync(15);
            await Pair.Server.WaitPost(() => KeepLoaded(entManager, mobUid));
        }

        await Pair.Server.WaitAssertion(() =>
        {
            var blackboard = entManager.GetComponent<HTNComponent>(mobUid).Blackboard;
            Assert.That(blackboard.TryGetValue<System.TimeSpan>("RepositionedAt", out var latest, entManager) && latest > first,
                $"the operative should have looked for a new spot since {first}, at {timing.CurTime}");
        });
    }

    /// <summary>
    ///     Puts a loaded pistol, a round chambered, in the operative's hand.
    /// </summary>
    private static void StageArmed(IEntityManager entManager, EntityUid mobUid)
    {
        var handsSystem = entManager.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
        handsSystem.AddHand(mobUid, "right", Content.Shared.Hands.Components.HandLocation.Right);

        var gunUid = entManager.SpawnEntity("WeaponPistolMk58", entManager.GetComponent<TransformComponent>(mobUid).Coordinates);
        Assert.That(handsSystem.TryPickup(mobUid, gunUid, "right"), "the operative should be holding the pistol");

        // What the operative prototypes put on the blackboard for fighting with it, which the test mob lacks.
        var blackboard = entManager.GetComponent<HTNComponent>(mobUid).Blackboard;
        blackboard.SetValue("VisionRadius", 17.5f);
        blackboard.SetValue("AggroVisionRadius", 25f);
        blackboard.SetValue("RangedRange", 9f);
        blackboard.SetValue("MaxFiringDistance", 9f);
        blackboard.SetValue("CampingRange", 0.75f);
        blackboard.SetValue("RetreatClaimDuration", 10f);

        // Chamber a round, as racking would.
        entManager.System<Content.Shared.Weapons.Ranged.Systems.SharedGunSystem>().UseChambered(gunUid,
            entManager.GetComponent<Content.Shared.Weapons.Ranged.Components.ChamberMagazineAmmoProviderComponent>(gunUid), mobUid);
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
    private static void StageContact(IEntityManager entManager, EntityUid mobUid, EntityUid gridUid, NpcContactState state)
    {
        var hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 10, 10);
        var now = Robust.Shared.IoC.IoCManager.Resolve<IGameTiming>().CurTime;
        entManager.System<NpcPerceptionSystem>().SetContact(mobUid, hostileUid, new NpcContact(state,
            now, now, now, new EntityCoordinates(gridUid, new System.Numerics.Vector2(2.5f, 2.5f)), new System.Numerics.Vector2(1f, 0f),
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
        System.Action<IEntityManager, EntityUid, EntityUid>? stage = null)
    {
        var protoManager = Pair.Server.ResolveDependency<IPrototypeManager>();
        if (!protoManager.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var (entManager, mobUid) = await SetUpOperativeInRoom(mobPrototype);
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
                new HTNCompoundTask { Task = _operativeRoot },
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
    private async Task<(IEntityManager EntManager, EntityUid MobUid)> SetUpOperativeInRoom(string mobPrototype = SyndicateMob)
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
        });

        await Pair.RunTicksSync(150);

        return (entManager, mobUid);
    }

    private static string Describe(HTNPlan plan)
    {
        return string.Join(", ", plan.Tasks.Select(task => task.Operator.GetType().Name));
    }
}
