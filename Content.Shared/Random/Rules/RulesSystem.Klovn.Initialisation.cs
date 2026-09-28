using Content.Shared._KS14.IoC;

namespace Content.Shared.Random.Rules;

public abstract partial class RulesRule
{
    private bool _initialised = false;

    public virtual bool TryInitialise(EntityManager entityManager)
    {
        if (_initialised)
            return true;

        if (IoCManager.Instance?.TryResolveType<SystemCollectionHookManager>(out var collectionHookManager) != true)
            return false;

        Initialise(collectionHookManager!.DependencyCollection);
        return true;
    }

    public virtual void Initialise(IDependencyCollection dependencyCollection)
    {
        dependencyCollection.InjectDependencies(this, oneOff: true);
        _initialised = true;
    }
}
