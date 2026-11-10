using Content.Server._KS14.NPC.Execution;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Mobs.Systems;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Execution;

/// <summary>
///     Executes the entity at <see cref="TargetKey"/> with the weapon at <see cref="WeaponKey"/>, and waits for it to be
///         over (see <see cref="NpcExecutionSystem"/>). Fails if the target comes out of it alive. The owner has to be in
///         reach, and stay still until it is done.
/// </summary>
public sealed partial class ExecuteOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcExecutionSystem _npcExecutionSystem = default!;

    [DataField]
    public string TargetKey = "Target";

    [DataField]
    public string WeaponKey = "ExecutionWeapon";

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        if (blackboard.TryGetEntity(TargetKey, _entityManager, out var targetUid) &&
            blackboard.TryGetEntity(WeaponKey, _entityManager, out var weaponUid))
            _npcExecutionSystem.TryStartExecution(blackboard.GetOwner(), targetUid, weaponUid);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (_npcExecutionSystem.IsExecuting(blackboard.GetOwner()))
            return HTNOperatorStatus.Continuing;

        return blackboard.TryGetEntity(TargetKey, _entityManager, out var targetUid) && _mobStateSystem.IsDead(targetUid)
            ? HTNOperatorStatus.Finished
            : HTNOperatorStatus.Failed;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        _npcExecutionSystem.FinishExecution(blackboard.GetOwner());
    }
}
