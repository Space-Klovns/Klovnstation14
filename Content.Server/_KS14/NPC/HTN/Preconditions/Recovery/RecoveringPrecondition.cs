using Content.Server._KS14.NPC.Recovery;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Recovery;

/// <summary>
///     Met while the owner is waiting for its meds to work (see <see cref="NpcRecoverySystem"/>).
/// </summary>
public sealed partial class RecoveringPrecondition : HTNPrecondition
{
    [Dependency] private NpcRecoverySystem _npcRecoverySystem = default!;

    [DataField]
    public bool Invert;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        return _npcRecoverySystem.IsRecovering(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner)) != Invert;
    }
}
