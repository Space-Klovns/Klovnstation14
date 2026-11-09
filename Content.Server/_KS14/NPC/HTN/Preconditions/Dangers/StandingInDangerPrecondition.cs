using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Server.NPC.Systems;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Dangers;

/// <summary>
///     Met while the owner is stopped on a tile posing what it avoids (see <see cref="NPCSteeringSystem.IsStandingInDanger"/>).
/// </summary>
public sealed partial class StandingInDangerPrecondition : HTNPrecondition
{
    [Dependency] private NPCSteeringSystem _npcSteeringSystem = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        return _npcSteeringSystem.IsStandingInDanger(blackboard.GetOwner());
    }
}
