using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Content.Shared.Whitelist;

namespace Content.Server._KS14.NPC.Queries.Considerations;

public sealed partial class FitsWhitelistCon : UtilityConsideration
{
    [Dependency] private EntityWhitelistSystem _entityWhitelistSystem = default!;

    [DataField] public EntityWhitelist? Whitelist = null;
    [DataField] public EntityWhitelist? Blacklist = null;

    /// <summary>
    ///     Checks the owner rather than the target: "only for NPCs that do this", inside a query shared with those that do
    ///         not.
    /// </summary>
    [DataField] public bool CheckOwner;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        var checkedUid = CheckOwner ? ownerUid : targetUid;
        return (_entityWhitelistSystem.IsWhitelistPassOrNull(Whitelist, checkedUid) && _entityWhitelistSystem.IsWhitelistFailOrNull(Blacklist, checkedUid)) ? 1f : 0f;
    }
}
