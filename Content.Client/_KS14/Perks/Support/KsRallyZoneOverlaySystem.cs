using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Perks.Support;

/// <summary>
///     Keeps <see cref="KsRallyZoneOverlay"/> up for as long as the client runs.
/// </summary>
public sealed partial class KsRallyZoneOverlaySystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;

    private KsRallyZoneOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new KsRallyZoneOverlay(EntityManager, _gameTiming);
        _overlayManager.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_overlay != null)
            _overlayManager.RemoveOverlay(_overlay);
        _overlay = null;
    }
}
