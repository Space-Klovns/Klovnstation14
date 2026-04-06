namespace Content.Server._KS14.IoC;

public sealed class SystemCollectionHookSystem : EntitySystem
{
    [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;

    /// <inheritdoc cref="EntitySystemManager.DependencyCollection"/>
    [Access(Other = AccessPermissions.ReadExecute)]
    public IDependencyCollection DependencyCollection => _entitySystemManager.DependencyCollection;
    public Action<IDependencyCollection>? OnSystemCollectionAvailable = null;

    private bool _initialisedAllSystems = false;

    public override void Update(float frameTime) // Best i could do without reflection or some other bs
    {
        base.Update(frameTime);

        if (_initialisedAllSystems)
            return;

        OnSystemCollectionAvailable?.Invoke(DependencyCollection);
        _initialisedAllSystems = true;
    }

    public void HookAction(Action act) => OnSystemCollectionAvailable += (_) => act();
}
