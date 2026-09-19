using Robust.Shared.Network;

namespace Content.Shared._KS14.IoC;

// TODO LCDC: somehow make engine PR to make this engine-based or otherwise publicly accessible

/// <summary>
///     Class that helps you retrieve <see cref="IEntitySystemManager.DependencyCollection"/>, which, upon being used to inject
///         into a class, will resolve <see cref="EntityQuery<>>"/> and <see cref="EntitySystem"/>, unlike
///         the <see cref="IDependencyCollection"/> used by the default <see cref="IoCManager"/>.
/// </summary>
public sealed partial class SystemCollectionHookManager
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private IEntitySystemManager _entitySystemManager = default!;
    private readonly ISawmill _sawmill = default!;

    public SystemCollectionHookManager()
    {
        _sawmill = Logger.GetSawmill("sys.collectionhook.man");
    }

    /// <summary>
    ///     Dependency collection that contains all loaded systems and component queries,
    ///         unlike that of <see cref="IoCManager"/>.
    /// </summary>
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
    /// <param name="act">Is given <see cref="SystemCollectionHookManager.DependencyCollection"/>: a dependency collection that already contains all loaded systems and component queries.</param>
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
