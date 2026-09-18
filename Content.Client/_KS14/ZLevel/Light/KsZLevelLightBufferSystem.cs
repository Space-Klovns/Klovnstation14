using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ZLevel.Light;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Map;

namespace Content.Client._KS14.ZLevel.Light;

/// <summary>
///     Decides how light crosses z-levels, and carries the captured light maps from the viewport that takes
///         them to the overlay that uses them.
/// </summary>
/// <remarks>
///     The capture render targets themselves belong to the ScalingViewport, because it is the only thing that
///         can drive the extra render passes and the only thing that knows when they are invalid. This just
///         holds a pointer to whichever viewport is mid-draw, which is safe because controls draw one at a
///         time and the pointer is put back before the draw returns.
/// </remarks>
public sealed partial class KsZLevelLightBufferSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;

    /// <summary>
    ///     How light crosses z-levels right now.
    /// </summary>
    public KsZLevelLightLeakMode Mode { get; private set; } = KsZLevelLightLeakMode.StandIns;

    /// <summary>
    ///     Whether the light maps of the z-levels above are wanted at all this frame.
    /// </summary>
    public bool WantsCaptures => Mode is KsZLevelLightLeakMode.Buffer or KsZLevelLightLeakMode.Both;

    /// <summary>
    ///     Draw the captured light as a flat colour, to prove the compositing before trusting the contents.
    /// </summary>
    public bool DebugFlat { get; private set; }

    private IReadOnlyDictionary<MapId, KsZLevelLightCapture>? _activeCaptures;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.ZLevelLightLeakMode, OnModeChanged, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelLightBufferDebugFlat, value => DebugFlat = value, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _activeCaptures = null;
        _overlayManager.RemoveOverlay<KsZLevelLightBufferOverlay>();
    }

    private void OnModeChanged(string value)
    {
        if (!Enum.TryParse<KsZLevelLightLeakMode>(value, ignoreCase: true, out var mode))
        {
            Log.Warning(
                $"Unrecognised z-level light leak mode '{value}'; falling back to {KsZLevelLightLeakMode.StandIns}. " +
                $"Expected one of {string.Join(", ", Enum.GetNames<KsZLevelLightLeakMode>())}.");

            mode = KsZLevelLightLeakMode.StandIns;
        }

        Mode = mode;

        // The overlay is what actually composites, so its presence is the switch. Kept in step here rather
        //      than checked per draw so that turning the mode off stops the work entirely.
        if (WantsCaptures)
            _overlayManager.AddOverlay(new KsZLevelLightBufferOverlay());
        else
            _overlayManager.RemoveOverlay<KsZLevelLightBufferOverlay>();
    }

    /// <summary>
    ///     Points the overlay at the captures belonging to the viewport that is about to draw.
    /// </summary>
    /// <remarks>
    ///     Always paired with a null in a finally: a stale pointer would have one viewport lighting itself
    ///         from another's z-levels, at another's scale.
    /// </remarks>
    public void SetActiveCaptures(IReadOnlyDictionary<MapId, KsZLevelLightCapture>? captures)
    {
        _activeCaptures = captures;
    }

    /// <summary>
    ///     The light map of a z-level, if one was captured for this draw.
    /// </summary>
    public bool TryGetCapture(MapId mapId, out KsZLevelLightCapture capture)
    {
        capture = default!;

        return _activeCaptures is { } captures && captures.TryGetValue(mapId, out capture!);
    }
}
