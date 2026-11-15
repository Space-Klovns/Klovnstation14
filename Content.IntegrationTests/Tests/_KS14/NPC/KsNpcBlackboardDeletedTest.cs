#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     The blackboard never hands out an entity, or coordinates on one, that is deleted or being deleted: an operator
///         passing a gibbed target on to a system (<c>EnsureComp</c>) threw, every tick, and took the NPC update with it.
/// </summary>
public sealed class KsNpcBlackboardDeletedTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string TargetKey = "KsDeletedKeyTestTarget";

    [TestPrototypes]
    private const string Prototypes = @"
- type: htnCompound
  id: KsDeletedKeyTestExistsRoot
  branches:
  - preconditions:
    - !type:KeyExistsPrecondition
      key: KsDeletedKeyTestTarget
    tasks:
    - !type:HTNPrimitiveTask
      operator: !type:NoOperator
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:NoOperator

- type: htnCompound
  id: KsDeletedKeyTestNotExistsRoot
  branches:
  - preconditions:
    - !type:KeyNotExistsPrecondition
      key: KsDeletedKeyTestTarget
    tasks:
    - !type:HTNPrimitiveTask
      operator: !type:NoOperator
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:NoOperator
";

    /// <summary>
    ///     A branch gated on a key is taken while the entity the key names is there, and skipped for the branch below once it
    ///         is deleted, although the key is still on the blackboard: planned on, its task failed on reading the entity, and
    ///         the failure replanned straight back onto it, with everything below never getting a turn. A branch gated on the
    ///         key not being there is the other way about.
    /// </summary>
    /// <param name="coordinates">Whether the key holds coordinates on the entity, rather than the entity.</param>
    [TestCase("KsDeletedKeyTestExistsRoot", false, 0, 1)]
    [TestCase("KsDeletedKeyTestExistsRoot", true, 0, 1)]
    [TestCase("KsDeletedKeyTestNotExistsRoot", false, 1, 0)]
    public async Task TestABranchGatedOnADeletedEntityIsSkipped(string root, bool coordinates, int branchAlive, int branchDeleted)
    {
        var map = await Pair.CreateTestMap();
        var entManager = Pair.Server.ResolveDependency<IEntityManager>();
        var protoManager = Pair.Server.ResolveDependency<IPrototypeManager>();

        await Pair.Server.WaitAssertion(() =>
        {
            var ownerUid = entManager.SpawnAtPosition(null, map.GridCoords);
            var targetUid = entManager.SpawnAtPosition(null, map.GridCoords);
            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, ownerUid);
            blackboard.SetValue(TargetKey, coordinates ? new EntityCoordinates(targetUid, 0, 0) : targetUid);

            Assert.That(Plan(protoManager, root, blackboard), Is.EqualTo(new List<int> { branchAlive }), "the entity is there");

            entManager.DeleteEntity(targetUid);

            Assert.That(blackboard.ContainsKey(TargetKey), "the test needs the key still on the blackboard");
            Assert.That(Plan(protoManager, root, blackboard), Is.EqualTo(new List<int> { branchDeleted }),
                "a key naming a deleted entity is not there to plan on");
        });
    }

    /// <summary>
    ///     The branches the planner takes from <paramref name="root"/> on <paramref name="blackboard"/>.
    /// </summary>
    private static List<int> Plan(IPrototypeManager protoManager, string root, NPCBlackboard blackboard)
    {
        var job = new HTNPlanJob(
            maxTime: 10,
            protoManager,
            new HTNCompoundTask { Task = root },
            blackboard,
            branchTraversal: null);

        // The job suspends once on start and again after every primitive it plans, so keep resuming it.
        for (var i = 0; i < 100 && job.Status != JobStatus.Finished; i++)
        {
            job.Run();
        }

        Assert.That(job.Status, Is.EqualTo(JobStatus.Finished));
        Assert.That(job.Exception, Is.Null);
        Assert.That(job.Result, Is.Not.Null, "one of the two branches should plan");
        return job.Result!.BranchTraversalRecord;
    }

    [Test]
    public async Task TestDeletedEntitiesReadAsMissing()
    {
        var map = await Pair.CreateTestMap();
        var entManager = Pair.Server.ResolveDependency<IEntityManager>();

        await Pair.Server.WaitAssertion(() =>
        {
            var targetUid = entManager.SpawnAtPosition(null, map.GridCoords);
            var blackboard = new NPCBlackboard();
            blackboard.SetValue("Target", targetUid);
            blackboard.SetValue("TargetCoordinates", new EntityCoordinates(targetUid, 0, 0));
            blackboard.SetValue("Spot", map.GridCoords);
            blackboard.SetValue("Nothing", EntityUid.Invalid);

            Assert.Multiple(() =>
            {
                Assert.That(blackboard.TryGetEntity("Target", entManager, out _), "alive: there");
                Assert.That(blackboard.TryGetValue<EntityCoordinates>("TargetCoordinates", out _, entManager), "on it: there");
            });

            entManager.DeleteEntity(targetUid);

            Assert.Multiple(() =>
            {
                Assert.That(blackboard.TryGetEntity("Target", entManager, out _), Is.False, "deleted: missing");
                Assert.That(blackboard.ContainsKey("Target", entManager), Is.False, "and not there for a plan to be gated on");
                Assert.That(blackboard.ContainsKey("TargetCoordinates", entManager), Is.False);
                Assert.That(blackboard.ContainsKey("Spot", entManager), "what is still there is");
                Assert.That(blackboard.ContainsKey("Nothing", entManager), "invalid on purpose: as ever");
                Assert.That(blackboard.TryGetValue<EntityCoordinates>("TargetCoordinates", out _, entManager), Is.False,
                    "coordinates on a deleted entity: missing");
                Assert.That(blackboard.GetValueOrDefault<EntityUid>("Target", entManager), Is.EqualTo(default(EntityUid)));
                Assert.That(blackboard.TryGetValue<EntityCoordinates>("Spot", out _, entManager), "on the grid: still there");
                Assert.That(blackboard.TryGetValue<EntityUid>("Nothing", out _, entManager), "invalid on purpose: as ever");
            });
        });
    }
}
