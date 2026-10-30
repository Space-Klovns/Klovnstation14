using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.SupplyPod.LandingZone;

/// <summary>
///     Marks the ground a supply pod is about to hit with a holographic landing zone indicator for
///         the duration of its descent.
/// </summary>
/// <remarks>
///     tgstation's <c>/obj/effect/pod_landingzone</c> and <c>pod_landingzone_effect</c>. Pods with
///         no indicator simply don't carry this component, which is that codebase's
///         <c>effectStealth</c>.
/// </remarks>
[RegisterComponent]
public sealed partial class SupplyPodLandingZoneComponent : Component
{
    /// <summary>
    ///     Indicator to spawn on the pod's destination. Must carry a
    ///         <see cref="SupplyPodLandingZoneIndicatorComponent"/>.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId IndicatorProto;

    /// <summary>
    ///     The spawned indicator. Server-side bookkeeping only.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? IndicatorEntity;
}

/// <summary>
///     The indicator itself. Counts down to the pod's arrival: the zone turns a quarter turn while
///         the slider ring closes in on it from 1.5x down to nothing.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(SharedSupplyPodLandingZoneSystem))]
public sealed partial class SupplyPodLandingZoneIndicatorComponent : Component
{
    /// <summary>
    ///     Game-time the countdown started at.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan StartTime;

    /// <summary>
    ///     How long the countdown lasts. tgstation's <c>delays[POD_TRANSIT] + delays[POD_FALLING]</c>.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField]
    public TimeSpan Duration;

    /// <summary>
    ///     Size of the slider ring when the countdown starts. Clockwise-turning zone ends a quarter
    ///         turn on.
    /// </summary>
    [DataField]
    public float SliderStartScale = 1.5f;

    /// <summary>
    ///     Size of the slider ring when the pod arrives. Not zero, to match <c>matrix() * 0.01</c>.
    /// </summary>
    [DataField]
    public float SliderEndScale = 0.01f;

    /// <summary>
    ///     Whether the pixel-snapped layers blend the four nearest texels, rather than taking the one
    ///         under each block's centre. Only read by the client.
    /// </summary>
    [DataField]
    public bool Filtered = true;

    /// <summary>
    ///     How far the zone turns over the whole countdown. Negative is clockwise on screen.
    /// </summary>
    [DataField]
    public Angle ZoneTurn = Angle.FromDegrees(-90);
}

[Serializable, NetSerializable]
public enum SupplyPodLandingZoneLayers : byte
{
    Zone,
    Slider
}
