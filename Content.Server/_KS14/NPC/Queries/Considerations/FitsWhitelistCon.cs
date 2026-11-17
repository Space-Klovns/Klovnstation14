using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;

namespace Content.Server._KS14.NPC.Queries.Considerations;

public sealed partial class FitsWhitelistCon : KsWhitelistCon
{
    /// <summary>
    ///     Checks the owner rather than the target: "only for NPCs that do this", inside a query shared with those that do
    ///         not.
    /// </summary>
    [DataField] public bool CheckOwner;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        var checkedUid = CheckOwner ? ownerUid : targetUid;
        return Passes(checkedUid) ? 1f : 0f;
    }
}
