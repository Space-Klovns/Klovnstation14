using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;

namespace Content.Client._KS14.Trail;

public sealed partial class KsTrailOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private SystemCollectionHookManager _hookManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        _hookManager.HookAction(OnDependencyAvailable);
    }

    private void OnDependencyAvailable(IDependencyCollection dependencyCollection)
    {
        var overlay = new KsTrailOverlay();

        dependencyCollection.InjectDependencies(overlay, oneOff: true);
        _overlayManager.AddOverlay(overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<KsTrailOverlay>();
    }
}
