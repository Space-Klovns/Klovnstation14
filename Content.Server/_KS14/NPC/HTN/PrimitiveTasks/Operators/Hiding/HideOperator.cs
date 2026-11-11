using Content.Server._KS14.NPC.Hiding;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Hiding;

/// <summary>
///     Stays in the closet it got into until <see cref="NpcHidingSystem"/> lets it out. However the task ends, it gets out:
///         nothing is left shut in a closet by a plan that was cut short.
/// </summary>
public sealed partial class HideOperator : HTNOperator
{
    [Dependency] private NpcHidingSystem _npcHidingSystem = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return _npcHidingSystem.IsHiding(blackboard.GetOwner()) ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Finished;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        _npcHidingSystem.StopHiding(blackboard.GetOwner());
    }
}
