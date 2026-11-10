using Content.Server._KS14.NPC.Doors;
using Content.Server.NPC;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Doors;

/// <summary>
///     The owner has said a door refused it: <c>DoorRefusedPrecondition</c> stops being met. When the task runs, not
///         when it is planned.
/// </summary>
public sealed partial class AcknowledgeDoorRefusedOperator : KsInstantOperator
{
    [Dependency] private NpcDoorSystem _npcDoorSystem = default!;

    protected override void OnStartup(EntityUid ownerUid, NPCBlackboard blackboard)
    {
        _npcDoorSystem.ClearPendingRefusal(ownerUid);
    }
}
