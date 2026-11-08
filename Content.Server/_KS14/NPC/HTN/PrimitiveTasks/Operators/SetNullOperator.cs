using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Sets the value of the target key to null.
/// </summary>
public sealed partial class SetNullOperator : HTNOperator
{
    [DataField(required: true)] public string Key = "Target";

    /// <summary>
    ///     Removes the key when the plan ends, however it ends - finished, failed or replaced - rather than when this task
    ///         finishes. For something the plan starts that must not outlive it, such as a move that goes on in the
    ///         background: the task can come anywhere in the plan, and the key goes even if the plan never gets that far.
    /// </summary>
    [DataField]
    public bool OnPlanEnd;

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);

        if (!OnPlanEnd)
            blackboard.Remove<object>(Key);
    }

    public override void PlanShutdown(NPCBlackboard blackboard)
    {
        base.PlanShutdown(blackboard);

        if (OnPlanEnd)
            blackboard.Remove<object>(Key);
    }
}
