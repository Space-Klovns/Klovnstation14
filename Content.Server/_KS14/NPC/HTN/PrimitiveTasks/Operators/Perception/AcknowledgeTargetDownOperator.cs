using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Perception;

/// <summary>
///     The owner has called out the death it confirmed: <c>TargetDownPrecondition</c> stops being met. When the task
///         runs, not when it is planned.
/// </summary>
public sealed partial class AcknowledgeTargetDownOperator : HTNOperator
{
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        _npcPerceptionSystem.ClearPendingKillCallout(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Finished;
    }
}
