using Content.Server.NPC.Queries.Considerations;
using Content.Shared.Whitelist;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     A consideration of whether an entity is on the <see cref="Whitelist"/> and off the <see cref="Blacklist"/>,
///         either of which is skipped if unset.
/// </summary>
public abstract partial class KsWhitelistCon : UtilityConsideration
{
    [Dependency] protected EntityWhitelistSystem EntityWhitelistSystem = default!;

    [DataField] public EntityWhitelist? Whitelist = null;
    [DataField] public EntityWhitelist? Blacklist = null;

    /// <summary>
    ///     Whether <paramref name="uid"/> passes <see cref="Whitelist"/> and <see cref="Blacklist"/>.
    /// </summary>
    protected bool Passes(EntityUid uid)
    {
        return EntityWhitelistSystem.IsWhitelistPassOrNull(Whitelist, uid) && EntityWhitelistSystem.IsWhitelistFailOrNull(Blacklist, uid);
    }
}
