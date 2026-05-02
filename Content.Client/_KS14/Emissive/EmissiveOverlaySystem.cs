using Robust.Client.Graphics;

namespace Content.Client._KS14.Emissive;

public sealed class EmissiveOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlayManager.AddOverlay(new EmissiveOverlay());
    }
    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<EmissiveOverlay>();
    }
}
