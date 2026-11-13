using Content.Shared.Construction.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Client.Construction;

public sealed partial class ConstructionSystem
{
    /// <summary>
    ///     A recipe's name, description and target are worked out once, at startup. A hot reload of a recipe replaces its
    ///         prototype with one that has none of them, and the construction menu then lists it blank, so work them out
    ///         again whenever a recipe, a graph or the entity a recipe builds changes.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnKsPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<ConstructionPrototype>()
            && !args.WasModified<ConstructionGraphPrototype>()
            && !args.WasModified<EntityPrototype>())
        {
            return;
        }

        _recipesMetadataCache.Clear();
        WarmupRecipesCache();
    }
}
