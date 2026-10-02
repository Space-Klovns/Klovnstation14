using Content.Server._KS14.NPC.PlayDead;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions.PlayDead;

/// <summary>
///     Met while the owner should be playing dead (see <see cref="NpcPlayDeadSystem"/>). Recheck it on the task doing
///         the playing, so the owner gets up the moment it should.
/// </summary>
public sealed partial class PlayingDeadPrecondition : HTNPrecondition
{
    [Dependency] private NpcPlayDeadSystem _npcPlayDeadSystem = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        return _npcPlayDeadSystem.IsPlayingDead(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }
}
