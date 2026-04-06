using Content.Client._KS14.CanisterOverlay; // KS14
using Content.Client.Atmos.Overlays;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client.Atmos.EntitySystems;

/// <summary>
///     System responsible for rendering visible atmos gasses (like plasma for example) using <see cref="GasTileVisibleGasOverlay"/>.
/// </summary>
[UsedImplicitly]
public sealed class GasTileVisibleGasOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayMan = default!;

    private GasTileVisibleGasOverlay _visibleGasOverlay = default!;
    private CanisterOverlay _canisterOverlay = default!; // KS14

    public override void Initialize()
    {
        base.Initialize();

        _visibleGasOverlay = new GasTileVisibleGasOverlay();
        _overlayMan.AddOverlay(_visibleGasOverlay);

        _canisterOverlay = new CanisterOverlay(_visibleGasOverlay); // KS14
        _overlayMan.AddOverlay(_canisterOverlay); // KS14
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayMan.RemoveOverlay<GasTileVisibleGasOverlay>();
        _overlayMan.RemoveOverlay<CanisterOverlay>(); // KS14
    }

}
