using System.Numerics;
using Content.Shared._KS14.Sensors;
using Content.Shared._KS14.Sensors.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Sensors;

/// <summary>
///     Everything a grid knows through the sensor framework: its own sensors plus
///         anything ingested over datalink. Server-only: clients get per-console
///         snapshots via NavInterfaceState.KsSensorNav and never learn about
///         undetected grids. Added on demand by <see cref="KsSensorSystem"/>; every
///         console on the grid reading this pool is the automatic internal datalink.
/// </summary>
[RegisterComponent]
[Access(typeof(KsSensorSystem), Other = AccessPermissions.ReadExecute)]
public sealed partial class KsSensorContactPoolComponent : Component
{
    [ViewVariables]
    public Dictionary<EntityUid, KsContactRecord> Contacts = new();

    /// <summary>Set when the pool's contents change, cleared after console pushes so quiet grids generate no UI traffic.</summary>
    [ViewVariables]
    public bool Changed;

    /// <summary>
    ///     The next emitter designation number this pool hands out ("E-004"). Persistent
    ///         rather than derived from <see cref="Contacts"/>: records are pruned when
    ///         target grids die, so the contact count is not monotonic and reusing a dead
    ///         emitter's number would relabel history on every linked console.
    /// </summary>
    [ViewVariables]
    public int NextDesignation = 1;

    /// <summary>
    ///     The grid's emission log, oldest first, capped by the emission-log CVar. Fed by
    ///         this pool's own contact transitions (an emitter-class track appearing or
    ///         going silent) and the grid's own jam state edges, never by the global
    ///         emission registry, which would leak emitters the grid has not actually heard.
    /// </summary>
    [ViewVariables]
    public List<KsEmissionLogEntry> EmissionLog = new();
}

/// <summary>Server-side record of one known contact; positional data always holds the freshest knowledge across all sources.</summary>
public sealed class KsContactRecord
{
    public EntityUid TargetGrid;
    public NetEntity TargetNet;

    /// <summary>
    ///     The map this contact was last observed on. Consoles only render contacts for
    ///         their own map; charts of other maps are retained but dormant until you
    ///         return there.
    /// </summary>
    public MapId MapId;

    public Vector2 WorldPosition;
    public Angle Rotation;
    public Vector2 LinearVelocity;
    public bool Static;
    public Box2 LocalBounds;
    public Vector2 LocalCenter;

    /// <summary>Freshest detection time across all sources.</summary>
    public TimeSpan LastSeen;

    /// <summary>
    ///     Server CurTime a sensor last confirmed this contact's spot empty ("look and
    ///         it's gone"). While this is newer than <see cref="LastSeen"/> the record is
    ///         a tombstone: hidden from consoles and never rebroadcast, so a stale datalink
    ///         relay can re-ingest it without resurrecting it or thrashing the pool. A
    ///         genuinely newer sighting (LastSeen advancing past this) revives it.
    ///         <see cref="TimeSpan.MinValue"/> = never confirmed gone.
    /// </summary>
    public TimeSpan ConfirmedGoneAt = TimeSpan.MinValue;

    /// <summary>
    ///     Whether this contact was last delivered to consoles as a live track (rather
    ///         than a memory ghost). Liveness is time-derived and decays silently as a
    ///         track ages, so the sensor tick watches this to push a fresh picture the
    ///         moment a contact crosses the live/memory boundary. See
    ///         <see cref="KsSensorSystem"/>.
    /// </summary>
    public bool WasLive;

    /// <summary>
    ///     Per-origin-sensor knowledge, keyed by the origin sensor entity: the provenance
    ///         key that makes relay dedup work. Values are immutable: replace entries,
    ///         never mutate them.
    /// </summary>
    public Dictionary<EntityUid, KsSourceRecord> Sources = new();

    /// <summary>
    ///     Last-known value of each STICKY readout
    ///         (<see cref="KsSensorIntelPrototype.Sticky"/>), never cleared until a fresher
    ///         detection overwrites the same field, so an IRST-only re-acquisition
    ///         (reporting just heat) keeps mass/size/top-speed an earlier visual pass
    ///         learned. Rides the datalink with the record; non-sticky readouts are never
    ///         stored here and come straight from the current sources.
    /// </summary>
    public Dictionary<ProtoId<KsSensorIntelPrototype>, KsKnownIntel> KnownIntel = new();

    /// <summary>
    ///     The measuring grid this contact's bearing strobe is pinned to, once one has
    ///         been shown. A still-Bearing contact never shows another grid's centreline:
    ///         two true rays from distinct origins, even across time, intersect at a static
    ///         target's withheld position, sidestepping the triangulation baseline gate.
    ///         Released (nulled) when the contact's snapshot goes Exact; local pool state,
    ///         never relayed (ingest ignores it).
    /// </summary>
    public NetEntity? StrobeGrid;

    /// <summary>Last-known name, sticky like <see cref="KnownIntel"/> so an IRST-only track keeps a name an earlier visual pass resolved.</summary>
    public string? KnownName;

    /// <summary>When <see cref="KnownName"/> was learned, so only a newer sighting replaces it.</summary>
    public TimeSpan KnownNameSeen = TimeSpan.MinValue;

    /// <summary>
    ///     The emitter designation ("E-004") assigned when an emitter-class source (ELINT,
    ///         jammer classification) first heard this contact, null until then. Rides the
    ///         datalink with the record; on an ingest conflict the earlier
    ///         <see cref="FirstSeen"/> wins (tie: lower <see cref="DesignatedBy"/>), so a
    ///         linked fleet converges on one label.
    /// </summary>
    public string? Designation;

    /// <summary>Server time the winning designation was assigned, the conflict-resolution key.</summary>
    public TimeSpan FirstSeen;

    /// <summary>
    ///     The grid whose pool assigned the designation. Tie-breaker when two grids
    ///         designate the same emitter at the same instant; the TARGET entity is
    ///         identical on both sides of a relay conflict, so it could never break the tie.
    /// </summary>
    public NetEntity DesignatedBy;

    /// <summary>
    ///     Whether this contact counted as a live emitter track (any live emitter-class
    ///         source) on the last expiry pass. Edge-detected there to feed the pool's
    ///         emission log; local pool state, never relayed.
    /// </summary>
    public bool WasEmitterLive;

    public KsContactRecord Clone()
    {
        var clone = (KsContactRecord)MemberwiseClone();
        clone.Sources = new Dictionary<EntityUid, KsSourceRecord>(Sources);
        clone.KnownIntel = new Dictionary<ProtoId<KsSensorIntelPrototype>, KsKnownIntel>(KnownIntel);
        return clone;
    }
}

/// <summary>One sticky readout's last-known value and the server time it was observed.</summary>
public readonly record struct KsKnownIntel(string Value, TimeSpan Seen);

/// <summary>
///     What one origin sensor knows about a contact. Immutable once created (clone-with
///         instead of mutating) so pool snapshots can share references.
/// </summary>
public sealed record KsSourceRecord
{
    public EntityUid Sensor;
    public NetEntity SensorNet;
    public string SensorName = string.Empty;
    public NetEntity SourceGridNet;
    public string SourceGridName = string.Empty;

    /// <summary>Datalink hops from the origin sensor to this pool's grid. 0 = own sensor.</summary>
    public int Hops;

    public TimeSpan LastSeen;
    public KsContactRenderMode RenderMode;

    /// <summary>
    ///     This source's own view of the target's motion, already gated by its sensor's
    ///         <see cref="KsSensorComponent.RevealVelocity"/> (zero when it hides heading).
    ///         Per-source rather than on the record for the same reason as
    ///         <see cref="RenderMode"/>: two sensors tracking one grid in the same tick can
    ///         disagree about how much they reveal, and the record keeps only the freshest
    ///         write, so a hiding source (ELINT) would otherwise erase motion a revealing
    ///         one (IRST) legitimately earned. The console reads the winning source's copy.
    /// </summary>
    public Vector2 LinearVelocity;

    /// <summary>The kind of sensor this source is, driving the contact's confidence tier / colour.</summary>
    public KsSensorType Type;

    /// <summary>
    ///     How well this source resolves the target's position:
    ///         <c>detection.QualityOverride ?? sensor.ResolvesPosition</c> at sweep time.
    ///         Relays carry it verbatim, so an ally's bearing track never arrives as an
    ///         exact fix and never downgrades one (the snapshot takes the winning source's
    ///         quality, live sources first).
    /// </summary>
    public KsPositionQuality Quality = KsPositionQuality.Exact;

    /// <summary>
    ///     Where this source measured its bearing FROM (the sensor mount's world position
    ///         at measurement time). Stored at sweep time rather than recomputed at
    ///         snapshot build: a ghost's strobe must stay frozen at the last measurement,
    ///         or a moving viewer would receive a family of rays through the withheld
    ///         last-known point and could intersect them client-side. Only meaningful when
    ///         <see cref="Quality"/> is Bearing.
    /// </summary>
    public Vector2 BearingOrigin;

    /// <summary>
    ///     The measured direction from <see cref="BearingOrigin"/> toward the target's true
    ///         position: a mathematical direction angle (atan2 of the world delta). Frozen
    ///         with the origin, see there.
    /// </summary>
    public Angle Bearing;

    /// <summary>Display wedge half-width in degrees, copied from the measuring sensor.</summary>
    public float BearingAccuracy;

    /// <summary>
    ///     Relative strength of the heard emission in (0, 1], measured at sweep time
    ///         (distance into the emission's reach) and frozen with the bearing, like
    ///         <see cref="BearingOrigin"/>. 1 for non-emission bearings.
    /// </summary>
    public float SignalStrength = 1f;

    /// <summary>
    ///     This sensor's PREVIOUS bearing measurement of the contact, rolled forward at
    ///         sweep time so the snapshot builder can classify the bearing's drift rate
    ///         (STABLE / DRIFTING) without the pipeline keeping full history. Only
    ///         meaningful when <see cref="PrevBearingAt"/> is set.
    /// </summary>
    public Angle PrevBearing;

    /// <summary>When <see cref="PrevBearing"/> was measured. <see cref="TimeSpan.MinValue"/> = no history yet.</summary>
    public TimeSpan PrevBearingAt = TimeSpan.MinValue;

    /// <summary>
    ///     The measuring sensor's triangulation threshold in degrees (0 = never), copied at
    ///         sweep time so a relayed source keeps its sensor's capability without the
    ///         receiving pool touching the remote entity.
    /// </summary>
    public float TriangulateMinBaseline;

    public string? Name;
    public Dictionary<ProtoId<KsSensorIntelPrototype>, string>? Intel;

    /// <summary>The heard emission's frequency band, when this source located an emitter (identification intel).</summary>
    public ProtoId<KsEmitterBandPrototype>? Band;

    /// <summary>The heard emission's pattern over time, alongside <see cref="Band"/>.</summary>
    public KsEmissionPattern? Pattern;
}
