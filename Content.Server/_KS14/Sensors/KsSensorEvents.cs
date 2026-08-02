using System.Numerics;
using Content.Shared._KS14.Sensors;
using Content.Shared._KS14.Sensors.Prototypes;
using Content.Shared.Shuttles.BUIStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Sensors;

/// <summary>One detection produced by a sensor during a sweep.</summary>
/// <param name="WorldPosition">Target world position (typically its center of mass).</param>
/// <param name="Static">Whether the target's body is static (terrain/station); suppresses moving-track heading markers on radar.</param>
/// <param name="LocalBounds">Target grid's local AABB, the silhouette rectangle.</param>
/// <param name="LocalCenter">Target's center of mass in its own grid-local space.</param>
/// <param name="Name">Target name if the sensor provides identification, else null.</param>
/// <param name="Intel">Evaluated intel readout values, or null if the sensor extracts none.</param>
/// <param name="Obscured">True when the target actively obscures itself (IFF Hide):
///     the detection is degraded to an anonymous blip regardless of sensor quality.</param>
/// <param name="TypeOverride">Files this detection under a different tier/colour than the
///     producing sensor's <see cref="KsSensorComponent.Type"/>, so an ELINT sensor can file
///     a located jammer as a Jammer return, and a jammed radar its home-on-jam return.</param>
/// <param name="QualityOverride">Overrides the producing sensor's
///     <see cref="KsSensorComponent.ResolvesPosition"/>. Nullable like
///     <paramref name="TypeOverride"/>: null means "no opinion", so a YAML bearing-only
///     sensor still applies (a non-nullable Exact default would silently override it);
///     a home-on-jam return sets it explicitly.</param>
/// <param name="Band">The heard emission's frequency band, when this detection located
///     an emitter (ELINT). Identification intel carried onto the source record.</param>
/// <param name="Pattern">The heard emission's pattern over time, alongside <paramref name="Band"/>.</param>
/// <param name="SignalStrength">Relative strength of the heard emission in (0, 1],
///     measured by the detecting sensor at sweep time. Only meaningful on
///     bearing-quality detections; 1 (the default) for everything else.</param>
public readonly record struct KsSensorDetection(
    EntityUid TargetGrid,
    Vector2 WorldPosition,
    Angle Rotation,
    Vector2 LinearVelocity,
    bool Static,
    Box2 LocalBounds,
    Vector2 LocalCenter,
    string? Name,
    Dictionary<ProtoId<KsSensorIntelPrototype>, string>? Intel,
    bool Obscured = false,
    KsSensorType? TypeOverride = null,
    KsPositionQuality? QualityOverride = null,
    ProtoId<KsEmitterBandPrototype>? Band = null,
    KsEmissionPattern? Pattern = null,
    float SignalStrength = 1f);

/// <summary>
///     Raised each sensor tick on every operational sensor entity. Behavior systems
///         subscribe with their marker component and append what the sensor currently sees.
/// </summary>
[ByRefEvent]
public record struct KsSensorSweepEvent(Entity<KsSensorComponent> Sensor)
{
    public readonly List<KsSensorDetection> Detections = [];

    /// <summary>Aboard life signs by target grid, centre-of-mass-relative local offsets (the frame the client dead-reckons in). Null for sensors without life-sign resolution.</summary>
    public Dictionary<EntityUid, List<Vector2>>? LifeSigns;

    /// <summary>Free-floating life signs by creature (co-mounted sensors dedupe), world positions.</summary>
    public Dictionary<EntityUid, Vector2>? LifeSignFloaters;
}

/// <summary>
///     Raised each tick a console needs to draw a sensor's coverage. Behavior systems
///         fill <see cref="WorldPoints"/> with the sensor's field-of-view fan in WORLD
///         space: index 0 is the sensor mount (apex), the rest trace the visible boundary
///         cut short by occluders. Left null when the sensor produces no drawable coverage.
/// </summary>
[ByRefEvent]
public record struct KsSensorCoverageEvent(Entity<KsSensorComponent> Sensor)
{
    public List<Vector2>? WorldPoints;

    /// <summary>
    ///     Set true by an actively-emitting behavior (radar: on == emitting) so its cone
    ///         pulses as a "you are lit up" tell. Left false by passive sensors.
    /// </summary>
    public bool Emitting;
}

/// <summary>
///     Asks a sensor whether it currently has a clear, in-range line of sight to the
///         last-known spot of <see cref="TargetGrid"/>, answered with the same
///         line-of-sight (and detectability) rules the behavior system detects with.
///         Confirms a memory ghost's last spot is now empty ("look and it's gone"): a
///         sensor that would plainly see the target and detects nothing prunes the ghost.
///     <see cref="TargetGrid"/> lets the behavior system decline to confirm-empty a target
///         its sweep would ignore anyway (e.g. sub-threshold debris), where a clear line of
///         sight proves nothing.
/// </summary>
[ByRefEvent]
public record struct KsSensorPointVisibleEvent(Entity<KsSensorComponent> Sensor, EntityUid TargetGrid, MapId MapId, Vector2 WorldPos)
{
    public bool Visible;
}

/// <summary>
///     Broadcast-raised by console state builders to fetch the contact snapshot for a
///         grid. Answered by <see cref="KsSensorSystem"/>.
///     <see cref="Contacts"/> stays null when the grid has no pool (no sensor coverage);
///         <see cref="Regions"/> stays null when it has no drawable sensor coverage.
/// </summary>
[ByRefEvent]
public record struct KsCollectNavContactsEvent(EntityUid? Grid)
{
    public List<KsSensorContactState>? Contacts;
    public List<KsSensorRegionState>? Regions;

    /// <summary>Whether any radar on the grid is currently jammed, for the console "JAMMED" indicator.</summary>
    public bool Jammed;

    /// <summary>Whether the grid mounts any radar at all, for the console's radar on/off toggle visibility.</summary>
    public bool HasRadar;

    /// <summary>Whether any radar on the grid is switched on (emitting), for the toggle's ON/OFF label.</summary>
    public bool RadarActive;

    /// <summary>Whether the grid mounts any jammer at all, for the console's jammer on/off toggle visibility.</summary>
    public bool HasJammer;

    /// <summary>Whether any jammer on the grid is switched on, for the jammer toggle's ON/OFF label.</summary>
    public bool JammerActive;

    /// <summary>Whether the grid mounts any ELINT array, for the ESM tab's precision-panel gating.</summary>
    public bool HasElint;

    /// <summary>Whether the grid's ELINT is self-blinded right now by an own active emitter (radar or jammer). See <see cref="KsSensorNavState.ElintDeaf"/>.</summary>
    public bool ElintDeaf;

    /// <summary>Whether the grid mounts any radar warning receiver, for the ESM tab's warning-panel gating.</summary>
    public bool HasRwr;

    /// <summary>The grid's emission log (oldest first), or null when it is empty. See <see cref="KsSensorNavState.EmissionLog"/>.</summary>
    public List<KsEmissionLogEntry>? EmissionLog;

    /// <summary>This sweep's life-sign blips, or null when there are none. See <see cref="KsSensorNavState.LifeSigns"/>.</summary>
    public List<KsLifeSignState>? LifeSigns;
}

/// <summary>
///     Raised right after the shuttle or radar console builds a
///         <see cref="NavInterfaceState"/>, so the sensor framework can attach the console
///         grid's contact picture (contacts, coverage fans, emitter state) without the
///         upstream console builder knowing anything about sensors.
///     Handled by <see cref="KsSensorConsoleSystem"/>, which enforces the anchored-console
///         and open-UI gating before it collects.
/// </summary>
[ByRefEvent]
public readonly record struct KsNavStateBuiltEvent(EntityUid Console, NavInterfaceState State);
