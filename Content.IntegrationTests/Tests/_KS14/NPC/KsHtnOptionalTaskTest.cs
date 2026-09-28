#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     The planner's <c>optional</c> knob: an optional task that cannot plan is skipped and its branch carries
///         on, while the same task without the knob still fails the branch and falls through to the next one.
/// </summary>
public sealed class KsHtnOptionalTaskTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: htnCompound
  id: KsOptionalTestPrimitiveRoot
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      optional: true
      preconditions:
      - !type:KeyExistsPrecondition
        key: KsOptionalTestMissingKey
      operator: !type:NoOperator
    - !type:HTNPrimitiveTask
      operator: !type:NoOperator
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:NoOperator

- type: htnCompound
  id: KsOptionalTestRequiredRoot
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      preconditions:
      - !type:KeyExistsPrecondition
        key: KsOptionalTestMissingKey
      operator: !type:NoOperator
    - !type:HTNPrimitiveTask
      operator: !type:NoOperator
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:NoOperator

- type: htnCompound
  id: KsOptionalTestCompoundRoot
  branches:
  - tasks:
    - !type:HTNCompoundTask
      optional: true
      task: KsOptionalTestUnplannable
    - !type:HTNPrimitiveTask
      operator: !type:NoOperator
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:NoOperator

- type: htnCompound
  id: KsOptionalTestUnplannable
  branches:
  - preconditions:
    - !type:KeyExistsPrecondition
      key: KsOptionalTestMissingKey
    tasks:
    - !type:HTNPrimitiveTask
      operator: !type:NoOperator
";

    [Test]
    public async Task TestOptionalPrimitiveIsSkipped()
    {
        var plan = await PlanFor("KsOptionalTestPrimitiveRoot");

        Assert.That(plan, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(plan!.BranchTraversalRecord, Is.EqualTo(new List<int> { 0 }),
                "a skipped optional primitive must not fail its branch");
            Assert.That(plan.Tasks, Has.Count.EqualTo(1), "the skipped primitive must not be in the plan");
        });
    }

    [Test]
    public async Task TestRequiredPrimitiveStillFailsBranch()
    {
        var plan = await PlanFor("KsOptionalTestRequiredRoot");

        Assert.That(plan, Is.Not.Null);
        Assert.That(plan!.BranchTraversalRecord, Is.EqualTo(new List<int> { 1 }),
            "without optional, a primitive that cannot plan fails its branch as before");
    }

    [Test]
    public async Task TestOptionalCompoundIsSkipped()
    {
        var plan = await PlanFor("KsOptionalTestCompoundRoot");

        Assert.That(plan, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(plan!.BranchTraversalRecord, Is.EqualTo(new List<int> { 0 }),
                "a skipped optional compound must not fail its branch, nor add a traversal entry");
            Assert.That(plan.Tasks, Has.Count.EqualTo(1));
        });
    }

    private async Task<HTNPlan?> PlanFor(string rootCompound)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();

        HTNPlan? plan = null;

        await server.WaitPost(() =>
        {
            var ownerUid = entManager.SpawnEntity(null, MapCoordinates.Nullspace);
            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, ownerUid);

            var job = new HTNPlanJob(
                maxTime: 10,
                protoManager,
                new HTNCompoundTask { Task = rootCompound },
                blackboard,
                branchTraversal: null);

            // The job suspends once on start and again after every primitive it plans, so keep resuming it.
            for (var i = 0; i < 100 && job.Status != JobStatus.Finished; i++)
            {
                job.Run();
            }

            Assert.That(job.Status, Is.EqualTo(JobStatus.Finished));
            Assert.That(job.Exception, Is.Null);
            plan = job.Result;

            entManager.DeleteEntity(ownerUid);
        });

        return plan;
    }
}
