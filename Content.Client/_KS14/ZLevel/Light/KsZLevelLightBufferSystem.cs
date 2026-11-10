using System.Numerics;
using Content.Client.Light;
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
    ///     Whether light crosses z-levels at all right now, which is the viewer's to decide.
    /// </summary>
    public bool LeakEnabled { get; private set; } = true;

    /// <summary>
    ///     Whether the light maps of the z-levels above are wanted at all this frame.
    /// </summary>
    public bool WantsCaptures => LeakEnabled && Mode is KsZLevelLightLeakMode.Buffer or KsZLevelLightLeakMode.Both;

    /// <summary>
    ///     Whether stand-in point lights are wanted at all this frame.
    /// </summary>
    /// <remarks>
    ///     Held here beside <see cref="WantsCaptures"/> rather than on the system that spawns them, because
    ///         both answers are made of the same two inputs and the pair only makes sense read together.
    /// </remarks>
    public bool WantsStandIns => LeakEnabled && Mode is KsZLevelLightLeakMode.StandIns or KsZLevelLightLeakMode.Both;

    /// <summary>
    ///     Whether the z-level directly above the viewer is worth rendering for its light map.
    /// </summary>
    /// <remarks>
    ///     Gated on the server cvar, replicated for exactly this, because the two are inseparable: the stack
    ///         is only ever drawn downwards, so the only lights and occluders the level above can be built
    ///         from are the ones PVS sent. With that off, capturing it renders a map the client has been
    ///         detached from - a black light map and an empty floor mask - once per frame, forever.
    /// </remarks>
    public bool WantsLightFromAbove => WantsCaptures && _sendAbove;

    /// <summary>
    ///     How much wider than the light target the enlarged target composited into is, as a ratio, so that
    ///         captures can be taken wide enough to fill it.
    /// </summary>
    /// <remarks>
    ///     <see cref="LightBlurOverlay"/> blurs that target with a multiplier of 70, which works out at
    ///         something like a tenth of the screen, and it samples the skirt around the edges to do it. The
    ///         skirt is where every other contributor's light spills in from off-screen - but a capture is a
    ///         picture with a hard edge, so if it stops at the visible bounds the blur drags its outermost
    ///         tenth back down to black. That is a wide, smooth fade around the whole screen.
    ///     Measured by the overlay, because it is the only thing holding both targets, and read back by the
    ///         viewport for the frame after. A frame of lag costs nothing: it only changes when the window,
    ///         the render scale or the light resolution does.
    /// </remarks>
    public Vector2 CaptureOversize { get; set; } = Vector2.One;

    /// <summary>
    ///     Draw the captured light as a flat colour, to prove the compositing before trusting the contents.
    /// </summary>
    public bool DebugFlat { get; private set; }

    private bool _sendAbove;

    private IReadOnlyDictionary<MapId, KsZLevelLightCapture>? _activeCaptures;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.ZLevelLightLeakMode, OnModeChanged, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelLightLeakEnabled, OnLeakEnabledChanged, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelLightBufferDebugFlat, value => DebugFlat = value, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelPvsSendAbove, value => _sendAbove = value, true);
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
        RefreshOverlay();
    }

    private void OnLeakEnabledChanged(bool enabled)
    {
        LeakEnabled = enabled;
        RefreshOverlay();
    }

    /// <summary>
    ///     The overlay is what actually composites, so its presence is the switch. Kept in step with the cvars
    ///         rather than checked per draw so that turning the mode off stops the work entirely.
    /// </summary>
    /// <remarks>
    ///     Asking before adding matters because two different cvars land here and several transitions leave
    ///         <see cref="WantsCaptures"/> true on both sides of the change - Buffer to Both, most obviously,
    ///         which is the switch the two approaches are compared with. AddOverlay is keyed by type and
    ///         quietly returns false for a duplicate, so the overlay built for it would be dropped on the floor
    ///         still holding a unique ShaderInstance and three live cvar subscriptions, with nothing left
    ///         holding it that could ever dispose them.
    /// </remarks>
    private void RefreshOverlay()
    {
        if (!WantsCaptures)
        {
            _overlayManager.RemoveOverlay<KsZLevelLightBufferOverlay>();
            return;
        }

        if (!_overlayManager.HasOverlay<KsZLevelLightBufferOverlay>())
            _overlayManager.AddOverlay(new KsZLevelLightBufferOverlay());
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
