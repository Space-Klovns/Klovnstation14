using Content.Server.Hands.Systems;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Content.Shared.Hands.Components;
using Content.Shared.Whitelist;

namespace Content.Server._KS14.NPC.Queries.Considerations;

public sealed partial class AnyItemInHandsFitsWhitelistCon : UtilityConsideration
{
    [Dependency] private HandsSystem _handsSystem = default!;
    [Dependency] private EntityWhitelistSystem _entityWhitelistSystem = default!;

    [DataField] public EntityWhitelist? Whitelist = null;
    [DataField] public EntityWhitelist? Blacklist = null;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        if (!EntityManager.TryGetComponent<HandsComponent>(targetUid, out var handsComponent))
            return 0f;

        foreach (var handId in _handsSystem.EnumerateHands((targetUid, handsComponent)))
        {
            if (!_handsSystem.TryGetHeldItem((targetUid, handsComponent), handId, out var heldItemUid) ||
                !Passes(heldItemUid.Value))
                continue;

            return 1f;
        }

        return 0f;
    }

    private bool Passes(EntityUid uid)
        => _entityWhitelistSystem.IsWhitelistPassOrNull(Whitelist, uid) && _entityWhitelistSystem.IsWhitelistFailOrNull(Blacklist, uid);
}
