using Robust.Client.Graphics;

namespace Content.Client._KS14.CanisterOverlay;

public sealed partial class CanisterOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlayManager.AddOverlay(new CanisterOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay<CanisterOverlay>();
    }
}
