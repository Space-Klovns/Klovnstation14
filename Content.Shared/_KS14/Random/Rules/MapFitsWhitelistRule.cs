using Content.Shared.Random.Rules;
using Content.Shared.Whitelist;

namespace Content.Shared._KS14.Random.Rules;

/// <summary>
///     Returns true if the map fits a certain whitelist.
/// </summary>
public sealed partial class MapFitsWhitelistRule : RulesRule
{
    [Dependency] private EntityWhitelistSystem _entityWhitelistSystem = default!;

    [DataField]
    public EntityWhitelist Whitelist;

    public override bool Check(EntityManager entManager, EntityUid uid)
    {
        if (entManager.TransformQuery.GetComponent(uid).MapUid is not { } mapUid)
            return Inverted;

        TryInitialise(entManager);
        return _entityWhitelistSystem.IsWhitelistPass(Whitelist, mapUid);
    }
}
