using Content.Shared._KS14.Sensors.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Sensors;

/// <summary>
///     Common configuration for all shipboard sensors. A concrete sensor also
///         needs a behavior component (e.g. <see cref="KsVisualSearchComponent"/>)
///         whose system answers the server-side sweep event each sensor tick.
///     Detections land in the mounting grid's contact pool, read by every
///         sensor-consuming console on the grid.
///     Not networked: sensor settings are server-side state.
/// </summary>
[RegisterComponent]
public sealed partial class KsSensorComponent : Component
{
    /// <summary>Power and mounting are checked separately each tick.</summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool Enabled = true;

    /// <summary>Maximum detection range in meters/tiles.</summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float MaxRange = 160f;

    /// <summary>
    ///     Minimum mass a non-static grid must have for this sensor to track it. The
    ///         default mirrors SharedShuttleSystem.CanDraw's 10 mass floor so stock
    ///         sensors stay consistent with the fog of war; lower it for a debris
    ///         scanner or a seeker tracking small targets. The ghost prune reads the
    ///         same value, so a sensor never confirms empty a spot whose occupant its
    ///         sweep would ignore.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float MinTrackableMass = 10f;

    /// <summary>
    ///     What kind of sensor this is. Drives the confidence tier and colour
    ///         its contacts render with, and the precedence when several
    ///         sensors see one target. Must match the behavior component
    ///         (e.g. <see cref="KsVisualSearchComponent"/> -> <see cref="KsSensorType.VisualSearch"/>).
    ///     Serialized as "sensorType" because "type" is the component discriminator.
    /// </summary>
    [DataField("sensorType")]
    public KsSensorType Type = KsSensorType.VisualSearch;

    /// <summary>How contacts from this sensor render when the real grid isn't drawable client-side.</summary>
    [DataField]
    public KsContactRenderMode RenderMode = KsContactRenderMode.Blip;

    [DataField]
    public bool ProvidesName;

    /// <summary>
    ///     Master switch for revealing the target's heading/velocity. When false this
    ///         sensor's contacts carry no velocity, so consoles draw no heading marker.
    /// </summary>
    [DataField]
    public bool RevealVelocity = true;

    /// <summary>
    ///     Master switch for revealing the target's silhouette. When false this sensor's
    ///         contacts always render as an anonymous blip, always winning over
    ///         <see cref="RenderMode"/>. Kept distinct from RenderMode so a sensor can keep
    ///         its native render mode while a config withholds the silhouette.
    /// </summary>
    [DataField]
    public bool RevealSilhouette = true;

    /// <summary>
    ///     How well this sensor resolves a target's position: Exact is a full fix,
    ///         Bearing a direction-only strobe (the contact snapshot's position block is
    ///         withheld from the client entirely). A single detection can override it via
    ///         <c>KsSensorDetection.QualityOverride</c> (home-on-jam does).
    /// </summary>
    [DataField]
    public KsPositionQuality ResolvesPosition = KsPositionQuality.Exact;

    /// <summary>
    ///     Half-width of the drawn bearing wedge in degrees, when this sensor produces
    ///         Bearing-quality returns. Display only: the centreline is the true direction
    ///         and is never perturbed (angular fuzz was tried and removed).
    /// </summary>
    [DataField]
    public float BearingAccuracy = 4f;

    /// <summary>
    ///     Minimum angular separation, in degrees, between this sensor's live bearing
    ///         track and another grid's live bearing track of the same contact for the
    ///         pool to triangulate the two into an Exact fix. A pure reveal rule: the
    ///         server already knows the truth, there is no solver. The stricter of the
    ///         pair's thresholds must be met. 0 = this sensor's bearings never
    ///         participate in triangulation.
    /// </summary>
    [DataField]
    public float TriangulateMinBaseline = 10f;

    /// <summary>Intel this sensor can extract about contacts (readout lines).</summary>
    [DataField]
    public List<ProtoId<KsSensorIntelPrototype>> Intel = [];

    /// <summary>
    ///     If true the sensor only works while anchored on a tile with at least one
    ///         spaced (tile-less) neighbor, i.e. mounted externally.
    /// </summary>
    [DataField]
    public bool RequireExternalMount = true;

    /// <summary>Purely cosmetic: it never affects what the sensor detects.</summary>
    [DataField]
    public bool ShowCoverage = true;

    /// <summary>
    ///     Higher = smoother cone, more cost per drawn tick. Purely cosmetic; detection
    ///         uses direct per-target line of sight.
    /// </summary>
    [DataField]
    public int CoverageRays = 90;
}

/// <summary>
///     Behavior marker: sees every grid within <see cref="KsSensorComponent.MaxRange"/> of
///         the mount point that it has a clear line of sight to (blocked by its own hull
///         and by any other grid in the way) and cleanly returns ship data.
/// </summary>
[RegisterComponent]
public sealed partial class KsVisualSearchComponent : Component;

/// <summary>
///     Behavior component: infrared search and track. Detects a grid by its thermal
///         signature (the sum of the IRST value of every exterior wall, see
///         <see cref="KsThermalSourceComponent"/>) rather than by sight, so a hot ship is
///         picked up far past visual range while a cold one hides. Detection is still
///         line-of-sight gated (own hull and other grids block it), like
///         <see cref="KsVisualSearchComponent"/>, and by default it resolves no name
///         (<see cref="KsSensorComponent.ProvidesName"/>).
///     Effective range as a function of a target's signature S: S below
///         <see cref="MinDetectable"/> is never seen; otherwise the range is
///         <c>MaxRange - Factor * (MinDetectableAtMaxRange - S)</c> clamped to
///         [0, MaxRange]. So a target at exactly <see cref="MinDetectableAtMaxRange"/>
///         is seen out to the full <see cref="KsSensorComponent.MaxRange"/>, and
///         a fainter one only closer, the slope set by <see cref="Factor"/>.
/// </summary>
[RegisterComponent]
public sealed partial class KsIrstComponent : Component
{
    /// <summary>
    ///     Absolute sensitivity floor: a target whose thermal signature is below this is
    ///         never detected, at any range. Raise it for a simple sensor (e.g. a
    ///         counter-missile seeker) that ignores everything but strong contacts.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float MinDetectable = 10f;

    /// <summary>
    ///     The thermal signature a target needs to be detected all the way out at
    ///         <see cref="KsSensorComponent.MaxRange"/>. Should be at least
    ///         <see cref="MinDetectable"/> (seeing something far away is harder).
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float MinDetectableAtMaxRange = 80f;

    /// <summary>
    ///     Slope of the range falloff: meters of effective range lost per unit of
    ///         signature below <see cref="MinDetectableAtMaxRange"/>. Larger = steeper,
    ///         so a faint contact drops off into short range; smaller = a gentle taper.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Factor = 6f;
}

/// <summary>
///     Behavior component: active radar. Works like <see cref="KsIrstComponent"/> (the same
///         range curve, the same occluder-gated line of sight) but keyed on a grid's radar
///         cross-section (see <see cref="KsRadarSourceComponent"/>) instead of its heat,
///         with two differences that make it an ACTIVE sensor:
///     <list type="bullet">
///         <item>It emits. An operational radar is always emitting (on == emitting), so
///             its coverage cone carries <c>Emitting</c> and it is detectable by enemy
///             ELINT out to its full cone reach.</item>
///         <item>Its cone reaches further than it can actually resolve: the drawn/ELINT
///             cone extends to <see cref="ConeRangeFactor"/> times
///             <see cref="KsSensorComponent.MaxRange"/>, while detection stays capped at
///             MaxRange. The outer band is "illuminated but not resolved", where enemy
///             ELINT hears you before you can see them.</item>
///     </list>
///     Bleed-through mirrors IRST: a grid with RCS below <see cref="MinDetectable"/> casts
///         no radar shadow, so the detection ray passes through it and a bright ship behind
///         a stealthy one is still seen. RCS governs only a target's own return strength,
///         never whether it is cover. The sensor's own hull always blocks.
/// </summary>
[RegisterComponent]
public sealed partial class KsRadarComponent : Component
{
    /// <summary>Absolute RCS floor: a target below this is never detected. See <see cref="KsIrstComponent.MinDetectable"/>.</summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float MinDetectable = 10f;

    /// <summary>The RCS a target needs to be detected all the way out at MaxRange. See <see cref="KsIrstComponent.MinDetectableAtMaxRange"/>.</summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float MinDetectableAtMaxRange = 80f;

    /// <summary>Slope of the range falloff. See <see cref="KsIrstComponent.Factor"/>.</summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Factor = 6f;

    /// <summary>
    ///     How far the emitting cone reaches, as a multiple of
    ///         <see cref="KsSensorComponent.MaxRange"/>. The cone is drawn and heard by
    ///         ELINT out to <c>ConeRangeFactor * MaxRange</c>; actual detection is still
    ///         bounded by MaxRange. Default 2, so ELINT typically spots you before your
    ///         radar resolves it.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float ConeRangeFactor = 2f;

    /// <summary>
    ///     How well this radar burns through jamming. A jammer with power P blinds this
    ///         radar everywhere in its cone EXCEPT within <c>P * BurnThroughFactor</c> of
    ///         the jammer, where the radar works again. Higher = more jam-resistant; a
    ///         value >= 1 is effectively immune (the burn-through range meets or exceeds
    ///         the whole jam cone). Default 0.5: the radar recovers inside the inner half
    ///         of the jam cone.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float BurnThroughFactor = 0.5f;

    /// <summary>
    ///     Whether this radar localizes the jamming source and emits one home-on-jam
    ///         return (a magenta Jammer blip at the jammer's position) on the tick it
    ///         becomes jammed. False = a cheap set that is simply blinded silently.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool HomeOnJam = true;

    /// <summary>
    ///     The frequency band this radar transmits on. Pure identification intel: enemy
    ///         ELINT reveals it about the located emitter so the crew can classify the
    ///         set. Null = unclassifiable (reads as no band line).
    /// </summary>
    [DataField]
    public ProtoId<KsEmitterBandPrototype>? Band;

    /// <summary>The emission pattern over time, identification intel like <see cref="Band"/>.</summary>
    [DataField]
    public KsEmissionPattern Pattern = KsEmissionPattern.Continuous;
}

/// <summary>
///     Behavior component: ELINT (electronic intelligence). A passive listener that
///         emits nothing and detects no grids: it locates active EMITTERS. For each
///         emitting radar or jammer cone it sits inside, it produces a contact at the
///         emitter's position (a radar return, orange; or a jammer return, magenta),
///         out to a fraction of the cone's reach set by <see cref="IgnoreFraction"/>.
///     Because a radar's cone reaches ~twice its detection range, a sensitive ELINT
///         (low IgnoreFraction) usually hears an emitter well before that emitter can
///         resolve the ship carrying the ELINT; a primitive one (high IgnoreFraction)
///         only once close, i.e. after they can already see it.
///     ELINT is deaf while its own grid runs any active emitter (radar or jammer).
/// </summary>
[RegisterComponent]
public sealed partial class KsElintComponent : Component
{
    /// <summary>
    ///     The furthest fraction of every emitter's cone this ELINT cannot hear, in
    ///         [0, 1). 0 = advanced: hears an emitter across its whole cone (to
    ///         ConeRangeFactor * MaxRange for a radar). 0.6 = crude: only within the
    ///         inner 40% of the cone. The break-even against a radar's own detection is
    ///         0.5 (effective ELINT reach then equals the radar's MaxRange).
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float IgnoreFraction = 0f;

    /// <summary>
    ///     Seconds of continuous listening for focus analysis to reach 100% on a
    ///         designated emitter. Progress only advances on ticks the emitter is
    ///         actually heard, never while this ELINT is self-blinded by its own
    ///         grid's emissions.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float AnalysisTime = 30f;

    /// <summary>
    ///     Each stage's intel readouts are evaluated against the analysed grid once
    ///         progress reaches the stage. Unlocked intel is sticky (it stays known
    ///         after the track is lost). Reaching 100% additionally resolves the
    ///         emitter Exact while its track stays live.
    /// </summary>
    [DataField]
    public List<KsElintAnalysisStage> AnalysisStages = new();

    /// <summary>
    ///     The grid this ELINT is currently running focus analysis on. Runtime state,
    ///         set grid-wide from the console (one designated emitter per grid).
    ///         Null = no analysis running.
    /// </summary>
    [ViewVariables]
    public EntityUid? FocusTarget;

    /// <summary>Focus-analysis completion in [0, 1]. Reset on focus change or clear.</summary>
    [ViewVariables]
    public float FocusProgress;
}

/// <summary>
///     Behavior component: RWR (radar warning receiver), the cheap defensive tripwire
///         below the ELINT array. It hears only emissions that actually ILLUMINATE its
///         own grid (a radar cone with line of sight onto the hull, a jam slice covering
///         the centre of mass), always as a bare bearing, with no sensitivity tuning and
///         no focus analysis.
///     Unlike ELINT it is never self-blinded: it must warn even while the own radar is
///         up, and it only ever reports foreign emissions. Because a radar's cone reaches
///         ~twice its detection range, an RWR warns the crew while the painting radar
///         still cannot resolve them.
/// </summary>
[RegisterComponent]
public sealed partial class KsRwrComponent : Component;

/// <summary>The intel readouts that unlock when analysis progress reaches <see cref="Progress"/>.</summary>
[DataDefinition]
public sealed partial class KsElintAnalysisStage
{
    /// <summary>Progress fraction in [0, 1] at which this stage's intel unlocks.</summary>
    [DataField]
    public float Progress = 1f;

    /// <summary>The intel readouts this stage unlocks, evaluated against the analysed grid.</summary>
    [DataField]
    public List<ProtoId<KsSensorIntelPrototype>> Unlocks = [];
}
