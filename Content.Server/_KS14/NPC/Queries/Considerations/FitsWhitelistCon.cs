using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Content.Shared.Whitelist;

namespace Content.Server._KS14.NPC.Queries.Considerations;

public sealed partial class FitsWhitelistCon : UtilityConsideration
{
    [Dependency] private EntityWhitelistSystem _entityWhitelistSystem = default!;

    [DataField] public EntityWhitelist? Whitelist = null;
    [DataField] public EntityWhitelist? Blacklist = null;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
        => (_entityWhitelistSystem.IsWhitelistPassOrNull(Whitelist, targetUid) && _entityWhitelistSystem.IsWhitelistFailOrNull(Blacklist, targetUid)) ? 1f : 0f;
}
