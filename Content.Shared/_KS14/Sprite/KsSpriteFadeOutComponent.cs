using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Sprite;

/// <summary>
///     Fades this entity's sprite out to nothing once <see cref="FadeStartTime"/> passes.
///     Purely visual - the alpha is interpolated clientside, and deleting the entity afterwards
///         is somebody else's job (a TimedDespawnComponent, usually).
/// </summary>
/// <remarks>
///     Inert until <see cref="FadeStartTime"/> is set to something other than
///         <see cref="TimeSpan.MaxValue"/>, so a prototype can carry the tuning while the fade
///         itself gets triggered later.
/// </remarks>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class KsSpriteFadeOutComponent : Component
{
    /// <summary>
    ///     Game-time the fade begins. <see cref="TimeSpan.MaxValue"/> means 'not started'.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan FadeStartTime = TimeSpan.MaxValue;

    /// <summary>
    ///     How long the sprite takes to reach <see cref="TargetAlpha"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan FadeDuration = TimeSpan.FromSeconds(2.5);

    [DataField]
    public float TargetAlpha = 0f;

    /// <summary>
    ///     Draw depth to drop to when the fade starts, if any. Something on its way out generally
    ///         shouldn't keep occluding things that aren't.
    /// </summary>
    [DataField]
    public DrawDepth.DrawDepth? FadeDrawDepth;

    /// <summary>
    ///     The sprite's alpha when the fade started, captured once so the lerp stays linear
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
