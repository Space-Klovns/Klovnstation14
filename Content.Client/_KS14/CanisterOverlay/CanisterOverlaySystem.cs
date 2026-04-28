using Content.Client.Atmos.Overlays;
using Robust.Client.Graphics;

namespace Content.Client._KS14.CanisterOverlay;

public sealed class CanisterOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;

    /* TODO LCDC KILL THIS VVV */
    private CanisterOverlay _canisterOverlay = null!;

    private GasTileVisibleGasOverlay _gasTileOverlay = null!;
    private GasTileFireOverlay _fireTileOverlay = null!;

    public void AddOverlay(GasTileVisibleGasOverlay otherOverlay)
    {
        _gasTileOverlay = otherOverlay;

        if (_fireTileOverlay != null &&
            _canisterOverlay != null)
            _canisterOverlay.InitialiseOverlays(_gasTileOverlay, _fireTileOverlay);
    }

    public void AddOverlay(GasTileFireOverlay otherOverlay)
    {
        _fireTileOverlay = otherOverlay;

        if (_gasTileOverlay != null &&
            _canisterOverlay != null)
            _canisterOverlay.InitialiseOverlays(_gasTileOverlay, _fireTileOverlay);
    }
    /* TODO LCDC KILL THIS ^^^ */

    public override void Initialize()
    {
        base.Initialize();

        _canisterOverlay = new CanisterOverlay();
        _overlayManager.AddOverlay(_canisterOverlay);

        if (_gasTileOverlay != null &&
            _fireTileOverlay != null)
            _canisterOverlay.InitialiseOverlays(_gasTileOverlay, _fireTileOverlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay<CanisterOverlay>();
    }
}
