using System.Numerics;
using Content.Shared._KS14.Sensors.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Sensors;

/// <summary>
///     How a contact renders when the real grid isn't available client-side (or
///         isn't live). When multiple sources see one contact the best mode wins
///         (<see cref="Outline"/> beats <see cref="Blip"/>).
/// </summary>
[Serializable, NetSerializable]
public enum KsContactRenderMode : byte
{
    Blip = 0,
    Outline = 1,
}

/// <summary>
///     The kind of sensor that produced a detection. Its declared order IS the
///         confidence precedence: when several sensors see one contact the
///         lowest-valued (highest-tier) type wins the rendering. The client maps
///         each type to a colour. A datalink self-report is filed as
///         <see cref="VisualSearch"/> (an ally reporting its own position is perfect
///         knowledge) and additionally tinted as an ally.
/// </summary>
[Serializable, NetSerializable]
public enum KsSensorType : byte
{
    VisualSearch = 0,
    IRST = 1,
    Radar = 2,
    Elint = 3,

    /// <summary>
    ///     Radar warning receiver. Hears only emissions that actually illuminate its
    ///         own grid, always as a bare bearing, so it ranks just below ELINT.
    /// </summary>
    Rwr = 4,

    /// <summary>
    ///     Not a sensor a ship carries: the classification an emission listener's
    ///         return (ELINT, RWR, or a jammed radar's home-on-jam return) gets when
    ///         the emission it located is a jammer rather than a radar. Lowest tier, so
    ///         a jammer also tracked by a real sensor renders as that better track; the
    ///         dedicated "being jammed" indicator covers the superseded case.
    /// </summary>
    Jammer = 5,
}

/// <summary>
///     How much of a contact's text readout the radar draws. Purely a display
///         setting: the server always sends the whole picture, the operator chooses
///         how much of it clutters the plot. Each readout line declares the lowest
///         level it survives at, so what "basic" means is YAML, not code.
/// </summary>
[Serializable, NetSerializable]
public enum KsContactDetail : byte
{
    None = 0,
    Basic = 1,
    Full = 2,
}

/// <summary>
///     How well a detection resolves WHERE a target is, orthogonal to the sensor
///         type: <see cref="Exact"/> is a position fix, <see cref="Bearing"/> only a
///         direction. Bearing-quality knowledge never sends the client a position: the
///         contact state's position block is zeroed server-side and a single bearing
///         strobe is sent instead, so the withheld fix cannot be recovered client-side.
/// </summary>
[Serializable, NetSerializable]
public enum KsPositionQuality : byte
{
    Exact = 0,
    Bearing = 1,
}

/// <summary>
///     How steady a bearing track's direction is: the rate of bearing change
///         between the strobe source's last two measurements, classified server-side
///         against a drift threshold. A crossing or manoeuvring emitter reads
///         <see cref="Drifting"/>; a station (or a ship on a constant-bearing
///         intercept) reads <see cref="Stable"/>. <see cref="Unknown"/> = fewer than
///         two measurements to compare.
/// </summary>
[Serializable, NetSerializable]
public enum KsBearingStability : byte
{
    Unknown = 0,
    Stable = 1,
    Drifting = 2,
}

/// <summary>
///     The single bearing strobe a Bearing-quality contact renders as: a ray of
///         known direction and unknown range. At most ONE strobe is ever sent per
///         contact, however many bearing sources the server holds: two true
///         centrelines from distinct origins would let the player intersect the
///         rays on screen and read off the exact position the server withheld.
/// </summary>
/// <param name="SourceGrid">The grid that measured the bearing (the wedge apex's owner).</param>
/// <param name="Origin">Wedge apex in world coordinates: the measuring sensor's mount
///     for an own-grid strobe, the ally's last-known position for a relayed one.</param>
/// <param name="Bearing">Direction from <paramref name="Origin"/> toward the target: a
///     mathematical direction angle (atan2 of the world delta), not a compass bearing.</param>
/// <param name="AccuracyDeg">Display wedge half-width in degrees. Presentation only:
///     the centreline itself is never perturbed.</param>
/// <param name="SignalStrength">Relative strength of the heard emission, measured at
///     sweep time (distance into the emission's reach), frozen with the bearing and
///     QUANTIZED to quarter steps (0.25/0.5/0.75/1) on the wire: the raw ratio plus
///     the revealed emitter-range readout would invert into an accurate range along
///     the known bearing. 1 for non-emission bearings (home-on-jam: the noise is the
///     loudest thing in the sky).</param>
/// <param name="LastSeen">Server CurTime when this bearing was measured.</param>
[Serializable, NetSerializable]
public readonly record struct KsBearingLine(
    NetEntity SourceGrid,
    Vector2 Origin,
    Angle Bearing,
    float AccuracyDeg,
    float SignalStrength,
    TimeSpan LastSeen);

/// <summary>
///     One sensor that (last) saw a contact. Carried per-contact so UIs can
///         attribute detections ("visual search: KNS Tiderfall") and so emission
///         logic can reason about the emitting/observing entity.
/// </summary>
/// <param name="Sensor">Origin identity for provenance dedup.</param>
/// <param name="SourceGrid">When this equals the contact's grid, the
///     contact is a datalink self-report, a network member announcing itself, i.e. an ally.</param>
/// <param name="Hops">How many datalink hops this detection travelled to reach the viewing grid. 0 = own sensor.</param>
/// <param name="LastSeen">Server CurTime when this source last saw the contact.</param>
/// <param name="Quality">How well this source resolves the target's position, for attribution UIs.</param>
[Serializable, NetSerializable]
public readonly record struct KsContactSource(
    NetEntity Sensor,
    string SensorName,
    NetEntity SourceGrid,
    string SourceGridName,
    int Hops,
    TimeSpan LastSeen,
    KsSensorType Type,
    KsPositionQuality Quality);

/// <summary>
///     A single contact snapshot as pushed to console UIs: the whole truth a
///         viewing grid has about a target. Clients never receive anything about
///         undetected grids.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsSensorContactState
{
    /// <summary>Identity, not a data source: the client may not have this entity at all (beyond PVS).</summary>
    public NetEntity Grid;

    /// <summary>
    ///     Target name, if any detecting sensor is advanced enough to provide it
    ///         (<c>KsSensorComponent.ProvidesName</c>). Null renders as unknown.
    /// </summary>
    public string? Name;

    /// <summary>Last known world position (map coordinates of the viewing grid's map).</summary>
    public Vector2 WorldPosition;

    /// <summary>Last known world rotation.</summary>
    public Angle Rotation;

    /// <summary>Last known linear velocity, world space.</summary>
    public Vector2 LinearVelocity;

    /// <summary>Whether the target's physics body is static (terrain/station). Presentational: suppresses the heading/velocity markers a moving track draws.</summary>
    public bool Static;

    /// <summary>
    ///     True while some source currently sees the target; false = memory ghost.
    ///         Ghosts never time out on their own: they persist as last-known intel
    ///         until a sensor confirms the spot is empty or the grid dies.
    /// </summary>
    public bool Live;

    /// <summary>Server CurTime of the freshest detection across all sources.</summary>
    public TimeSpan LastSeen;

    public KsContactRenderMode RenderMode;

    /// <summary>The winning (highest-tier) sensor type among this contact's sources; the client colours the contact by it.</summary>
    public KsSensorType Type;

    /// <summary>
    ///     Silhouette rectangle (the target grid's local AABB) for
    ///         <see cref="KsContactRenderMode.Outline"/> rendering beyond PVS.
    /// </summary>
    public Box2 LocalBounds;

    /// <summary>
    ///     The target's center of mass in its own grid-local space.
    ///         <see cref="WorldPosition"/> tracks the center of mass, so the
    ///         silhouette must be drawn relative to this point, not the AABB center.
    /// </summary>
    public Vector2 LocalCenter;

    /// <summary>
    ///     How well this contact's position is resolved. For
    ///         <see cref="KsPositionQuality.Bearing"/> the whole position block
    ///         (<see cref="WorldPosition"/>, <see cref="Rotation"/>,
    ///         <see cref="LinearVelocity"/>, <see cref="LocalBounds"/>,
    ///         <see cref="LocalCenter"/>) is zeroed server-side and
    ///         <see cref="Bearing"/> carries the strobe instead.
    /// </summary>
    public KsPositionQuality Quality;

    /// <summary>
    ///     The single collapsed bearing strobe of a Bearing-quality contact. Null
    ///         for Exact contacts, and for a Bearing contact whose only bearings
    ///         came from allies this grid has no position for: that contact is
    ///         roster-only, since plotting it would leak the ally.
    /// </summary>
    public KsBearingLine? Bearing;

    /// <summary>
    ///     Only meaningful when <see cref="Bearing"/> is set;
    ///         <see cref="KsBearingStability.Unknown"/> otherwise.
    /// </summary>
    public KsBearingStability Stability;

    /// <summary>
    ///     Relative strength of the heard emission at the reporting receiver, when
    ///         any emitter-class source has heard this contact. Unlike
    ///         <see cref="Bearing"/> it survives a position fix: a better sensor
    ///         upgrading the track to Exact must not read back as SIGNAL loss on
    ///         the analysis panel. A scalar, so nothing positional rides along.
    /// </summary>
    public float? SignalStrength;

    /// <summary>
    ///     The emitter designation ("E-004") the datalink network filed this contact
    ///         under, once any emitter-class source (ELINT, jammer classification) has
    ///         heard it. Null for contacts never filed as an emitter. Shared over
    ///         datalink; conflicts resolve to the earliest assignment.
    /// </summary>
    public string? Designation;

    /// <summary>
    ///     The frequency band an emitter-class source heard this contact transmit
    ///         on, as identification intel. Null when no source has classified the band.
    /// </summary>
    public ProtoId<KsEmitterBandPrototype>? Band;

    /// <summary>The heard emission's pattern over time, alongside <see cref="Band"/>.</summary>
    public KsEmissionPattern? Pattern;

    /// <summary>
    ///     True while some LIVE emitter-class source (ELINT, RWR, jammer
    ///         classification) is hearing this contact emit, the same edge the emission
    ///         log runs on, derived from the viewing pool's own knowledge only.
    ///         Distinct from <see cref="Live"/>: a visually-tracked ship whose radar
    ///         just went dark is Live but not EmitterLive. Drives the RWR threat stack,
    ///         which warns about current illumination, not memories.
    /// </summary>
    public bool EmitterLive;

    /// <summary>Whether the viewing grid's ELINT is currently running focus analysis on this contact.</summary>
    public bool Focused;

    /// <summary>
    ///     Focus-analysis completion in [0, 1] (the best across the viewing grid's
    ///         ELINT arrays). 0 when not focused. At 1 the analysed emitter resolves
    ///         Exact while its track stays live.
    /// </summary>
    public float AnalysisProgress;

    /// <summary>
    ///     Ordered intel readout lines, e.g. (TopSpeed, "30"). Merged across
    ///         sources; ordering follows <see cref="KsSensorIntelPrototype.Order"/>.
    /// </summary>
    public List<(ProtoId<KsSensorIntelPrototype> Intel, string Value)> Intel = [];

    /// <summary>Everyone who (last) saw this contact, for attribution/tooltips.</summary>
    public List<KsContactSource> Sources = [];

    /// <summary>
    ///     Dead-reckoning cap: past this the last velocity is too stale to trust, so
    ///         the blip parks instead of flying off a stalled feed. Comfortably above
    ///         the live window, whose expiry flips the track to a frozen ghost anyway.
    /// </summary>
    public const double MaxDeadReckonSeconds = 2.0;

    /// <summary>
    ///     Best estimate of the target's position NOW: the last fix advanced along the
    ///         last known velocity, so the scope moves smoothly every frame instead of
    ///         stepping on each sensor tick (it also absorbs datalink hop latency). A
    ///         ghost stays frozen at its last confirmed fix, a static hull never moves,
    ///         and a bearing track has no position block to advance. A sensor that
    ///         hides heading zeroed the velocity server-side, so its contacts keep
    ///         stepping by design.
    /// </summary>
    public Vector2 EstimatedPosition(TimeSpan curTime)
    {
        if (!Live || Static || Quality == KsPositionQuality.Bearing)
            return WorldPosition;

        var age = Math.Clamp((curTime - LastSeen).TotalSeconds, 0.0, MaxDeadReckonSeconds);
        return WorldPosition + LinearVelocity * (float) age;
    }
}

/// <summary>
///     A sensor's computed coverage region (its field of view) for radar
///         display. A star-shaped fan: <see cref="Points"/>[0] is the sensor
///         mount (the apex) and the remaining points trace the visible boundary,
///         cut short wherever a wall or another grid occludes the line of sight.
///     Points are in the local frame of <see cref="Grid"/> so the client can
///         redraw them every frame as the ship moves, without a fresh state push.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsSensorRegionState
{
    /// <summary>The grid the polygon points are local to (the console's own grid).</summary>
    public NetEntity Grid;

    public NetEntity Sensor;

    /// <summary>
    ///     The client tints the fan to match this type's contact colour and drives
    ///         the per-type cone display mode (off / outline / filled) off it.
    /// </summary>
    public KsSensorType Type;

    /// <summary>
    ///     Whether this cone is an active emission (radar or jammer) rather than a
    ///         passive field of view (visual/IRST). The client pulses an emitting cone
    ///         as a reminder that running an emitter exposes you to enemy ELINT.
    /// </summary>
    public bool Emitting;

    /// <summary>
    ///     True when this cone belongs to a datalinked ally rather than the console's
    ///         own grid. Its <see cref="Points"/> are STILL framed against
    ///         <see cref="Grid"/> (the console's grid): the server transforms them at
    ///         collect time, because the ally's grid can be beyond PVS and the client
    ///         could never resolve its transform. The radar tab draws own cones only;
    ///         the sector map draws the whole network's coverage.
    /// </summary>
    public bool Relayed;

    /// <summary>
    ///     True for a sensor fan: only index 0 (the apex) is <see cref="Grid"/>-local,
    ///         and the boundary points are WORLD-oriented offsets from the apex. The
    ///         fan's rays are cast at world-fixed angles, so this split lets the client
    ///         ride the apex on the live ship transform while the occlusion notches
    ///         keep pointing at the hulls that cast them as the ship spins. False for a
    ///         jammer wedge, which follows its mount's facing: there the whole polygon
    ///         is <see cref="Grid"/>-local, which rotates with the hull exactly as the
    ///         real wedge does.
    /// </summary>
    public bool WorldOffsets;

    /// <summary>
    ///     Coverage polygon. Index 0 is the sensor mount (fan apex) in
    ///         <see cref="Grid"/>-local coordinates; the rest are the boundary in
    ///         angular order, forming a closed star polygon around the apex, framed
    ///         per <see cref="WorldOffsets"/>.
    /// </summary>
    public List<Vector2> Points = [];
}
