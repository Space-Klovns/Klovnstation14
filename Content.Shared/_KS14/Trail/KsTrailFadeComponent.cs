using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Trail;

/// <summary>
///     Makes a <see cref="KsTrailComponent"/> die off: blurs it out while fading it to nothing.
///     Only the timing is networked — the actual interpolation happens clientside, so blur and
///         colour never travel over the wire.
/// </summary>
/// <remarks>
///     Inert until <see cref="StartTime"/> is set to something other than <see cref="TimeSpan.MaxValue"/>,
///         which lets prototypes carry the tuning values while the fade itself is triggered later
///         by <see cref="KsTrailSystem.StartFade"/>.
/// </remarks>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class KsTrailFadeComponent : Component
{
    /// <summary>
    ///     Game-time that the fade began. <see cref="TimeSpan.MaxValue"/> means 'not started'.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan StartTime = TimeSpan.MaxValue;

    /// <summary>
    ///     How long the alpha takes to reach <see cref="TargetAlpha"/>.
    /// </summary>
    [DataField]
    public TimeSpan AlphaDuration = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     How long the blur takes to ramp from <see cref="StartBlur"/> to <see cref="EndBlur"/>.
    /// </summary>
    [DataField]
    public TimeSpan BlurDuration = TimeSpan.FromSeconds(1.5);

    [DataField]
    public float StartBlur = 0f;

    [DataField]
    public float EndBlur = 6f;

    [DataField]
    public float TargetAlpha = 0f;

    /// <summary>
    ///     Shape of the blur ramp. The engine's blur saturates quickly at the radii a visible
    ///         trail needs, so a front-loaded curve reads as an instant snap - hence linear by
    ///         default, with <see cref="KsTrailEasing.CubicIn"/> on hand for an even slower start.
    /// </summary>
    [DataField]
    public KsTrailEasing BlurEasing = KsTrailEasing.Linear;

    /// <summary>
    ///     Shape of the alpha ramp.
    /// </summary>
    [DataField]
    public KsTrailEasing AlphaEasing = KsTrailEasing.Linear;

    /// <summary>
    ///     The trail's alpha when the fade started, captured once so that the lerp stays linear
    ///         instead of decaying exponentially off its own output.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float InitialAlpha = 1f;

    /// <summary>
    ///     Whether <see cref="InitialAlpha"/> has been captured yet.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool Captured = false;
}

/// <summary>
///     Shape of a trail's fade ramps.
/// </summary>
public enum KsTrailEasing : byte
{
    Linear,

    /// <summary>
    ///     Slow start, fast finish.
    /// </summary>
    CubicIn,

    /// <summary>
    ///     Fast start, slow finish. tgstation's CUBIC_EASING|EASE_OUT.
    /// </summary>
    CubicOut,
}
