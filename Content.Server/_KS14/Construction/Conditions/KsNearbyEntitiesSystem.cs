using Content.Server.Construction;
using Content.Shared._KS14.Construction.Conditions;

namespace Content.Server._KS14.Construction.Conditions;

public sealed partial class KsNearbyEntitiesSystem : SharedKsNearbyEntitiesSystem
{
    [Dependency] private ConstructionSystem _constructionSystem = default!;

    /// <summary>
    ///     The same entities item crafting takes its materials from.
    /// </summary>
    public override bool AllPresent(EntityUid user, List<KsNearbyEntityEntry> entries)
    {
        return AllPresent(_constructionSystem.EnumerateNearby(user), entries);
    }
}
