using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Sensors;
using Content.Shared._KS14.Sensors.Prototypes;
using Content.Shared.Examine;
using Content.Shared.Maps;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Sensors;

/// <summary>
///     Orchestrates the sensor framework: one global tick that sweeps every
///         operational sensor, merges detections into per-grid contact pools,
///         resolves datalink broadcasts between grids, expires stale memory and
///         pushes fresh snapshots to open console UIs. Everything is evaluated on
///         the same tick by design.
/// </summary>
public sealed partial class KsSensorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private ShuttleConsoleSystem _shuttleConsole = default!;
    [Dependency] private RadarConsoleSystem _radarConsole = default!;
    [Dependency] private KsSensorIntelSystem _intel = default!;

    /// <summary>
    ///     Global sensor tick: all sensors, datalinks and console pushes run on this
    ///         cadence. Backed by <see cref="KsCCVars.SensorsUpdateInterval"/>.
    /// </summary>
    private TimeSpan _updateInterval;

    /// <summary>
    ///     Sources older than this are pruned from a contact (the freshest one is always
    ///         kept for attribution). Backed by <see cref="KsCCVars.SensorsSourceRetention"/>.
    /// </summary>
    private TimeSpan _sourceRetention;

    /// <summary>
    ///     Base number of ticks a contact stays a live track after its last sighting.
    ///         Backed by <see cref="KsCCVars.SensorsLiveWindowTicks"/>.
    /// </summary>
    private int _liveWindowTicks;

    /// <summary>
    ///     Bearing drift rate (deg/s) above which a track reads DRIFTING.
    ///         Backed by <see cref="KsCCVars.SensorsDriftThreshold"/>.
    /// </summary>
    private float _driftThreshold;

    /// <summary>
    ///     Emission-log ring size. Backed by <see cref="KsCCVars.SensorsEmissionLogEntries"/>.
    /// </summary>
    private int _emissionLogEntries;

    private TimeSpan _nextTick;

    /// <summary>
    ///     Set when a derived console state changed without a contact-pool mutation (a
    ///         radar toggled on/off, a grid's jam state flipped), so the next tick's
    ///         change-gated push still fires and the ON/OFF label, emitting cone and
    ///         JAMMED alarm never go stale. Consumed in <see cref="Update"/>.
    /// </summary>
    private bool _forceConsolePush;

    /// <summary>Transmitters each receiver heard last tick, for receiver UIs.</summary>
    private readonly Dictionary<EntityUid, int> _heardTransmitters = new();

    /// <summary>
    ///     Ally grids whose contact-relaying datalink each grid heard last tick: the
    ///         network whose coverage cones the sector map may draw. Gated on
    ///         <see cref="KsDatalinkTransmitterComponent.RelayContacts"/>, since a
    ///         self-report-only beacon shares its position, not its picture. Rebuilt
    ///         every datalink tick alongside <see cref="_heardTransmitters"/>.
    /// </summary>
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _coverageLinks = new();

    /// <summary>
    ///     Per-tick cache of computed sensor coverage regions, so several
    ///         consoles on one grid share the (occluder-ray) work each push.
    /// </summary>
    private readonly Dictionary<EntityUid, (GameTick Tick, List<KsSensorRegionState>? Regions)> _regionCache = new();

    /// <summary>
    ///     Per-tick cache of each sensor's (or jammer's) WORLD-space coverage fan,
    ///         keyed by the mount entity. A relay network would otherwise recompute
    ///         the same occluder-raycast fan once per receiving console grid; the
    ///         region cache above can't help there because each receiver files the
    ///         points in its own local frame. Cleared with <see cref="_regionCache"/>.
    /// </summary>
    private readonly Dictionary<EntityUid, (List<Vector2>? World, bool Emitting)> _fanCache = new();

    /// <summary>
    ///     Scratch, rebuilt each expiry pass: every operational sensor grouped by
    ///         the grid it rides, so a grid's memory ghosts can be tested against
    ///         its own sensors' current view (the "look and it's gone" prune).
    /// </summary>
    private readonly Dictionary<EntityUid, List<Entity<KsSensorComponent>>> _operationalSensorsByGrid = new();

    /// <summary>
    ///     Each console grid's world yaw as of its last region build, so the tick can
    ///         force a push when a watched hull turns: fan boundaries go over the wire
    ///         world-oriented, which the client cannot rotate with the hull the way it
    ///         could the old grid-local points, so in a quiet sector (no pool change,
    ///         no datalink) a yawing ship would otherwise keep drawing its hull-shadow
    ///         notches at their old world bearing forever.
    /// </summary>
    private readonly Dictionary<EntityUid, double> _lastPushYaw = new();

    private readonly List<EntityUid> _yawScratch = new();

    /// <summary>Yaw delta (radians, ~0.06 degrees) past which a watched grid's rotation forces a push. Measured against the LAST PUSH, so a slow roll still accumulates into one.</summary>
    private const double YawPushEpsilon = 0.001;

    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<MapGridComponent> _gridQuery;

    public override void Initialize()
    {
        base.Initialize();

        _cfg.OnValueChanged(KsCCVars.SensorsUpdateInterval, v => _updateInterval = TimeSpan.FromSeconds(v), invokeImmediately: true);
        _cfg.OnValueChanged(KsCCVars.SensorsSourceRetention, v => _sourceRetention = TimeSpan.FromSeconds(v), invokeImmediately: true);
        _cfg.OnValueChanged(KsCCVars.SensorsLiveWindowTicks, v => _liveWindowTicks = v, invokeImmediately: true);
        _cfg.OnValueChanged(KsCCVars.SensorsDriftThreshold, v => _driftThreshold = v, invokeImmediately: true);
        _cfg.OnValueChanged(KsCCVars.SensorsEmissionLogEntries, v => _emissionLogEntries = Math.Max(1, v), invokeImmediately: true);

        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();

        SubscribeLocalEvent<KsCollectNavContactsEvent>(OnCollectNavContacts);

        SubscribeLocalEvent<KsSensorComponent, ExaminedEvent>(OnSensorExamined);

        // Push a full picture the moment a radar console UI opens, and scrub the
        // stored (PVS-replicated) BUI state when the last viewer leaves. The shuttle
        // console equivalents live as KS14-marked lines in ShuttleConsoleSystem's own
        // handlers: the engine allows only one subscriber per (component, event) pair
        // and it already holds them.
        SubscribeLocalEvent<RadarConsoleComponent, BoundUIOpenedEvent>(OnRadarConsoleUiOpened);
        SubscribeLocalEvent<RadarConsoleComponent, BoundUIClosedEvent>(OnRadarConsoleUiClosed);

        // Everything below changes what a console draws while mutating no contact pool, so
        // without a forced push the change-gated refresh never fires and the picture
        // latches. On a ship with nothing in its pool (a raider alone in a sector) it
        // latches forever. Rotation matters because the jam wedge follows the mount
        // (ThrusterSystem subscribes MoveEvent for the same reason). Mounting, unmounting,
        // powering and unpowering an emitter change the toggles' visibility and ON/OFF
        // labels the same way.
        SubscribeLocalEvent<KsJammerComponent, MoveEvent>(OnJammerMoved);
        SubscribeLocalEvent<KsJammerComponent, PowerChangedEvent>(OnEmitterPowerChanged);
        SubscribeLocalEvent<KsJammerComponent, AnchorStateChangedEvent>(OnEmitterAnchorChanged);
        SubscribeLocalEvent<KsJammerComponent, ComponentStartup>(OnEmitterAddedOrRemoved);
        SubscribeLocalEvent<KsJammerComponent, ComponentShutdown>(OnEmitterAddedOrRemoved);
        SubscribeLocalEvent<KsSensorComponent, PowerChangedEvent>(OnEmitterPowerChanged);
        SubscribeLocalEvent<KsSensorComponent, AnchorStateChangedEvent>(OnEmitterAnchorChanged);
        SubscribeLocalEvent<KsSensorComponent, ComponentStartup>(OnEmitterAddedOrRemoved);
        SubscribeLocalEvent<KsSensorComponent, ComponentShutdown>(OnEmitterAddedOrRemoved);
    }

    private void OnJammerMoved(Entity<KsJammerComponent> ent, ref MoveEvent args)
    {
        // Only rotation reshapes the wedge relative to the grid. Translation of the whole
        // ship moves the console's frame with it, so reacting to it would set the flag
        // every tick the ship is under thrust.
        if (args.NewRotation != args.OldRotation)
            _forceConsolePush = true;
    }

    private void OnEmitterPowerChanged<T>(Entity<T> ent, ref PowerChangedEvent args) where T : IComponent
    {
        _forceConsolePush = true;
    }

    private void OnEmitterAnchorChanged<T>(Entity<T> ent, ref AnchorStateChangedEvent args) where T : IComponent
    {
        _forceConsolePush = true;
    }

    private void OnEmitterAddedOrRemoved<T, TEvent>(Entity<T> ent, ref TEvent args) where T : IComponent
    {
        _forceConsolePush = true;
    }

    private void OnRadarConsoleUiOpened(EntityUid uid, RadarConsoleComponent component, BoundUIOpenedEvent args)
    {
        _radarConsole.KsRefreshConsole(uid, component);
    }

    private void OnRadarConsoleUiClosed(EntityUid uid, RadarConsoleComponent component, BoundUIClosedEvent args)
    {
        _radarConsole.KsRefreshConsole(uid, component);
    }

    private void OnSensorExamined(EntityUid uid, KsSensorComponent component, ExaminedEvent args)
    {
        string status;
        if (!component.Enabled)
            status = "ks-sensor-examine-off";
        else if (!_power.IsPowered(uid))
            status = "ks-sensor-examine-unpowered";
        else if (component.RequireExternalMount && !IsExternallyMounted(uid, Transform(uid)))
            status = "ks-sensor-examine-not-mounted";
        else
            status = "ks-sensor-examine-operational";

        args.PushMarkup(Loc.GetString(status));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        if (curTime < _nextTick)
            return;

        // Hitch-safe: never tries to catch up missed ticks.
        _nextTick = curTime + _updateInterval;

        // Drop last tick's coverage caches so they can never accumulate entries for
        // grids/sensors since deleted. They only need to dedupe region builds across
        // the consoles pushed within a single tick.
        _regionCache.Clear();
        _fanCache.Clear();

        // Must precede every sweep: ELINT reads the registry, and jamming has to suppress
        // a radar's detections on the same tick it lands (see KsSensorSystem.Emissions.cs).
        RebuildEmissions();

        RunSweeps(curTime);
        RunDatalink(curTime);
        RunExpiry(curTime);

        // Push fresh pictures to whoever is watching, but only if anything actually
        // changed, so a quiet sector generates no console traffic.
        var anyChanged = false;
        var poolQuery = EntityQueryEnumerator<KsSensorContactPoolComponent>();
        while (poolQuery.MoveNext(out _, out var pool))
        {
            anyChanged |= pool.Changed;
            pool.Changed = false;
        }

        // Relayed coverage moves with the ALLY's hull, which mutates no pool: a
        // linked network must keep pushing every tick or a quiet sector would
        // freeze the allies' cones at their old console-local offsets.
        anyChanged |= _coverageLinks.Count > 0;

        // These changes mutate no contact pool, so without folding the flag in here the
        // change-gated push would miss them and the console would show stale feedback
        // until an unrelated contact happened to change.
        if (_forceConsolePush)
        {
            anyChanged = true;
            _forceConsolePush = false;
        }

        // See _lastPushYaw: a watched hull turning is a frame change the client
        // cannot reconstruct itself.
        anyChanged |= WatchedGridYawed();

        if (anyChanged)
        {
            _shuttleConsole.KsRefreshSensorConsoles();
            _radarConsole.KsRefreshOpenUis();
        }
    }

    /// <summary>How many transmitters the given receiver ingested from last tick.</summary>
    public int GetHeardCount(EntityUid receiver)
    {
        return _heardTransmitters.GetValueOrDefault(receiver);
    }

    /// <summary>
    ///     The live sensor tick cadence, for behaviors that accumulate per tick (ELINT
    ///         focus analysis converts it to progress per sweep).
    /// </summary>
    public TimeSpan UpdateInterval => _updateInterval;

    /// <summary>
    ///     Forces the next tick's console push even though no contact pool changed, for
    ///         derived state living on components (an ELINT's focus progress advancing,
    ///         a focus set/cleared) that consoles must still show moving.
    /// </summary>
    public void ForceConsolePush() => _forceConsolePush = true;

    /// <summary>Emitter-class source types: the ones that file a contact as a heard EMISSION (drives designations, the emission log and the wire EmitterLive flag).</summary>
    private static bool IsEmitterClass(KsSensorType type) => type is KsSensorType.Elint or KsSensorType.Rwr or KsSensorType.Jammer;

    /// <summary>
    ///     Whether a console on <paramref name="gridUid"/> may order focus analysis on
    ///         <paramref name="target"/>: the pool must currently ROSTER it as a
    ///         designated emitter (designated AND not tombstoned AND on the console's
    ///         map), the exact visibility test <see cref="BuildContactStates"/> applies.
    ///         Anything less is an oracle: a designated-but-hidden record (confirmed
    ///         gone, or charted on another map) survives in the pool exactly as long as
    ///         its grid is alive, so accepting it would let a modified client probe
    ///         hidden records and read "escaped vs destroyed" off the observable focus
    ///         state.
    /// </summary>
    public bool CanFocusContact(EntityUid gridUid, EntityUid target)
    {
        if (!TryComp<KsSensorContactPoolComponent>(gridUid, out var pool)
            || !pool.Contacts.TryGetValue(target, out var record))
            return false;

        return record.Designation != null
            && !IsTombstoned(record)
            && record.MapId == Transform(gridUid).MapID;
    }

    /// <summary>
    ///     Appends one entry to a grid pool's emission log, dropping the oldest past the
    ///         ring cap, and marks the pool changed so the push fires.
    /// </summary>
    private void AppendEmissionLog(KsSensorContactPoolComponent pool, KsEmissionLogKind kind, string? designation, string? name)
    {
        pool.EmissionLog.Add(new KsEmissionLogEntry(_timing.CurTime, kind, designation, name));

        var excess = pool.EmissionLog.Count - _emissionLogEntries;
        if (excess > 0)
            pool.EmissionLog.RemoveRange(0, excess);

        pool.Changed = true;
    }

    #region Sweep

    private void RunSweeps(TimeSpan curTime)
    {
        var query = EntityQueryEnumerator<KsSensorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var sensor, out var xform))
        {
            if (xform.GridUid is not { } gridUid)
                continue;

            if (!IsSensorOperational((uid, sensor, xform)))
                continue;

            // Jam suppression is NOT handled here: it rides the radar behavior
            // component (KsRadarSystem.OnSweep bails and emits the home-on-jam
            // return), so the sweep raise stays free of per-behavior branches.
            var ev = new KsSensorSweepEvent((uid, sensor));
            RaiseLocalEvent(uid, ref ev);

            if (ev.Detections.Count == 0)
                continue;

            var pool = EnsureComp<KsSensorContactPoolComponent>(gridUid);
            var sensorName = Name(uid);
            var gridName = Name(gridUid);
            var mapId = xform.MapID;
            var sensorWorldPos = _transform.GetWorldPosition(xform);

            foreach (var detection in ev.Detections)
            {
                if (detection.TargetGrid == gridUid)
                    continue;

                // Velocity gating is central: a sensor that hides heading zeroes the
                // velocity once here so nothing downstream ever sees the real motion. The
                // zeroed value lands on this source's own KsSourceRecord, so it binds only
                // contacts this sensor wins and cannot erase a co-tracking sensor's honest
                // reading.
                var effective = sensor.RevealVelocity
                    ? detection
                    : detection with { LinearVelocity = Vector2.Zero };

                // Position quality mirrors the tier override just below: the sensor's
                // YAML default unless this one detection says otherwise (home-on-jam).
                var quality = detection.QualityOverride ?? sensor.ResolvesPosition;

                var source = new KsSourceRecord
                {
                    Sensor = uid,
                    SensorNet = GetNetEntity(uid),
                    SensorName = sensorName,
                    SourceGridNet = GetNetEntity(gridUid),
                    SourceGridName = gridName,
                    Hops = 0,
                    LastSeen = curTime,
                    // An actively obscured target (IFF Hide) OR a sensor that hides
                    // silhouette both degrade to an anonymous blip; only a clear
                    // target on a silhouette-revealing sensor keeps its outline.
                    RenderMode = detection.Obscured || !sensor.RevealSilhouette
                        ? KsContactRenderMode.Blip
                        : sensor.RenderMode,
                    // A detection may file under a different tier than its producing
                    // sensor (an ELINT sensor filing a located jammer as a Jammer
                    // return, a jammed radar's home-on-jam return likewise).
                    Type = detection.TypeOverride ?? sensor.Type,
                    Quality = quality,
                    Name = sensor.ProvidesName && !detection.Obscured ? detection.Name : null,
                    Intel = detection.Intel,
                    LinearVelocity = effective.LinearVelocity,
                    Band = detection.Band,
                    Pattern = detection.Pattern,
                };

                if (quality == KsPositionQuality.Bearing)
                {
                    // The measured ray, frozen at sweep time (see KsSourceRecord.BearingOrigin):
                    // apex at the sensor mount, direction toward the target's true position.
                    var delta = detection.WorldPosition - sensorWorldPos;
                    source.BearingOrigin = sensorWorldPos;
                    source.Bearing = Math.Atan2(delta.Y, delta.X);
                    source.BearingAccuracy = sensor.BearingAccuracy;
                    source.TriangulateMinBaseline = sensor.TriangulateMinBaseline;
                    source.SignalStrength = detection.SignalStrength;

                    // Roll this sensor's previous bearing of the same contact forward so
                    // the snapshot builder can classify drift from the last two
                    // measurements. Same-tick duplicates (two radars on one emitter grid)
                    // keep the first detection via UpsertSource, so only a genuinely older
                    // measurement becomes history.
                    if (pool.Contacts.TryGetValue(detection.TargetGrid, out var prior)
                        && prior.Sources.TryGetValue(uid, out var priorSource)
                        && priorSource.Quality == KsPositionQuality.Bearing
                        && priorSource.LastSeen < curTime)
                    {
                        source.PrevBearing = priorSource.Bearing;
                        source.PrevBearingAt = priorSource.LastSeen;
                    }
                }

                MergeDetection(pool, effective, source, mapId);
            }
        }
    }

    /// <summary>
    ///     Whether the sensor is switched on, powered and (if required)
    ///         externally mounted: anchored with at least one spaced neighbor tile.
    /// </summary>
    public bool IsSensorOperational(Entity<KsSensorComponent?, TransformComponent?> sensor)
    {
        if (!Resolve(sensor, ref sensor.Comp1, ref sensor.Comp2))
            return false;

        if (!sensor.Comp1.Enabled || !_power.IsPowered(sensor.Owner))
            return false;

        // TODO KS14: sensors should be able to require cooling. This is the single
        // choke point to gate on it, e.g. a RequiresCooling flag + a coolant/heat check.
        return !sensor.Comp1.RequireExternalMount || IsExternallyMounted(sensor.Owner, sensor.Comp2);
    }

    private bool IsExternallyMounted(EntityUid uid, TransformComponent xform)
    {
        if (!xform.Anchored || xform.GridUid is not { } gridUid || !_gridQuery.TryGetComponent(gridUid, out var grid))
            return false;

        var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);

        foreach (var offset in new[] { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right })
        {
            // IsSpace (the same "faces vacuum" test ThrusterSystem uses for nozzle
            // exposure) counts bare void and lattice/scaffolding, not just empty tiles.
            // A sensor beside the exterior frame is mounted externally.
            if (_turf.IsSpace(_map.GetTileRef(gridUid, grid, indices + offset)))
                return true;
        }

        return false;
    }

    private void MergeDetection(KsSensorContactPoolComponent pool, KsSensorDetection detection, KsSourceRecord source, MapId mapId)
    {
        var record = GetOrNewRecord(pool, detection.TargetGrid);

        if (source.LastSeen >= record.LastSeen)
        {
            record.MapId = mapId;
            record.WorldPosition = detection.WorldPosition;
            record.Rotation = detection.Rotation;
            record.LinearVelocity = detection.LinearVelocity;
            record.Static = detection.Static;
            record.LocalBounds = detection.LocalBounds;
            record.LocalCenter = detection.LocalCenter;
            record.LastSeen = source.LastSeen;
            pool.Changed = true;
        }

        if (UpsertSource(record, source))
            pool.Changed = true;

        if (ApplyKnownIntel(record, source.Intel, source.LastSeen, source.Name))
            pool.Changed = true;

        // First filing from an emitter-class source names the emitter ("E-004").
        // Own sweeps only reach here (relays go through Ingest, which carries the
        // designation with the record), so the assigning grid is this pool's own:
        // for a zero-hop source, SourceGridNet IS the pool grid.
        if (record.Designation == null && IsEmitterClass(source.Type))
        {
            record.Designation = $"E-{pool.NextDesignation:000}";
            pool.NextDesignation++;
            record.FirstSeen = source.LastSeen;
            record.DesignatedBy = source.SourceGridNet;
            pool.Changed = true;
            // The emission-log entry comes from the expiry pass's emitter-live edge
            // detector, which also covers re-acquisitions and relayed tracks.
        }
    }

    /// <summary>
    ///     Folds a source's freshly detected intel and name into a record's sticky
    ///         last-known store: for every readout whose prototype is
    ///         <see cref="KsSensorIntelPrototype.Sticky"/>, and for the name, keeps the
    ///         newest value so it survives after the detecting sensor loses track (a
    ///         mass/size read by a visual pass stays under IRST-only tracking).
    ///         Non-sticky readouts are skipped: they come straight from the live sources
    ///         in <see cref="BuildIntel"/>. Returns whether anything changed.
    /// </summary>
    private bool ApplyKnownIntel(KsContactRecord record, Dictionary<ProtoId<KsSensorIntelPrototype>, string>? intel, TimeSpan seen, string? name)
    {
        var changed = false;

        if (intel != null)
        {
            foreach (var (field, value) in intel)
            {
                if (!_proto.TryIndex(field, out var proto) || !proto.Sticky)
                    continue;

                if (record.KnownIntel.TryGetValue(field, out var existing) && seen <= existing.Seen)
                    continue;

                record.KnownIntel[field] = new KsKnownIntel(value, seen);
                changed = true;
            }
        }

        if (name != null && seen > record.KnownNameSeen)
        {
            record.KnownName = name;
            record.KnownNameSeen = seen;
            changed = true;
        }

        return changed;
    }

    private KsContactRecord GetOrNewRecord(KsSensorContactPoolComponent pool, EntityUid target)
    {
        if (pool.Contacts.TryGetValue(target, out var record))
            return record;

        record = new KsContactRecord
        {
            TargetGrid = target,
            TargetNet = GetNetEntity(target),
        };
        pool.Contacts[target] = record;
        return record;
    }

    /// <summary>
    ///     Inserts or updates the per-origin-sensor source record for a contact, keeping
    ///         the freshest (lowest-hop) report when the same origin sensor reports
    ///         again. Returns true only when the stored record actually changed, so
    ///         callers know whether to re-send.
    /// </summary>
    private static bool UpsertSource(KsContactRecord record, KsSourceRecord source)
    {
        if (record.Sources.TryGetValue(source.Sensor, out var existing)
            && (existing.LastSeen > source.LastSeen
                || existing.LastSeen == source.LastSeen && existing.Hops <= source.Hops))
        {
            return false;
        }

        record.Sources[source.Sensor] = source;
        return true;
    }

    #endregion

    #region Datalink

    private void RunDatalink(TimeSpan curTime)
    {
        _heardTransmitters.Clear();
        _coverageLinks.Clear();

        // Snapshot every transmitter's broadcast BEFORE any ingest so
        // propagation is exactly one hop per tick and order-independent.
        // Per-transmitter (not per-grid) so self-reports are attributed to
        // the transmitter actually reaching you, per frequency.
        var snapshots = new Dictionary<EntityUid, List<KsContactRecord>>();

        var txQuery = EntityQueryEnumerator<KsDatalinkTransmitterComponent, TransformComponent>();
        var transmitters = new List<(EntityUid Uid, KsDatalinkTransmitterComponent Comp, TransformComponent Xform, EntityUid Grid)>();

        while (txQuery.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid is not { } gridUid)
                continue;

            // A power-independent beacon (IgnorePower) emits with no APC at all;
            // otherwise the transmitter needs power like any machine.
            if (!comp.Enabled || comp.PowerFraction <= 0f || (!comp.IgnorePower && !_power.IsPowered(uid)))
                continue;

            // A transmitter with nothing to say (e.g. AnnounceSelf off with an empty
            // pool, or RelayContacts off and no self-report) broadcasts an empty
            // snapshot. Skip it so a silent config never counts as "heard" by a receiver
            // and never joins the tick's transmitter set.
            var snapshot = BuildBroadcastSnapshot(gridUid, (uid, comp), curTime);
            if (snapshot.Count == 0)
                continue;

            transmitters.Add((uid, comp, xform, gridUid));
            snapshots[uid] = snapshot;
        }

        if (transmitters.Count == 0)
            return;

        var rxQuery = EntityQueryEnumerator<KsDatalinkReceiverComponent, TransformComponent>();
        while (rxQuery.MoveNext(out var rxUid, out var rx, out var rxXform))
        {
            if (rxXform.GridUid is not { } rxGrid)
                continue;

            if (!rx.Enabled || !_power.IsPowered(rxUid))
                continue;

            var rxPos = _transform.GetWorldPosition(rxXform);
            var heard = 0;

            foreach (var tx in transmitters)
            {
                if (tx.Grid == rxGrid)
                    continue;

                // A public beacon (BroadcastAllFrequencies) is heard on every
                // channel; otherwise the receiver must be tuned to its frequency.
                if (!tx.Comp.BroadcastAllFrequencies && tx.Comp.Frequency != rx.Frequency)
                    continue;

                if (tx.Xform.MapID != rxXform.MapID)
                    continue;

                // A sector-wide beacon (UnlimitedRange) skips distance falloff
                // entirely; otherwise reach is MaxRange scaled by the power slider.
                if (!tx.Comp.UnlimitedRange)
                {
                    var effectiveRange = tx.Comp.MaxRange * tx.Comp.PowerFraction;
                    var txPos = _transform.GetWorldPosition(tx.Xform);

                    if ((txPos - rxPos).LengthSquared() > effectiveRange * effectiveRange)
                        continue;
                }

                heard++;
                var pool = EnsureComp<KsSensorContactPoolComponent>(rxGrid);
                Ingest(pool, rxGrid, snapshots[tx.Uid], tx.Comp.HopLimit);

                // A pure repeater (AnnounceSelf off) withholds its own position from
                // the network, and its coverage cones would hand that exact position
                // (apex = the mount) plus facing to every listener, so cones relay only
                // when the transmitter announces itself too.
                if (tx.Comp.RelayContacts && tx.Comp.AnnounceSelf)
                    _coverageLinks.GetOrNew(rxGrid).Add(tx.Grid);
            }

            if (heard > 0)
                _heardTransmitters[rxUid] = heard;
        }
    }

    /// <summary>
    ///     What a grid tells the world: its full pool (live + memory) plus a self-report,
    ///         because being in a network reveals you to your network.
    /// </summary>
    private List<KsContactRecord> BuildBroadcastSnapshot(EntityUid gridUid, Entity<KsDatalinkTransmitterComponent> transmitter, TimeSpan curTime)
    {
        var transmitterUid = transmitter.Owner;
        var comp = transmitter.Comp;

        var snapshot = new List<KsContactRecord>();

        // Relay what we know about OTHER grids only when configured to; a pure
        // position beacon (RelayContacts false) forwards none of its own pool.
        if (comp.RelayContacts && TryComp<KsSensorContactPoolComponent>(gridUid, out var pool))
        {
            foreach (var record in pool.Contacts.Values)
            {
                if (Deleted(record.TargetGrid))
                    continue;

                // Nor one our own sensors confirmed gone: we don't relay intel we have
                // personally disproven.
                if (IsTombstoned(record))
                    continue;

                snapshot.Add(record.Clone());
            }
        }

        // Announce our own grid only when configured to; a pure relay/repeater
        // (AnnounceSelf false) forwards allies' tracks but never reveals itself.
        if (comp.AnnounceSelf
            && _physicsQuery.TryGetComponent(gridUid, out var physics)
            && _gridQuery.TryGetComponent(gridUid, out var grid))
        {
            var (worldPos, worldRot) = _transform.GetWorldPositionRotation(gridUid);

            var selfRecord = new KsContactRecord
            {
                TargetGrid = gridUid,
                TargetNet = GetNetEntity(gridUid),
                MapId = Transform(gridUid).MapID,
                WorldPosition = worldPos + worldRot.RotateVec(physics.LocalCenter),
                Rotation = worldRot,
                LinearVelocity = physics.LinearVelocity,
                Static = physics.BodyType == Robust.Shared.Physics.BodyType.Static,
                LocalBounds = grid.LocalAABB,
                LocalCenter = physics.LocalCenter,
                LastSeen = curTime,
            };

            selfRecord.Sources[transmitterUid] = new KsSourceRecord
            {
                Sensor = transmitterUid,
                SensorNet = GetNetEntity(transmitterUid),
                SensorName = Name(transmitterUid),
                SourceGridNet = GetNetEntity(gridUid),
                SourceGridName = Name(gridUid),
                Hops = 0,
                LastSeen = curTime,
                // How the self-report renders on allied consoles: a full announce
                // is a crisp Outline silhouette, an anonymous beacon a Blip dot.
                RenderMode = comp.SelfRenderMode,
                // An ally announcing itself over datalink is perfect knowledge:
                // filed as the top (visual) tier so it renders crisp, then tinted
                // as an ally by the client's self-report check. Exact for the same
                // reason: a ship knows exactly where it is.
                Type = KsSensorType.VisualSearch,
                Quality = KsPositionQuality.Exact,
                // Carry the grid's name only when configured to; otherwise an
                // anonymous outline at the transmitter's position.
                Name = comp.RevealName ? Name(gridUid) : null,
                // Convey everything a sensor would: a receiver-only grid should
                // learn the transmitting ship's size/mass/top speed, not just its
                // position and outline. Same evaluator the shipboard sensors use.
                Intel = _intel.Evaluate(comp.Intel, gridUid, physics, grid),
                // Ungated, unlike a sensor's: a ship reporting its own motion is
                // perfect knowledge, so there is nothing to hide from itself.
                LinearVelocity = physics.LinearVelocity,
            };

            // A silent transmitter still teaches allies its own sticky intel, so a
            // receiver that later loses the relay keeps the ship's last-known readout.
            var selfSource = selfRecord.Sources[transmitterUid];
            ApplyKnownIntel(selfRecord, selfSource.Intel, curTime, selfSource.Name);

            snapshot.Add(selfRecord);
        }

        return snapshot;
    }

    private void Ingest(KsSensorContactPoolComponent pool, EntityUid poolGrid, List<KsContactRecord> snapshot, int hopLimit)
    {
        foreach (var incoming in snapshot)
        {
            if (incoming.TargetGrid == poolGrid)
                continue;

            // If every incoming source is already at the hop limit, relaying this contact
            // would mint a sourceless record: it never prunes (source pruning keeps the
            // freshest, expiry only tombstones via own sensors), renders as a phantom
            // default-tier blip and rebroadcasts onward, cascading the target's position
            // past the hop cap HopLimit exists to enforce. Drop it before the record is
            // ever created.
            var anySurvives = false;
            foreach (var source in incoming.Sources.Values)
            {
                if (source.Hops + 1 <= hopLimit)
                {
                    anySurvives = true;
                    break;
                }
            }

            if (!anySurvives)
                continue;

            var record = GetOrNewRecord(pool, incoming.TargetGrid);

            if (incoming.LastSeen > record.LastSeen)
            {
                record.MapId = incoming.MapId;
                record.WorldPosition = incoming.WorldPosition;
                record.Rotation = incoming.Rotation;
                record.LinearVelocity = incoming.LinearVelocity;
                record.Static = incoming.Static;
                record.LocalBounds = incoming.LocalBounds;
                record.LocalCenter = incoming.LocalCenter;
                record.LastSeen = incoming.LastSeen;
                pool.Changed = true;
            }

            foreach (var source in incoming.Sources.Values)
            {
                var hops = source.Hops + 1;
                if (hops > hopLimit)
                    continue;

                if (UpsertSource(record, source with { Hops = hops }))
                    pool.Changed = true;
            }

            // Sticky intel and name ride the relay so a receiver that later loses the
            // source keeps the last-known readout, exactly as an own-sensor track does.
            foreach (var (field, known) in incoming.KnownIntel)
            {
                if (record.KnownIntel.TryGetValue(field, out var existing) && known.Seen <= existing.Seen)
                    continue;

                record.KnownIntel[field] = known;
                pool.Changed = true;
            }

            if (incoming.KnownName != null && incoming.KnownNameSeen > record.KnownNameSeen)
            {
                record.KnownName = incoming.KnownName;
                record.KnownNameSeen = incoming.KnownNameSeen;
                pool.Changed = true;
            }

            // Designations converge fleet-wide on the earliest assignment: two grids that
            // independently designated the same emitter settle on whoever filed it first,
            // tie broken by the lower assigning grid so both sides pick the same winner
            // (the TARGET entity is identical on both sides and cannot break the tie).
            // The metadata is adopted even when the visible string happens to match (both
            // pools independently picked "E-001"): otherwise the two sides keep different
            // FirstSeen keys for one label and a later three-way conflict resolves
            // differently on each.
            if (incoming.Designation != null)
            {
                var adopt = record.Designation == null
                    || incoming.FirstSeen < record.FirstSeen
                    || incoming.FirstSeen == record.FirstSeen && incoming.DesignatedBy.CompareTo(record.DesignatedBy) < 0;

                if (adopt
                    && (record.Designation != incoming.Designation
                        || record.FirstSeen != incoming.FirstSeen
                        || record.DesignatedBy != incoming.DesignatedBy))
                {
                    // Only a visible label change re-pushes consoles; the metadata
                    // alone renders nothing.
                    if (record.Designation != incoming.Designation)
                        pool.Changed = true;

                    record.Designation = incoming.Designation;
                    record.FirstSeen = incoming.FirstSeen;
                    record.DesignatedBy = incoming.DesignatedBy;
                }
            }
        }
    }

    #endregion

    #region Expiry

    /// <summary>
    ///     Whether a contact currently reads as a live track or has decayed to a memory
    ///         ghost. Relayed knowledge arrives up to one tick per hop late, so the live
    ///         window widens with the freshest source's hop count; judged by the freshest
    ///         source so a stale zero-hop track can't shrink the window of a live relay.
    ///         Shared by the expiry transition check and the console snapshot so the two
    ///         never disagree.
    /// </summary>
    private bool IsRecordLive(KsContactRecord record, TimeSpan curTime)
    {
        var freshestHops = 0;
        var freshestSeen = TimeSpan.MinValue;

        foreach (var source in record.Sources.Values)
        {
            if (source.LastSeen > freshestSeen)
            {
                freshestSeen = source.LastSeen;
                freshestHops = source.Hops;
            }
        }

        return curTime - record.LastSeen <= _updateInterval * (_liveWindowTicks + freshestHops);
    }

    /// <summary>
    ///     Whether one source is still actively tracking (its own last sighting is within
    ///         the live window). Used to pick a contact's displayed tier: a live source
    ///         outranks a stale one, so a target only IRST still sees turns red instead
    ///         of clinging to a stale visual source's grey. Mirrors
    ///         <see cref="IsRecordLive"/> but per source.
    /// </summary>
    private bool IsSourceLive(KsSourceRecord source, TimeSpan curTime)
    {
        return curTime - source.LastSeen <= _updateInterval * (_liveWindowTicks + source.Hops);
    }

    /// <summary>
    ///     Whether the freshest thing known about this contact is that its spot was
    ///         confirmed empty ("look and it's gone"). A tombstoned record is hidden from
    ///         consoles and never rebroadcast, but kept so a stale datalink relay can't
    ///         resurrect it or thrash the pool; a genuinely newer sighting (LastSeen past
    ///         the confirmation) revives it automatically.
    /// </summary>
    private static bool IsTombstoned(KsContactRecord record)
    {
        return record.ConfirmedGoneAt > record.LastSeen;
    }

    private void RunExpiry(TimeSpan curTime)
    {
        GatherOperationalSensors();

        var pruneRecords = new List<EntityUid>();
        var pruneSources = new List<EntityUid>();

        var query = EntityQueryEnumerator<KsSensorContactPoolComponent, TransformComponent>();
        while (query.MoveNext(out var poolGrid, out var pool, out var poolXform))
        {
            pruneRecords.Clear();

            // The pool rides a grid; its own sensors (if any) can vet its ghosts.
            var poolMapId = poolXform.MapID;
            var ownSensors = _operationalSensorsByGrid.GetValueOrDefault(poolGrid);

            foreach (var (target, record) in pool.Contacts)
            {
                // A deleted grid can never be re-detected; without this it would haunt
                // every linked pool forever.
                if (Deleted(record.TargetGrid))
                {
                    pruneRecords.Add(target);
                    continue;
                }

                var isLive = IsRecordLive(record, curTime);

                // Look and it's gone: a memory ghost never times out on its own, it
                // persists until we can prove it wrong. If one of our own sensors now
                // plainly sees the spot the contact was last at (in range, clear line of
                // sight) and still detects nothing there, the target has moved or died. A
                // still-present target would have been re-seen by this same tick's sweep
                // and would read as live here, so only genuinely vacated spots get here.
                //
                // We tombstone rather than delete: a linked ally may still be relaying
                // this contact, and deleting it outright would let the datalink re-ingest
                // re-create it every tick, thrash pool.Changed and re-push every console
                // forever. Keeping the record lets the ingest's freshness guards no-op it;
                // it stays hidden and unbroadcast until a newer sighting revives it.
                if (!isLive
                    && ownSensors != null
                    && record.MapId == poolMapId
                    && IsSpotConfirmedEmpty(ownSensors, record.TargetGrid, poolMapId, record.WorldPosition))
                {
                    if (!IsTombstoned(record))
                    {
                        record.WasLive = false;
                        pool.Changed = true;
                    }

                    record.ConfirmedGoneAt = curTime;
                    // Still edge-detect the emitter log here: confirming the spot empty on
                    // the very tick the emitter track went stale must not swallow its
                    // "went silent" line.
                    UpdateEmitterLive(pool, record, curTime);
                    continue;
                }

                // Prune ancient sources but always keep the freshest for attribution,
                // plus the freshest emitter-class source: it carries the record's heard
                // classification (see PickEmissionIdentity), and a fresher non-emitter
                // co-tracker (an ally's IRST relay, say) must not strip a designated
                // emitter down to an anonymous EMITTER row.
                if (record.Sources.Count > 1)
                {
                    var freshest = default(EntityUid);
                    var freshestSeen = TimeSpan.MinValue;
                    var freshestEmitter = default(EntityUid);
                    var freshestEmitterSeen = TimeSpan.MinValue;

                    foreach (var (sensorUid, source) in record.Sources)
                    {
                        if (source.LastSeen > freshestSeen)
                        {
                            freshestSeen = source.LastSeen;
                            freshest = sensorUid;
                        }

                        if (IsEmitterClass(source.Type) && source.LastSeen > freshestEmitterSeen)
                        {
                            freshestEmitterSeen = source.LastSeen;
                            freshestEmitter = sensorUid;
                        }
                    }

                    pruneSources.Clear();
                    foreach (var (sensorUid, source) in record.Sources)
                    {
                        if (sensorUid != freshest && sensorUid != freshestEmitter && curTime - source.LastSeen > _sourceRetention)
                            pruneSources.Add(sensorUid);
                    }

                    foreach (var sensorUid in pruneSources)
                    {
                        record.Sources.Remove(sensorUid);
                    }
                }

                // Liveness is time-derived: a contact silently decays from a live track
                // to a memory ghost as its last sighting ages, mutating nothing else in
                // the pool. Without flagging the flip, the tick's change-gated push would
                // never fire and consoles would keep drawing a stale live track
                // indefinitely (a ghost only leaves on a confirmed-empty spot or a
                // deleted grid).
                if (isLive != record.WasLive)
                {
                    record.WasLive = isLive;
                    pool.Changed = true;
                }

                UpdateEmitterLive(pool, record, curTime);
            }

            foreach (var target in pruneRecords)
            {
                // A pruned emitter track (its grid died) still deserves its log line;
                // the edge detector will never see this record again.
                if (pool.Contacts.TryGetValue(target, out var pruned) && pruned.WasEmitterLive)
                    AppendEmissionLog(pool, KsEmissionLogKind.EmitterSilent, pruned.Designation, pruned.KnownName);

                pool.Contacts.Remove(target);
                pool.Changed = true;
            }
        }
    }

    /// <summary>
    ///     Edge-detects whether a contact counts as a live emitter track (any live
    ///         emitter-class source) and logs the transitions on the pool's emission log.
    ///         Runs off the pool's own knowledge, so a relayed ELINT track logs exactly
    ///         like an own-sensor one and nothing unheard ever reaches the log.
    /// </summary>
    private void UpdateEmitterLive(KsSensorContactPoolComponent pool, KsContactRecord record, TimeSpan curTime)
    {
        var emitterLive = false;
        foreach (var source in record.Sources.Values)
        {
            if (IsEmitterClass(source.Type) && IsSourceLive(source, curTime))
            {
                emitterLive = true;
                break;
            }
        }

        if (emitterLive == record.WasEmitterLive)
            return;

        record.WasEmitterLive = emitterLive;
        AppendEmissionLog(pool,
            emitterLive ? KsEmissionLogKind.EmitterNew : KsEmissionLogKind.EmitterSilent,
            record.Designation,
            record.KnownName);
    }

    /// <summary>
    ///     Rebuilds <see cref="_operationalSensorsByGrid"/> for this expiry pass in one
    ///         pass over the sensors, sparing the ghost check from re-scanning every
    ///         sensor per pool.
    /// </summary>
    private void GatherOperationalSensors()
    {
        _operationalSensorsByGrid.Clear();

        var query = EntityQueryEnumerator<KsSensorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var sensor, out var xform))
        {
            if (xform.GridUid is not { } gridUid)
                continue;

            if (!IsSensorOperational((uid, sensor, xform)))
                continue;

            if (!_operationalSensorsByGrid.TryGetValue(gridUid, out var list))
            {
                list = new List<Entity<KsSensorComponent>>();
                _operationalSensorsByGrid[gridUid] = list;
            }

            list.Add((uid, sensor));
        }
    }

    /// <summary>
    ///     Whether any of the grid's own sensors currently has a clear, in-range view of
    ///         <paramref name="worldPos"/>, i.e. it would have detected a target sitting
    ///         there. Asks each sensor's behavior system with
    ///         <see cref="KsSensorPointVisibleEvent"/> so the answer uses the exact
    ///         line-of-sight the sensor detects with.
    /// </summary>
    private bool IsSpotConfirmedEmpty(List<Entity<KsSensorComponent>> sensors, EntityUid targetGrid, MapId mapId, Vector2 worldPos)
    {
        foreach (var sensor in sensors)
        {
            var ev = new KsSensorPointVisibleEvent(sensor, targetGrid, mapId, worldPos);
            RaiseLocalEvent(sensor.Owner, ref ev);

            if (ev.Visible)
                return true;
        }

        return false;
    }

    #endregion

    #region Console snapshots

    private void OnCollectNavContacts(ref KsCollectNavContactsEvent ev)
    {
        if (ev.Grid is not { } gridUid)
            return;

        // Contacts need a pool; coverage does not (a sensor with nothing in sight still
        // draws its empty field of view).
        if (TryComp<KsSensorContactPoolComponent>(gridUid, out var pool))
        {
            ev.Contacts = BuildContactStates(gridUid, pool);

            // Copied, not shared: BUI state serialization is deferred, and the pool's
            // ring mutates every tick.
            if (pool.EmissionLog.Count > 0)
                ev.EmissionLog = new List<KsEmissionLogEntry>(pool.EmissionLog);
        }

        ev.Regions = BuildSensorRegions(gridUid);

        ev.Jammed = IsGridJammed(gridUid);
        (ev.HasRadar, ev.RadarActive) = GridRadarState(gridUid);
        (ev.HasJammer, ev.JammerActive) = GridJammerState(gridUid);
        ev.HasElint = GridHasElint(gridUid);
        ev.HasRwr = GridHasRwr(gridUid);

        // Emission truth, not the toggles: an unpowered radar left switched on
        // reports RadarActive without actually deafening the ELINT.
        ev.ElintDeaf = ev.HasElint && IsGridEmitting(gridUid);
    }

    /// <summary>
    ///     Collects the drawable coverage fans of every operational sensor on the grid,
    ///         converted to grid-local points so the client redraws them smoothly as the
    ///         ship moves. Cached per tick so several consoles on one grid don't each pay
    ///         for the occluder ray casts.
    /// </summary>
    private List<KsSensorRegionState>? BuildSensorRegions(EntityUid gridUid)
    {
        if (_regionCache.TryGetValue(gridUid, out var cached) && cached.Tick == _timing.CurTick)
            return cached.Regions;

        List<KsSensorRegionState>? regions = null;
        var invMatrix = _transform.GetInvWorldMatrix(gridUid);
        var gridNet = GetNetEntity(gridUid);

        regions = CollectSensorFans(gridUid, gridNet, invMatrix, relayed: false, regions);
        regions = BuildJammerRegions(gridUid, gridNet, invMatrix, relayed: false, regions);

        // Datalinked allies' coverage, for the sector map's network picture. The apexes
        // go through the SAME console-grid inverse matrix, so relayed cones arrive
        // framed against the console grid like own ones: the ally's grid can be beyond
        // PVS, where the client could never resolve its transform itself. Between
        // pushes a relayed apex therefore rides the console grid, not the ally, which
        // is acceptable at the sensor-tick cadence (the same freeze contacts have).
        if (_coverageLinks.TryGetValue(gridUid, out var allies))
        {
            var mapId = Transform(gridUid).MapID;

            foreach (var ally in allies)
            {
                if (TerminatingOrDeleted(ally) || Transform(ally).MapID != mapId)
                    continue;

                regions = CollectSensorFans(ally, gridNet, invMatrix, relayed: true, regions);
                regions = BuildJammerRegions(ally, gridNet, invMatrix, relayed: true, regions);
            }
        }

        _regionCache[gridUid] = (_timing.CurTick, regions);
        _lastPushYaw[gridUid] = _transform.GetWorldRotation(gridUid).Theta;
        return regions;
    }

    /// <summary>
    ///     Whether any grid watched at its last region build has yawed past
    ///         <see cref="YawPushEpsilon"/> since. Every checked-off entry is dropped:
    ///         a still-watched grid is re-recorded by the push this forces, and a
    ///         no-longer-watched one stops forcing traffic after this one shot.
    /// </summary>
    private bool WatchedGridYawed()
    {
        var yawed = false;
        _yawScratch.Clear();

        foreach (var (grid, theta) in _lastPushYaw)
        {
            if (TerminatingOrDeleted(grid))
            {
                _yawScratch.Add(grid);
                continue;
            }

            if (Math.Abs(Angle.ShortestDistance(new Angle(theta), _transform.GetWorldRotation(grid)).Theta) > YawPushEpsilon)
            {
                yawed = true;
                _yawScratch.Add(grid);
            }
        }

        foreach (var grid in _yawScratch)
            _lastPushYaw.Remove(grid);

        return yawed;
    }

    /// <summary>
    ///     Appends the coverage fans of every operational, coverage-showing sensor on
    ///         <paramref name="sourceGrid"/>, with points transformed by
    ///         <paramref name="invMatrix"/> (the CONSOLE grid's inverse world matrix)
    ///         and filed against <paramref name="fileAs"/> (the console's grid). For
    ///         the console's own grid the two coincide; for a datalinked ally only the
    ///         sweep source differs and <paramref name="relayed"/> marks the cone.
    /// </summary>
    private List<KsSensorRegionState>? CollectSensorFans(EntityUid sourceGrid, NetEntity fileAs, Matrix3x2 invMatrix, bool relayed, List<KsSensorRegionState>? regions)
    {
        var query = EntityQueryEnumerator<KsSensorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var sensor, out var xform))
        {
            if (xform.GridUid != sourceGrid || !sensor.ShowCoverage)
                continue;

            if (!IsSensorOperational((uid, sensor, xform)))
                continue;

            if (!_fanCache.TryGetValue(uid, out var fan))
            {
                var ev = new KsSensorCoverageEvent((uid, sensor));
                RaiseLocalEvent(uid, ref ev);
                fan = (ev.WorldPoints, ev.Emitting);
                _fanCache[uid] = fan;
            }

            if (fan.World is not { Count: > 1 } world)
                continue;

            // Apex grid-local so the fan rides the ship; the boundary as world-oriented
            // offsets from the apex, because the rays are cast at world-fixed angles:
            // a spinning ship must not carry its occlusion notches around with the
            // hull. The client recombines them against its live transforms each frame.
            var points = new List<Vector2>(world.Count) { Vector2.Transform(world[0], invMatrix) };
            for (var i = 1; i < world.Count; i++)
                points.Add(world[i] - world[0]);

            regions ??= new();
            regions.Add(new KsSensorRegionState
            {
                Grid = fileAs,
                Sensor = GetNetEntity(uid),
                Type = sensor.Type,
                // An actively-emitting behavior marks its cone on the coverage event
                // (radar: on == emitting), so the "you are lit up" pulse rides the
                // behavior component rather than the classification enum.
                Emitting = fan.Emitting,
                Relayed = relayed,
                WorldOffsets = true,
                Points = points,
            });
        }

        return regions;
    }

    private List<KsSensorContactState> BuildContactStates(EntityUid gridUid, KsSensorContactPoolComponent pool)
    {
        var curTime = _timing.CurTime;
        var states = new List<KsSensorContactState>(pool.Contacts.Count);
        var viewMapId = Transform(gridUid).MapID;
        var focusProgress = CollectFocusProgress(gridUid);

        foreach (var record in pool.Contacts.Values)
        {
            if (record.TargetGrid == gridUid)
                continue;

            // A tombstoned contact was confirmed gone by our own sensors; it stays in the
            // pool only to fend off stale relays, never on a console.
            if (IsTombstoned(record))
                continue;

            // Contacts charted on other maps stay in memory but don't render here: their
            // coordinates mean nothing on this map.
            if (record.MapId != viewMapId)
                continue;

            // Defensive: a sourceless record has no provenance and no real tier, so
            // it must never surface (the ingest hop-limit guard prevents minting one;
            // this stops any future path from drawing a phantom default-tier blip).
            if (record.Sources.Count == 0)
                continue;

            // The winning source dictates the contact's tier (colour) and render shape.
            // The name is sticky (record.KnownName) so a nameless IRST track still shows
            // a name an earlier visual pass already learned. Velocity rides the winner
            // too, so a heading-hiding source never erases motion a revealing one
            // reported.
            var (type, renderMode, velocity, quality) = PickWinningSource(record, curTime);

            // The winner's quality decides whether this snapshot carries a position at
            // all, with one earned escape: two live bearing tracks from far enough apart
            // triangulate into an Exact fix (a pure reveal; the record's truth was always
            // there, the sources just earned showing it).
            if (quality == KsPositionQuality.Bearing && CanTriangulate(record, curTime))
                quality = KsPositionQuality.Exact;

            var (band, pattern) = PickEmissionIdentity(record);

            KsSensorContactState state;
            if (quality == KsPositionQuality.Bearing)
            {
                var strobe = CollapseBearing(gridUid, pool, record, viewMapId, curTime, out var strobeSource);

                // Security invariant: a Bearing-quality state carries NO position block.
                // Zeroed here, not hidden client-side, because the client cannot render
                // what it never receives. The name stays: identity learned by an earlier
                // exact track is sticky intel, not position.
                state = new KsSensorContactState
                {
                    Grid = record.TargetNet,
                    Name = record.KnownName,
                    Live = IsRecordLive(record, curTime),
                    LastSeen = record.LastSeen,
                    RenderMode = KsContactRenderMode.Blip,
                    Type = type,
                    Quality = quality,
                    Bearing = strobe,
                    Stability = ClassifyStability(strobeSource),
                    // The strobe's own strength, so the panel and the drawn wedge agree.
                    SignalStrength = strobe?.SignalStrength,
                    Intel = BuildIntel(record),
                    Sources = BuildSources(record),
                };
            }
            else
            {
                // A revealed position releases the strobe-anchor pin: after the client has
                // legitimately seen the fix, a later re-bearing from a new anchor teaches
                // it nothing it was not shown.
                record.StrobeGrid = null;
                state = new KsSensorContactState
                {
                    Grid = record.TargetNet,
                    Name = record.KnownName,
                    WorldPosition = record.WorldPosition,
                    Rotation = record.Rotation,
                    // From the winning source, not the record: only that source's own
                    // reveal-velocity gating decides what heading the console draws.
                    LinearVelocity = velocity,
                    Static = record.Static,
                    Live = IsRecordLive(record, curTime),
                    LastSeen = record.LastSeen,
                    RenderMode = renderMode,
                    Type = type,
                    Quality = quality,
                    LocalBounds = record.LocalBounds,
                    LocalCenter = record.LocalCenter,
                    // The heard-emission product survives the fix: closing in and
                    // earning a position must not read back as SIGNAL loss.
                    SignalStrength = PickHeardSignal(record),
                    Intel = BuildIntel(record),
                    Sources = BuildSources(record),
                };
            }

            state.Designation = record.Designation;
            state.Band = band;
            state.Pattern = pattern;
            // The emission-log edge detector's current state, not a re-derivation: the
            // wire flag and the log lines must never disagree about whether an emitter is
            // being heard right now.
            state.EmitterLive = record.WasEmitterLive;
            if (focusProgress != null && focusProgress.TryGetValue(record.TargetGrid, out var progress))
            {
                state.Focused = true;
                state.AnalysisProgress = progress;
            }

            states.Add(state);
        }

        return states;
    }

    /// <summary>
    ///     The viewing grid's ELINT focus state, keyed by focused target grid: the best
    ///         analysis progress across the grid's arrays. Null when nothing on the grid
    ///         is focusing (the common case, so no allocation).
    /// </summary>
    private Dictionary<EntityUid, float>? CollectFocusProgress(EntityUid gridUid)
    {
        Dictionary<EntityUid, float>? result = null;

        var query = EntityQueryEnumerator<KsElintComponent, TransformComponent>();
        while (query.MoveNext(out _, out var elint, out var xform))
        {
            if (xform.GridUid != gridUid || elint.FocusTarget is not { } target || Deleted(target))
                continue;

            result ??= new Dictionary<EntityUid, float>();
            if (!result.TryGetValue(target, out var best) || elint.FocusProgress > best)
                result[target] = elint.FocusProgress;
        }

        return result;
    }

    /// <summary>
    ///     The freshest band/pattern classification any source heard for this contact.
    ///         Not sticky: it lives on the sources, and source pruning always keeps a
    ///         contact's freshest source, so a track last heard by ELINT keeps its band
    ///         until something fresher replaces the knowledge.
    /// </summary>
    private static (ProtoId<KsEmitterBandPrototype>? Band, KsEmissionPattern? Pattern) PickEmissionIdentity(KsContactRecord record)
    {
        ProtoId<KsEmitterBandPrototype>? band = null;
        KsEmissionPattern? pattern = null;
        var bandSeen = TimeSpan.MinValue;
        var patternSeen = TimeSpan.MinValue;

        foreach (var source in record.Sources.Values)
        {
            if (source.Band != null && source.LastSeen > bandSeen)
            {
                band = source.Band;
                bandSeen = source.LastSeen;
            }

            if (source.Pattern != null && source.LastSeen > patternSeen)
            {
                pattern = source.Pattern;
                patternSeen = source.LastSeen;
            }
        }

        return (band, pattern);
    }

    /// <summary>
    ///     Security invariant: the wire signal strength is quantized to quarter steps
    ///         (25/50/75/100). The raw measurement is 1 - distance/reach, and ELINT also
    ///         reveals the emitter's range readout, so a precise percentage would let the
    ///         player invert it into an accurate RANGE along the known bearing, a derived
    ///         position fix the quality gate exists to withhold. Quarter steps keep the
    ///         derived range a wide band. The raw value stays on the source record
    ///         server-side.
    /// </summary>
    private static float QuantizeSignal(float raw)
    {
        return Math.Clamp(MathF.Ceiling(raw * 4f) / 4f, 0.25f, 1f);
    }

    /// <summary>
    ///     Classifies the strobe source's bearing drift from its last two measurements:
    ///         at or under the drift-threshold CVar reads STABLE, above it DRIFTING, no
    ///         history (or no strobe) UNKNOWN.
    /// </summary>
    private KsBearingStability ClassifyStability(KsSourceRecord? source)
    {
        if (source == null || source.PrevBearingAt == TimeSpan.MinValue || source.LastSeen <= source.PrevBearingAt)
            return KsBearingStability.Unknown;

        var dt = (source.LastSeen - source.PrevBearingAt).TotalSeconds;
        var rate = Math.Abs(Angle.ShortestDistance(source.PrevBearing, source.Bearing).Degrees) / dt;
        return rate <= _driftThreshold ? KsBearingStability.Stable : KsBearingStability.Drifting;
    }

    private List<(ProtoId<KsSensorIntelPrototype>, string)> BuildIntel(KsContactRecord record)
    {
        var result = new List<(ProtoId<KsSensorIntelPrototype>, string)>();

        // Sticky readouts persist as last-known values, so a field a visual pass resolved
        // stays visible under IRST-only tracking. Non-sticky readouts come from the
        // current sources (freshest wins) and clear once none reports them.
        foreach (var (intel, known) in record.KnownIntel)
            result.Add((intel, known.Value));

        var merged = new Dictionary<ProtoId<KsSensorIntelPrototype>, (TimeSpan Seen, string Value)>();
        foreach (var source in record.Sources.Values)
        {
            if (source.Intel == null)
                continue;

            foreach (var (intel, value) in source.Intel)
            {
                // Sticky keys are served from KnownIntel above; never from a source.
                if (record.KnownIntel.ContainsKey(intel)
                    || _proto.TryIndex(intel, out var proto) && proto.Sticky)
                    continue;

                if (!merged.TryGetValue(intel, out var existing) || source.LastSeen > existing.Seen)
                    merged[intel] = (source.LastSeen, value);
            }
        }

        foreach (var (intel, entry) in merged)
        {
            result.Add((intel, entry.Value));
        }

        result.Sort((a, b) =>
        {
            var orderA = _proto.TryIndex(a.Item1, out var protoA) ? protoA.Order : 0;
            var orderB = _proto.TryIndex(b.Item1, out var protoB) ? protoB.Order : 0;
            return orderA != orderB ? orderA.CompareTo(orderB) : string.Compare(a.Item1.Id, b.Item1.Id, StringComparison.Ordinal);
        });

        return result;
    }

    /// <summary>
    ///     Picks the source that dictates a contact's rendering: live sources first, then
    ///         the better tier, then (among equally-live equal-tier sources) the better
    ///         POSITION QUALITY, then the freshest. Without the quality rank a fresher own
    ///         bearing track would beat a relayed same-tier Exact source forever, so the
    ///         receiver of an ally's completed focus analysis would never show the fleet's
    ///         earned fix. A LIVE Exact source licenses the record's current truth, so
    ///         preferring it leaks nothing. For a ghost, freshest still wins outright: an
    ///         old Exact source must not license showing a fresh position that only a
    ///         Bearing source measured.
    ///     Extracted so <see cref="EffectiveQuality"/> can never disagree with what
    ///         <see cref="BuildContactStates"/> actually renders.
    /// </summary>
    private (KsSensorType Type, KsContactRenderMode RenderMode, Vector2 Velocity, KsPositionQuality Quality) PickWinningSource(KsContactRecord record, TimeSpan curTime)
    {
        var type = KsSensorType.VisualSearch;
        var renderMode = KsContactRenderMode.Blip;
        var velocity = Vector2.Zero;
        var quality = KsPositionQuality.Exact;
        var winnerSeen = TimeSpan.MinValue;
        var winnerLive = false;
        var hasWinner = false;

        foreach (var source in record.Sources.Values)
        {
            var live = IsSourceLive(source, curTime);

            bool better;
            if (!hasWinner)
                better = true;
            else if (live != winnerLive)
                better = live; // a live source always outranks a stale one
            else if (live)
                better = source.Type < type
                    || source.Type == type
                        && (source.Quality < quality
                            || source.Quality == quality && source.LastSeen > winnerSeen);
            else
                better = source.LastSeen > winnerSeen || source.LastSeen == winnerSeen && source.Type < type;

            if (better)
            {
                hasWinner = true;
                type = source.Type;
                renderMode = source.RenderMode;
                velocity = source.LinearVelocity;
                quality = source.Quality;
                winnerSeen = source.LastSeen;
                winnerLive = live;
            }
        }

        return (type, renderMode, velocity, quality);
    }

    /// <summary>
    ///     The client-facing position quality a record's snapshot would carry: the winning
    ///         source's quality plus the triangulation reveal. Shared with
    ///         <see cref="CollapseBearing"/>'s ally-anchor gate, so a strobe can only ever
    ///         be anchored at a position the ally's own contact state would show.
    /// </summary>
    private KsPositionQuality EffectiveQuality(KsContactRecord record, TimeSpan curTime)
    {
        var quality = PickWinningSource(record, curTime).Quality;

        if (quality == KsPositionQuality.Bearing && CanTriangulate(record, curTime))
            return KsPositionQuality.Exact;

        return quality;
    }

    /// <summary>
    ///     The datalink triangulation reveal: whether any two LIVE bearing tracks of this
    ///         contact, measured from distinct grids, are separated by enough parallax to
    ///         fix the target. The required separation is the stricter of the pair's
    ///         <see cref="KsSensorComponent.TriangulateMinBaseline"/> thresholds (0 = that
    ///         sensor never participates). Stale tracks never count: their frozen rays no
    ///         longer cross the target.
    /// </summary>
    private bool CanTriangulate(KsContactRecord record, TimeSpan curTime)
    {
        List<KsSourceRecord>? candidates = null;

        foreach (var source in record.Sources.Values)
        {
            if (source.Quality != KsPositionQuality.Bearing || source.TriangulateMinBaseline <= 0f)
                continue;

            if (!IsSourceLive(source, curTime))
                continue;

            candidates ??= new List<KsSourceRecord>();
            candidates.Add(source);
        }

        if (candidates == null || candidates.Count < 2)
            return false;

        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                var a = candidates[i];
                var b = candidates[j];

                // Two sensors on one grid share (nearly) one vantage point: no baseline.
                if (a.SourceGridNet == b.SourceGridNet)
                    continue;

                var required = MathF.Max(a.TriangulateMinBaseline, b.TriangulateMinBaseline);
                var separation = Math.Abs(Angle.ShortestDistance(a.Bearing, b.Bearing).Degrees);

                if (separation >= required)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Picks the ONE bearing strobe a Bearing-quality contact may show. Security
    ///         invariant: two true centrelines from distinct origins intersect at the
    ///         withheld position, so multi-source bearings live server-side only; the
    ///         moment two of them pass the baseline test the contact IS Exact and the
    ///         plot may show the fix instead.
    ///     The viewing grid's own freshest bearing wins; otherwise the lowest-hop
    ///         freshest relay whose measuring ally this pool knows at EXACT effective
    ///         quality, because the wedge apex sits at the ally and anchoring it for an
    ///         ally the viewer only bearing-knows (or cannot place at all) would reveal
    ///         that ally's position through the back door.
    ///     The anchor is PINNED per contact (<see cref="KsContactRecord.StrobeGrid"/>):
    ///         once a strobe has been shown from one measuring grid, no other grid's
    ///         centreline is ever shown for the same still-Bearing contact, or the two
    ///         rays observed over time would intersect at a static target's withheld
    ///         position, sidestepping the triangulation baseline gate. The pin is
    ///         released when the contact goes Exact: the fix was legitimately shown, so
    ///         a fresh anchor afterwards adds nothing.
    ///     Returns null when nothing qualifies: the contact is roster-only.
    /// </summary>
    private KsBearingLine? CollapseBearing(EntityUid gridUid, KsSensorContactPoolComponent pool, KsContactRecord record, MapId viewMapId, TimeSpan curTime, out KsSourceRecord? chosen)
    {
        chosen = null;
        var gridNet = GetNetEntity(gridUid);
        var pinned = record.StrobeGrid;

        KsSourceRecord? own = null;
        KsSourceRecord? relay = null;
        Vector2 relayOrigin = default;

        foreach (var source in record.Sources.Values)
        {
            if (source.Quality != KsPositionQuality.Bearing)
                continue;

            if (pinned != null && source.SourceGridNet != pinned)
                continue;

            if (source.SourceGridNet == gridNet)
            {
                if (own == null || source.LastSeen > own.LastSeen)
                    own = source;

                continue;
            }

            if (own != null
                || relay != null
                    && (source.Hops > relay.Hops || source.Hops == relay.Hops && source.LastSeen <= relay.LastSeen))
                continue;

            // The apex of a relayed strobe is the position WE know for the measuring
            // ally (its datalink self-report), never the ally's true measured origin:
            // our record of the ally may be staler than its relayed bearing, and
            // anchoring the wedge at the fresher truth would hand the viewer the ally's
            // real position. The same rule demands the ally's own EFFECTIVE quality be
            // Exact: every pool record holds the truth, so an ally we merely bearing-know
            // (say, our ELINT hearing an announce-nothing relay ship's radar) has an
            // accurate WorldPosition its own contact state deliberately withholds, and
            // anchoring there would leak it. No revealable ally position, no strobe.
            if (!TryGetEntity(source.SourceGridNet, out var sourceGrid)
                || !pool.Contacts.TryGetValue(sourceGrid.Value, out var allyRecord)
                || IsTombstoned(allyRecord)
                || allyRecord.MapId != viewMapId
                || EffectiveQuality(allyRecord, curTime) != KsPositionQuality.Exact)
                continue;

            relay = source;
            relayOrigin = allyRecord.WorldPosition;
        }

        if (own != null)
        {
            record.StrobeGrid = own.SourceGridNet;
            chosen = own;
            return new KsBearingLine(own.SourceGridNet, own.BearingOrigin, own.Bearing, own.BearingAccuracy, QuantizeSignal(own.SignalStrength), own.LastSeen);
        }

        if (relay != null)
        {
            record.StrobeGrid = relay.SourceGridNet;
            chosen = relay;
            return new KsBearingLine(relay.SourceGridNet, relayOrigin, relay.Bearing, relay.BearingAccuracy, QuantizeSignal(relay.SignalStrength), relay.LastSeen);
        }

        // No strobe, but any existing pin stays: releasing it here would let the next
        // snapshot re-anchor at a different grid, the two-centreline reconstruction the
        // pin exists to prevent.
        return null;
    }

    /// <summary>
    ///     The heard-emission strength shipped alongside an Exact fix: the freshest
    ///         emitter-class source, own sensors preferred, mirroring the strobe's
    ///         own-over-relay policy. Needs none of <see cref="CollapseBearing"/>'s
    ///         ally-position gating because only the scalar ships - nothing
    ///         positional rides along.
    /// </summary>
    private static float? PickHeardSignal(KsContactRecord record)
    {
        KsSourceRecord? best = null;

        foreach (var source in record.Sources.Values)
        {
            if (!IsEmitterClass(source.Type))
                continue;

            if (best == null
                || source.Hops < best.Hops
                || source.Hops == best.Hops && source.LastSeen > best.LastSeen)
                best = source;
        }

        return best == null ? null : QuantizeSignal(best.SignalStrength);
    }

    private static List<KsContactSource> BuildSources(KsContactRecord record)
    {
        var result = new List<KsContactSource>(record.Sources.Count);

        foreach (var source in record.Sources.Values)
        {
            result.Add(new KsContactSource(source.SensorNet, source.SensorName, source.SourceGridNet, source.SourceGridName, source.Hops, source.LastSeen, source.Type, source.Quality));
        }

        // Own sensors first, then freshest.
        result.Sort((a, b) => a.Hops != b.Hops ? a.Hops.CompareTo(b.Hops) : b.LastSeen.CompareTo(a.LastSeen));
        return result;
    }

    #endregion
}
