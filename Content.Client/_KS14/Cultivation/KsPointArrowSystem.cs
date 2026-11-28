using Content.Client._KS14.Cultivation.Overlays;
using Content.Shared._KS14.Cultivation;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Cultivation;

/// <summary>
///     Shows the arrow the server asks for: around the player, pointing at a place, for a few seconds.
/// </summary>
public sealed partial class KsPointArrowSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;

    private KsPointArrowOverlay? _overlay;

    public MapCoordinates? Target { get; private set; }
    public TimeSpan Until { get; private set; }
    public TimeSpan Since { get; private set; }
    public string Label { get; private set; } = string.Empty;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<KsPointArrowEvent>(OnPointArrow);
        _overlay = new KsPointArrowOverlay();
        _overlayManager.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        if (_overlay != null)
            _overlayManager.RemoveOverlay(_overlay);
    }

    private void OnPointArrow(KsPointArrowEvent args)
    {
        Target = args.Target;
        Label = args.Label;
        Since = _gameTiming.RealTime;
        Until = _gameTiming.RealTime + TimeSpan.FromSeconds(args.Seconds);
    }
}
