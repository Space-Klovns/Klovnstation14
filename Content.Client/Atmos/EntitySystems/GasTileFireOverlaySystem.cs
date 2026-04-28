using Content.Client.Atmos.Overlays;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client.Atmos.EntitySystems;

/// <summary>
/// System responsible for rendering atmos fire animations using <see cref="GasTileFireOverlay"/>.
/// </summary>
[UsedImplicitly]
public sealed class GasTileFireOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] private readonly _KS14.CanisterOverlay.CanisterOverlaySystem _canisterOverlaySystem = default!; // KS14: Canister overlay

    private GasTileFireOverlay _fireOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _fireOverlay = new GasTileFireOverlay();
        _canisterOverlaySystem.AddOverlay(_fireOverlay); // KS14: Canister overlay
        _overlayMan.AddOverlay(_fireOverlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayMan.RemoveOverlay<GasTileFireOverlay>();
    }
}
