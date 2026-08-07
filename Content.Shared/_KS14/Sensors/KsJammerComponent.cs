using Content.Shared._KS14.Sensors.Prototypes;
using Content.Shared.Guidebook;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Sensors;

/// <summary>
///     A radar jammer: an emitter-only device, NOT a sensor. It detects nothing and
///         produces no contacts. It projects a pie slice of jamming (facing from the
///         mount rotation, half-angle <see cref="HalfAngle"/>, reach
///         <see cref="JammingPower"/>) which, unlike a radar or IRST cone, ignores line
///         of sight entirely: it jams every radar-emitting ship whose centre of mass
///         lies inside the slice, terrain or no terrain. A jammed radar goes dark
///         except within <c>JammingPower * radar.BurnThroughFactor</c> of the jammer.
///         ELINT sees the cone, classified as a jammer return.
///     Faction-blind: it jams allied radars caught in it too, hence the blind rear.
/// </summary>
[RegisterComponent]
public sealed partial class KsJammerComponent : Component
{
    /// <summary>
    ///     Checked together with power and mounting each tick. Defaults OFF, unlike a
    ///         radar: jamming is a deliberate offensive act (it broadcasts you to enemy
    ///         ELINT and burns power). Radar and jammer are mutually exclusive on a grid:
    ///         switching one on silences the other (KsSensorSystem.ToggleGridRadar /
    ///         ToggleGridJammer), and radar wins any residual tie in RebuildEmissions.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool Enabled = false;

    /// <summary>
    ///     The cone's reach in tiles AND the base of the burn-through range (a radar
    ///         burns through inside <c>JammingPower * radar.BurnThroughFactor</c> of
    ///         the jammer).
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    [GuidebookData]
    public float JammingPower = 192f;

    /// <summary>
    ///     Half the angular width of the jamming pie slice in DEGREES, measured either
    ///         side of the mount's facing. 180 is a full omnidirectional bubble.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    [GuidebookData]
    public float HalfAngle = 45f;

    /// <summary>
    ///     If true the jammer only works while anchored on a tile with at least one
    ///         spaced neighbor, i.e. mounted externally, like the shipboard sensors.
    /// </summary>
    [DataField]
    public bool RequireExternalMount = true;

    /// <summary>
    ///     The frequency band this jammer floods. Pure identification intel revealed
    ///         by enemy ELINT, like <see cref="KsRadarComponent.Band"/>; band-matched
    ///         jamming mechanics are not implemented.
    /// </summary>
    [DataField]
    public ProtoId<KsEmitterBandPrototype>? Band;

    /// <summary>The emission pattern over time, identification intel like <see cref="Band"/>.</summary>
    [DataField]
    public KsEmissionPattern Pattern = KsEmissionPattern.Continuous;

    /// <summary>Purely cosmetic: gates whether the jam cone is drawn. Never affects jamming.</summary>
    [DataField]
    public bool ShowCoverage = true;

    /// <summary>Higher = smoother drawn arc, more cost per drawn tick. Purely cosmetic.</summary>
    [DataField]
    public int CoverageRays = 48;
}
