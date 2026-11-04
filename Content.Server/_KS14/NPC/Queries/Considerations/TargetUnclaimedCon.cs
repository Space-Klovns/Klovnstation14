using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     1 if no squadmate of the owner has claimed the target, 0 if one has: one to a target, for jobs that only take one.
///         See <see cref="NpcTargetClaimSystem"/>.
/// </summary>
public sealed partial class TargetUnclaimedCon : UtilityConsideration
{
    [Dependency] private NpcTargetClaimSystem _npcTargetClaimSystem = default!;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        return _npcTargetClaimSystem.IsClaimedBySquadmate(ownerUid, targetUid) ? 0f : 1f;
    }
}
