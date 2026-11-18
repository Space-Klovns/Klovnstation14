using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._KS14.MedicalBeam;

/// <summary>
///     Keeps <see cref="KsMedicalBeamOverlay"/> up for as long as the client runs.
/// </summary>
public sealed partial class KsMedicalBeamOverlaySystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;

    private KsMedicalBeamOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new KsMedicalBeamOverlay(EntityManager, _gameTiming);
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
