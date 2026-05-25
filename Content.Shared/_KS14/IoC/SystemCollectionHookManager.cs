using Robust.Shared.Network;

namespace Content.Shared._KS14.IoC;

// TODO LCDC: somehow make engine PR to make this engine-based or otherwise publicly accessible

public sealed class SystemCollectionHookManager
{
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;
    private readonly ISawmill _sawmill = default!;

    public SystemCollectionHookManager()
    {
        _sawmill = Logger.GetSawmill("sys.collectionhook.man");
    }

    /// <inheritdoc cref="EntitySystemManager.DependencyCollection"/>
    [Access(Other = AccessPermissions.ReadExecute)]
    public IDependencyCollection DependencyCollection => _entitySystemManager.DependencyCollection;
    private Action<IDependencyCollection>? _onSystemCollectionAvailable = null;

    private bool _initalisedCollection = false;

    // Fuuckk
    private bool IsProperlyInitialised()
    {
        try
        {
            var get = DependencyCollection;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        return true;
    }

    public void TryInit()
    {
        if (_initalisedCollection)
            return;

        _sawmill.Info($"Collectionsysman initialised on {(_netManager.IsServer ? "server" : "client")}");
        _initalisedCollection = true;
        _onSystemCollectionAvailable?.Invoke(DependencyCollection);
    }

    public void Reset()
    {
        _sawmill.Info($"Collectionsysman reset on {(_netManager.IsServer ? "server" : "client")}");
        _initalisedCollection = false;
        _onSystemCollectionAvailable = null;
    }

    /// <summary>
    ///     Hooks an action to be called when the full <see cref="IDependencyCollection"/>
    ///         is available, calling it immediately if the collection
    ///         is already available.
    /// </summary>
    public void HookAction(Action act)
    {
        if (IsProperlyInitialised())
        {
            act();
            return;
        }

        _onSystemCollectionAvailable += (_) => act();
    }

    /// <inheritdoc cref="HookAction(Action)"/>
    public void HookAction(Action<IDependencyCollection> act)
    {
        if (IsProperlyInitialised())
        {
            act(DependencyCollection);
            return;
        }

        _onSystemCollectionAvailable += act;
    }
}
