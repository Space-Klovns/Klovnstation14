using Content.Server._KS14.NPC.Recovery;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Recovery;

/// <summary>
///     Met while the owner is waiting for its meds to work (see <see cref="NpcRecoverySystem"/>).
/// </summary>
public sealed partial class RecoveringPrecondition : KsInvertiblePrecondition
{
    [Dependency] private NpcRecoverySystem _npcRecoverySystem = default!;

    protected override bool IsMetRaw(NPCBlackboard blackboard)
    {
        return _npcRecoverySystem.IsRecovering(blackboard.GetOwner());
    }
}
