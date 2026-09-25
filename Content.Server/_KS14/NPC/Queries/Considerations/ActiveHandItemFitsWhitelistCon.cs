using Content.Server.Hands.Systems;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Content.Shared.Whitelist;

namespace Content.Server._KS14.NPC.Queries.Considerations;

public sealed partial class ActiveHandItemFitsWhitelistCon : UtilityConsideration
{
    [Dependency] private HandsSystem _handsSystem = default!;
    [Dependency] private EntityWhitelistSystem _entityWhitelistSystem = default!;

    [DataField] public EntityWhitelist? Whitelist = null;
    [DataField] public EntityWhitelist? Blacklist = null;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        if (!_handsSystem.TryGetActiveItem(targetUid, out var activeItemUid))
            return 0f;

        return (_entityWhitelistSystem.IsWhitelistPassOrNull(Whitelist, activeItemUid.Value) && _entityWhitelistSystem.IsWhitelistFailOrNull(Blacklist, activeItemUid.Value)) ? 1f : 0f;
    }
}
