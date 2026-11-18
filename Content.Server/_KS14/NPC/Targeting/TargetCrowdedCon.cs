using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;

namespace Content.Server._KS14.NPC.Targeting;

/// <summary>
///     0 if at least <see cref="Max"/> NPCs of the owner's side, the owner aside, are already fighting the target, else
///         1 (<see cref="NpcTargetTrackingSystem"/>, up to half a second old). With a <c>BoolCurve</c>'s
///         <c>lowScore</c> above 0, a crowded target is put behind the others rather than ruled out, so that a squad
///         spreads its fire over the hostiles it can see.
/// </summary>
public sealed partial class TargetCrowdedCon : UtilityConsideration
{
    [Dependency] private NpcTargetTrackingSystem _npcTargetTrackingSystem = default!;

    [DataField(required: true)]
    public int Max;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        return _npcTargetTrackingSystem.GetEngagingAllies(ownerUid, targetUid) >= Max ? 0f : 1f;
    }
}
