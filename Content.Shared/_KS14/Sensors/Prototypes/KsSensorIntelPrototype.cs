using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Sensors.Prototypes;

/// <summary>
///     The raw physical quantity a readout measures. Each needs server code to
///         compute, so a new quantity means a new value here; presentation
///         (banding/formatting) is all prototype data.
/// </summary>
public enum KsSensorMetric : byte
{
    /// <summary>Grid physics mass.</summary>
    Mass,

    /// <summary>Σ linear-thruster Thrust / mass estimate; no value without linear thrusters.</summary>
    TopSpeed,

    /// <summary>Thermal signature (exterior-wall heat crawler).</summary>
    Heat,

    /// <summary>Grid local-AABB area (width * height).</summary>
    Area,

    /// <summary>Radar cross-section (exterior-wall RCS crawler), the quantity active radar detects.</summary>
    RadarCrossSection,

    /// <summary>
    ///     The emitting radar's own detection range, reported by ELINT about a located
    ///         emitter. It measures the EMITTING SENSOR, not the target grid, so the
    ///         grid metric path yields no value and ELINT fills the readout in directly.
    /// </summary>
    EmitterRange,
}

/// <summary>
///     One labelled band of a bucketed readout: the first band whose
///         <see cref="Below"/> the scaled value is strictly under wins. List bands
///         ascending; the final catch-all band omits Below (defaults to +inf).
/// </summary>
[DataDefinition]
public sealed partial class KsIntelThreshold
{
    /// <summary>Exclusive upper bound. Omit on the last band for a catch-all.</summary>
    [DataField]
    public float Below = float.PositiveInfinity;

    [DataField(required: true)]
    public LocId Label;
}

/// <summary>
///     A kind of intel a sensor can extract about a contact, shown as a readout line
///         on radar UIs (e.g. "TOP SPEED: 30"). Sensors declare which of these they
///         provide in YAML; evaluation is server-side, in KsSensorIntelSystem.
/// </summary>
[Prototype]
public sealed partial class KsSensorIntelPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Label;

    /// <summary>Sort order of the readout line; lower is higher up.</summary>
    [DataField]
    public int Order;

    [DataField(required: true)]
    public KsSensorMetric Metric;

    /// <summary>
    ///     Bucketed presentation: the value is classified into the first band it falls
    ///         under. When non-empty this drives the readout and
    ///         <see cref="ValueFormat"/> is ignored.
    /// </summary>
    [DataField]
    public List<KsIntelThreshold> Thresholds = new();

    /// <summary>
    ///     Numeric presentation: a loc string with { $value } substituted by the
    ///         scaled+rounded metric value. Used when <see cref="Thresholds"/> is empty.
    /// </summary>
    [DataField]
    public LocId? ValueFormat;

    /// <summary>Decimal places the value is rounded to before formatting (0 = integer).</summary>
    [DataField]
    public int Round;

    /// <summary>Multiplies the raw metric before rounding/formatting (unit conversion).</summary>
    [DataField]
    public float Scale = 1f;

    /// <summary>
    ///     Shown when the metric yields no value (e.g. TopSpeed on an engineless grid).
    ///         Null omits the readout line entirely in that case.
    /// </summary>
    [DataField]
    public LocId? NoneLabel;

    /// <summary>
    ///     Whether this readout's label appears on every contact panel even when no
    ///         source has detected a value (the value then renders blank), so an
    ///         undetected quantity reads as an empty slot rather than a missing line.
    ///         False shows the row only when a value is present. Consumed client-side
    ///         by the nav radar; the server ignores it.
    /// </summary>
    [DataField]
    public bool AlwaysShowLabel = true;

    /// <summary>
    ///     Whether the last-known value persists on the contact after the sensor that
    ///         resolved it loses track, refreshed only by a fresh detection: a visual
    ///         pass that read mass/size stays visible while IRST alone holds the target.
    ///         Set false for a reading only meaningful live; it then shows only while a
    ///         currently-tracking source reports it, and clears once none does.
    /// </summary>
    [DataField]
    public bool Sticky = true;

    /// <summary>
    ///     The lowest readout detail level this line still draws at, so a crowded plot
    ///         can be cut back to the few quantities worth reading at a glance. Consumed
    ///         client-side by the nav radar; the server always sends every resolved
    ///         readout.
    /// </summary>
    [DataField]
    public KsContactDetail Detail = KsContactDetail.Full;
}
