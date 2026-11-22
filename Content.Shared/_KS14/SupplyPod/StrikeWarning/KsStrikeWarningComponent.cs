using Content.Shared._KS14.SupplyPod.LandingZone;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.SupplyPod.StrikeWarning;

/// <summary>
///     A landing zone indicator for something hostile coming down: a hazard-striped blast disc under a crosshair, with
///         chevrons closing in on its rim and a pulse that quickens as the impact nears. Sized to the blast of whatever
///         is landing, when that explodes. Its countdown is the <see cref="SupplyPodLandingZoneIndicatorComponent"/>
///         beside it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsStrikeWarningComponent : Component
{
    /// <summary>
    ///     Radius of the marked area, in tiles. The server replaces it with the blast radius of what is landing when
    ///         that carries an explosion, so this is what non-explosive landings show.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Radius = 1.5f;

    /// <summary>
    ///     Radius, in tiles, the art's disc is drawn at; the layers are scaled by <see cref="Radius"/> over this.
    /// </summary>
    [DataField]
    public float ArtRadius = 2.5f;

    /// <summary>
    ///     The smallest area the marker shrinks to, in tiles, so a tiny blast is still marked.
    /// </summary>
    [DataField]
    public float MinimumRadius = 1f;

    /// <summary>
    ///     How far out the chevrons start, as a multiple of the area. They close in to the rim by the impact.
    /// </summary>
    [DataField]
    public float ChevronStartScale = 1.6f;

    /// <summary>
    ///     Pulses per second when the countdown starts.
    /// </summary>
    [DataField]
    public float PulseStartFrequency = 1.5f;

    /// <summary>
    ///     Pulses per second at the impact. The rate rises linearly between the two.
    /// </summary>
    [DataField]
    public float PulseEndFrequency = 7f;

    /// <summary>
    ///     Opacity of the pulsing layers at the low point of each pulse.
    /// </summary>
    [DataField]
    public float PulseMinimumAlpha = 0.35f;
}

/// <summary>
///     The layers the client drives; a marker may leave any out.
/// </summary>
[Serializable, NetSerializable]
public enum KsStrikeWarningLayers : byte
{
    /// <summary>
    ///     The marked area, or only its rim: scaled to the blast, pulsing.
    /// </summary>
    Area,

    /// <summary>
    ///     The crosshair: scaled with the area, pulsing.
    /// </summary>
    Reticle,

    /// <summary>
    ///     Closing in on the rim over the countdown.
    /// </summary>
    Chevrons,
}
