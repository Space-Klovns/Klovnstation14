using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     An operator that does its one thing to the owner when the task starts, and is finished at once. It does it when
///         the task runs, not when it is planned: planning has no side effects (the npc-htn skill).
/// </summary>
public abstract partial class KsInstantOperator : HTNOperator
{
    public sealed override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        OnStartup(blackboard.GetOwner(), blackboard);
    }

    public sealed override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Finished;
    }

    /// <summary>
    ///     Does the work, once.
    /// </summary>
    protected abstract void OnStartup(EntityUid ownerUid, NPCBlackboard blackboard);
}
