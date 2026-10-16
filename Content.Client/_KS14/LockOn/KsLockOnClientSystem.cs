using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;
using Robust.Shared.IoC;

namespace Content.Client._KS14.LockOn;

/// <summary>
///     Registers <see cref="KsLockOnIndicatorOverlay"/>. The lock sounds are played by the server, from the shooter.
/// </summary>
public sealed partial class KsLockOnClientSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        var overlay = new KsLockOnIndicatorOverlay();
        dependencyCollection.InjectDependencies(overlay, oneOff: true);

        _overlayManager.AddOverlay(overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay<KsLockOnIndicatorOverlay>();
    }
}
