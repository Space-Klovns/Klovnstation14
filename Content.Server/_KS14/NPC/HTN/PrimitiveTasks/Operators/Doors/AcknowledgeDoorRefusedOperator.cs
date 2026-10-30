using Content.Server._KS14.NPC.Doors;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Doors;

/// <summary>
///     The owner has said a door refused it: <c>DoorRefusedPrecondition</c> stops being met. When the task runs, not
///         when it is planned.
/// </summary>
public sealed partial class AcknowledgeDoorRefusedOperator : HTNOperator
{
    [Dependency] private NpcDoorSystem _npcDoorSystem = default!;

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        _npcDoorSystem.ClearPendingRefusal(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Finished;
    }
}
