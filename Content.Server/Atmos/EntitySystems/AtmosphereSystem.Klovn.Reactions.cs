// KS14: added in this fork
using Content.Server.Atmos.Reactions;
using Content.Shared._KS14.IoC;
using Robust.Shared.Prototypes;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    private void InitialiseKlovnReactions()
    {
        _systemCollectionHookManager.HookAction(KsUpdateGasReactionPrototypes);
    }

    private void KsUpdateGasReactionPrototypes(IDependencyCollection dependencyCollection)
    {
        foreach (var gasReactionPrototype in _protoMan.EnumeratePrototypes<GasReactionPrototype>())
        {
            foreach (var effect in gasReactionPrototype.GetEffects())
                dependencyCollection.InjectDependencies(effect, oneOff: true);
        }
    }

    private void KsOnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.Modified.Contains(typeof(GasReactionPrototype)))
            return;

        KsUpdateGasReactionPrototypes(_systemCollectionHookManager.DependencyCollection);

        // KS14: reload replaces GasReactionPrototype instances outright (PrototypeManager.ReloadPrototypes),
        // so _gasReactions must be recaptured or it keeps iterating orphaned, un-injected prototypes
        RefreshGasReactionsCache();
    }
}
