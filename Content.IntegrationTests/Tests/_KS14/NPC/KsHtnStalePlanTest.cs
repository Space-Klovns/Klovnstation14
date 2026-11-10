#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server._KS14.NPC.Meters;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     <see cref="HTNSystem"/> asks for a new plan at the top of an update, from the blackboard as it is then, and runs
///         the current plan after. A plan that ends in that same update - having changed the blackboard - leaves the
///         request it did not wait for planning from before its changes.
/// </summary>
public sealed class KsHtnStalePlanTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Meter = "KsHtnStaleTestMeter";
    private const string Marker = "KsHtnStaleTestMarker";

    /// <summary>
    ///     A plan that counts once and removes the marker it needs. Replanning on every update, so a request is always
    ///         made in the update the plan ends.
    /// </summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: npcMeter
  id: KsHtnStaleTestMeter
  max: 100
  decayPerSecond: 0

- type: htnCompound
  id: KsHtnStaleTestRoot
  branches:
  - preconditions:
    - !type:HasVirtualMarkerPrecondition
      id: KsHtnStaleTestMarker
    tasks:
    - !type:HTNPrimitiveTask
      operator: !type:AddMeterOperator
        meter: KsHtnStaleTestMeter
        amount: 1
    - !type:HTNPrimitiveTask
      operator: !type:RemoveVirtualMarkerOperator
        id: KsHtnStaleTestMarker
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:NoOperator

- type: entity
  id: KsHtnStaleTestMob
  components:
  - type: MobState
  - type: HTN
    enabled: false
    planCooldown: 0
    rootTask:
      task: KsHtnStaleTestRoot
";

    /// <summary>
    ///     Removing the marker ends the plan's chance to run: it runs once. A plan worked out from before the marker was
    ///         removed ran it again - the leader calling a stand-down, and calling it a second time.
    /// </summary>
    [Test]
    public async Task TestPlanThatEndedIsNotRunAgainFromBeforeItRan()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid mobUid = default;

        await server.WaitPost(() =>
        {
            mobUid = entManager.SpawnEntity("KsHtnStaleTestMob", map.GridCoords);
            var htnComponent = entManager.GetComponent<HTNComponent>(mobUid);
            htnComponent.Blackboard.SetValue(EnsureVirtualMarkerOperator.MarkerSet, new HashSet<string> { Marker });
            entManager.System<HTNSystem>().SetHTNEnabled((mobUid, htnComponent), true);
            entManager.System<NPCSystem>().WakeNPC(mobUid, htnComponent);
        });

        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
            Assert.That(entManager.System<NpcMeterSystem>().GetValue(mobUid, Meter), Is.EqualTo(1f).Within(0.01f),
                "the plan should have run once"));
    }

    /// <summary>
    ///     Coordinates on a target outlast it. One deleted between planning and the move starting, as a body is by an
    ///         acidifier, leaves nowhere to go: the move fails, so the NPC plans again, rather than logging an error or
    ///         being taken for done by steering left over from a move in the background.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TestMoveToATargetDeletedSincePlanningFails(bool steeringLeftOver)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mobUid = entManager.SpawnEntity("KsHtnStaleTestMob", map.GridCoords);
            var targetUid = entManager.SpawnEntity(null, map.GridCoords);

            if (steeringLeftOver)
                entManager.System<NPCSteeringSystem>().Register(mobUid, map.GridCoords);

            var moveToOperator = new MoveToOperator();
            moveToOperator.Initialize(entManager.EntitySysManager);

            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, mobUid);
            blackboard.SetValue(moveToOperator.TargetKey, new EntityCoordinates(targetUid, Vector2.Zero));
            blackboard.SetValue(moveToOperator.PathfindKey, new PathResultEvent(PathResult.Path, new List<PathPoly>()));

            entManager.DeleteEntity(targetUid);

            moveToOperator.Startup(blackboard);

            Assert.That(moveToOperator.Update(blackboard, 0f), Is.EqualTo(HTNOperatorStatus.Failed));
        });
    }

    /// <summary>
    ///     A key left by the plan before can pass a check while planning, and be gone by the time the move starts, when that
    ///         plan's own move shuts down and takes it along. There is nowhere to go, and the move fails as above, rather
    ///         than throwing and taking the server's tick with it.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TestMoveToCoordinatesGoneSincePlanningFails(bool steeringLeftOver)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mobUid = entManager.SpawnEntity("KsHtnStaleTestMob", map.GridCoords);

            if (steeringLeftOver)
                entManager.System<NPCSteeringSystem>().Register(mobUid, map.GridCoords);

            var moveToOperator = new MoveToOperator();
            moveToOperator.Initialize(entManager.EntitySysManager);

            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, mobUid);

            moveToOperator.Startup(blackboard);

            Assert.That(moveToOperator.Update(blackboard, 0f), Is.EqualTo(HTNOperatorStatus.Failed));
        });
    }
}
