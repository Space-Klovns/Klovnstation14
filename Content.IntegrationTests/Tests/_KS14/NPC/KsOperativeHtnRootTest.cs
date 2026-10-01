#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Interactions;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Squad;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
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
///     Also covers sensor data forcing an immediate replan, and which of the perception-driven branches (search,
///         lost contact, callouts) may interrupt which.
/// </summary>
public sealed class KsOperativeHtnRootTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // Not static: the YAML linter validates every static ProtoId field against the client's prototypes too, and
    //      the client has no HTN prototypes at all, so a static one fails the lint.
    private readonly ProtoId<HTNCompoundPrototype> _operativeRoot = "KsOperativeCombatCompound";

    private const string HandsMob = "KsOperativeRootTestMobWithHands";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsOperativeRootTestMobWithHands
  components:
  - type: Hands
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
    ///     A hostile being fought that has just gone out of sight is watched for where it should be by now.
    /// </summary>
    [Test]
    public async Task TestLostHostileIsWatched()
    {
        var plan = await PlanOperative(pendingSensorData: false, recentlyFought: true,
            stage: (entManager, mobUid, gridUid) => StageContact(entManager, mobUid, gridUid, NpcContactState.Lost));

        Assert.That(plan.Tasks.Any(IsLostContactWatch), $"expected a lost contact watch, got: {Describe(plan)}");
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
    ///         cut short by anything below it, a hold gives way to a search or a lost contact, and a lost contact
    ///         watch is not cut short by a hold.
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
        var lost = await PlanOperative(pendingSensorData: false, recentlyFought: true,
            stage: (entManager, mobUid, gridUid) => StageContact(entManager, mobUid, gridUid, NpcContactState.Lost));

        Assert.Multiple(() =>
        {
            Assert.That(Replaces(search, hold), "a search should interrupt a hold");
            Assert.That(Replaces(lost, hold), "a lost contact should interrupt a hold");
            Assert.That(Replaces(hold, search), Is.False, "a hold must not interrupt a search");
            Assert.That(Replaces(lost, search), Is.False, "a lost contact must not interrupt a search");
            Assert.That(Replaces(sensors, search), Is.False, "sensor data must not interrupt a search");
            Assert.That(Replaces(hold, cover), Is.False, "a hold must not interrupt covering a locker");
            Assert.That(Replaces(lost, cover), Is.False, "a lost contact must not interrupt covering a locker");
            Assert.That(Replaces(hold, lost), Is.False, "a hold must not interrupt a lost contact watch");
            Assert.That(Replaces(sensors, lost), Is.False, "sensor data must not interrupt a lost contact watch");
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

    private static bool IsLostContactWatch(HTNPrimitiveTask task)
    {
        return task.Operator is StaticWaitOperator { DelayKey: "LostContactWatchTime" };
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
