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
    [Dependency] private readonly _KS14.CanisterOverlay.CanisterOverlaySystem _canisterOverlaySystem = default!; // KS14: Canister overlay

    private GasTileVisibleGasOverlay _visibleGasOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _visibleGasOverlay = new GasTileVisibleGasOverlay();
        _canisterOverlaySystem.AddOverlay(_visibleGasOverlay); // KS14: Canister overlay
        _overlayMan.AddOverlay(_visibleGasOverlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayMan.RemoveOverlay<GasTileVisibleGasOverlay>();
    }

}
