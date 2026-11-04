using Content.Server._KS14.NPC.Recovery;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Recovery;

/// <summary>
///     Starts the owner waiting for the meds it has just used to work (see <see cref="NpcRecoverySystem"/>). Goes after the
///         task injecting them, so a plan cut short before that starts nothing.
/// </summary>
public sealed partial class StartRecoveryOperator : HTNOperator
{
    [Dependency] private NpcRecoverySystem _npcRecoverySystem = default!;

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        _npcRecoverySystem.StartRecovery(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Finished;
    }
}
