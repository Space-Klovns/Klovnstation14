#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Squad;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
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
///     Also covers sensor data forcing an immediate replan.
/// </summary>
public sealed class KsOperativeHtnRootTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // Not static: the YAML linter validates every static ProtoId field against the client's prototypes too, and
    //      the client has no HTN prototypes at all, so a static one fails the lint.
    private readonly ProtoId<HTNCompoundPrototype> _operativeRoot = "KsOperativeCombatCompound";

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
        bool squadThreat = false)
    {
        var protoManager = Pair.Server.ResolveDependency<IPrototypeManager>();
        if (!protoManager.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var (entManager, mobUid) = await SetUpOperativeInRoom();
        var sensorSystem = entManager.System<NpcSensorSystem>();
        var squadSystem = entManager.System<NpcSquadSystem>();
        var timing = Pair.Server.ResolveDependency<IGameTiming>();

        HTNPlanJob job = default!;

        await Pair.Server.WaitPost(() =>
        {
            if (pendingSensorData)
                sensorSystem.AddEffect(mobUid, "__Sensor__KsTest", true);

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
    private async Task<(IEntityManager EntManager, EntityUid MobUid)> SetUpOperativeInRoom()
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

            mobUid = SpawnAt(entManager, SyndicateMob, gridUid, 4, 3);
        });

        await Pair.RunTicksSync(150);

        return (entManager, mobUid);
    }

    private static string Describe(HTNPlan plan)
    {
        return string.Join(", ", plan.Tasks.Select(task => task.Operator.GetType().Name));
    }
}
