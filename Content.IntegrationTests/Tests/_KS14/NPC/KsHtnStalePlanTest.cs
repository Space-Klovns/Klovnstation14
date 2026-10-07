#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server._KS14.NPC.Meters;
using Content.Server.NPC.HTN;
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
}
