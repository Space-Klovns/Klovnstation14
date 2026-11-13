using Content.Shared._KS14.Construction.Conditions;

namespace Content.Client._KS14.Construction.Conditions;

public sealed partial class KsNearbyEntitiesSystem : SharedKsNearbyEntitiesSystem
{
    /// <summary>
    ///     Item crafting is started and checked by the server, which tells the user what is missing.
    /// </summary>
    public override bool AllPresent(EntityUid user, List<KsNearbyEntityEntry> entries)
    {
        return true;
    }
}
