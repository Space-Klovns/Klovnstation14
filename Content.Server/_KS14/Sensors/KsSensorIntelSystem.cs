using Content.Server.Shuttles.Components;
using Content.Shared._KS14.Sensors;
using Content.Shared._KS14.Sensors.Prototypes;
using Content.Shared.Maps;
using Robust.Shared.Timing;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Sensors;

/// <summary>
///     Evaluates a sensor's declared <see cref="KsSensorIntelPrototype"/> list against a
///         detected grid, producing the readout lines shown on radar-type UIs. Each
///         prototype names a <see cref="KsSensorMetric"/> to compute and carries its own
///         banding or numeric formatting, so a new readout for an existing quantity is
///         pure YAML. Unrecognised ids are skipped.
/// </summary>
public sealed partial class KsSensorIntelSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;

    private ISawmill _sawmill = default!;

    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<KsThermalSourceComponent> _thermalSourceQuery;
    private EntityQuery<KsRadarSourceComponent> _radarSourceQuery;

    private readonly HashSet<string> _warnedUnknown = new();

    // Thrust totals are summed over EVERY thruster in the game, so cache one
    // pass per tick instead of paying it per detection per sensor.
    private readonly Dictionary<EntityUid, float> _thrustCache = new();
    private GameTick _thrustCacheTick = GameTick.Zero;

    // Per-grid totals, recomputed only when the grid's skin geometry changes: the old
    // per-tick crawl re-walked every wall in the game (~2ms, station-size-unbounded).
    // One shared build: heat and RCS differ per wall but share the 8-tile exposure count.
    private readonly Dictionary<EntityUid, float> _thermalCache = new();
    private readonly Dictionary<EntityUid, float> _radarCache = new();
    private readonly HashSet<EntityUid> _dirtySignatureGrids = new();

    /// <summary>
    ///     The eight surrounding tiles (four faces + four corners) checked for space
    ///         exposure. A wall's contribution scales with how many are open to space, so a
    ///         corner or protruding wall radiates/reflects more than a flush one, and a
    ///         boxed-in wall (zero exposed) contributes nothing. Shared by the thermal
    ///         (IRST) and radar (RCS) crawlers.
    /// </summary>
    private static readonly Vector2i[] ExposureNeighbors =
    {
        Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right,
        Vector2i.Up + Vector2i.Left, Vector2i.Up + Vector2i.Right,
        Vector2i.Down + Vector2i.Left, Vector2i.Down + Vector2i.Right,
    };

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("ks.sensors.intel");
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _thermalSourceQuery = GetEntityQuery<KsThermalSourceComponent>();
        _radarSourceQuery = GetEntityQuery<KsRadarSourceComponent>();

        // The full geometry-change surface: walls entering/leaving the hull, and tile
        // changes (exposure counts space TILES, so wall placement alone never changes
        // a neighbour's). Signature values never mutate at runtime.
        SubscribeLocalEvent<KsThermalSourceComponent, ComponentStartup>(OnThermalSourceLifecycle);
        SubscribeLocalEvent<KsThermalSourceComponent, ComponentShutdown>(OnThermalSourceLifecycle);
        SubscribeLocalEvent<KsThermalSourceComponent, AnchorStateChangedEvent>(OnThermalSourceAnchor);
        SubscribeLocalEvent<KsThermalSourceComponent, ReAnchorEvent>(OnThermalSourceReAnchor);
        SubscribeLocalEvent<KsRadarSourceComponent, ComponentStartup>(OnRadarSourceLifecycle);
        SubscribeLocalEvent<KsRadarSourceComponent, ComponentShutdown>(OnRadarSourceLifecycle);
        SubscribeLocalEvent<KsRadarSourceComponent, AnchorStateChangedEvent>(OnRadarSourceAnchor);
        SubscribeLocalEvent<KsRadarSourceComponent, ReAnchorEvent>(OnRadarSourceReAnchor);
        SubscribeLocalEvent<MapGridComponent, TileChangedEvent>(OnGridTileChanged);
        // Not <MapGridComponent, ComponentShutdown>: lifecycle events allow one
        // handler per component type game-wide, and the map system owns that one.
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoval);
    }

    private void OnThermalSourceLifecycle(EntityUid uid, KsThermalSourceComponent comp, ComponentStartup args) => DirtySignatureGrid(uid);
    private void OnThermalSourceLifecycle(EntityUid uid, KsThermalSourceComponent comp, ComponentShutdown args) => DirtySignatureGrid(uid);
    private void OnThermalSourceAnchor(EntityUid uid, KsThermalSourceComponent comp, ref AnchorStateChangedEvent args) => DirtySignatureGrid(uid);
    private void OnRadarSourceLifecycle(EntityUid uid, KsRadarSourceComponent comp, ComponentStartup args) => DirtySignatureGrid(uid);
    private void OnRadarSourceLifecycle(EntityUid uid, KsRadarSourceComponent comp, ComponentShutdown args) => DirtySignatureGrid(uid);
    private void OnRadarSourceAnchor(EntityUid uid, KsRadarSourceComponent comp, ref AnchorStateChangedEvent args) => DirtySignatureGrid(uid);

    private void OnThermalSourceReAnchor(EntityUid uid, KsThermalSourceComponent comp, ref ReAnchorEvent args)
    {
        _dirtySignatureGrids.Add(args.OldGrid);
        _dirtySignatureGrids.Add(args.Grid);
    }

    private void OnRadarSourceReAnchor(EntityUid uid, KsRadarSourceComponent comp, ref ReAnchorEvent args)
    {
        _dirtySignatureGrids.Add(args.OldGrid);
        _dirtySignatureGrids.Add(args.Grid);
    }

    private void OnGridTileChanged(EntityUid uid, MapGridComponent comp, ref TileChangedEvent args) => _dirtySignatureGrids.Add(uid);

    private void OnGridRemoval(GridRemovalEvent args)
    {
        _thermalCache.Remove(args.EntityUid);
        _radarCache.Remove(args.EntityUid);
        _dirtySignatureGrids.Remove(args.EntityUid);
    }

    private void DirtySignatureGrid(EntityUid wallUid)
    {
        if (Transform(wallUid).GridUid is { } grid)
            _dirtySignatureGrids.Add(grid);
    }

    /// <summary>
    ///     A grid's thermal signature: the summed <see cref="KsThermalSourceComponent.Signature"/>
    ///         of every anchored exterior wall on it (one with at least one space-facing
    ///         neighbor tile). Interior walls are shielded and add nothing, so a boxed-in
    ///         hull runs colder than a sprawling one. This is what IRST detects and the HEAT
    ///         readout reports.
    /// </summary>
    public float GetThermalSignature(EntityUid grid)
    {
        if (_dirtySignatureGrids.Count > 0)
            BuildSignatureCaches();

        return _thermalCache.GetValueOrDefault(grid);
    }

    /// <summary>
    ///     A grid's radar cross-section: the same exposed-sides sum as
    ///         <see cref="GetThermalSignature"/> but over each wall's
    ///         <see cref="KsRadarSourceComponent.Signature"/>, an independent value, so a
    ///         Peltier-cold wall can still reflect radar normally and a radar-absorbent one
    ///         can run stealthy while radiating heat.
    /// </summary>
    public float GetRadarSignature(EntityUid grid)
    {
        if (_dirtySignatureGrids.Count > 0)
            BuildSignatureCaches();

        return _radarCache.GetValueOrDefault(grid);
    }

    /// <summary>
    ///     Still one pass over all walls even though only dirty grids recompute:
    ///         enumeration is cheap, the 8 tile lookups are not. The second loop only
    ///         catches walls carrying just one of the two components.
    /// </summary>
    private void BuildSignatureCaches()
    {
        foreach (var grid in _dirtySignatureGrids)
        {
            _thermalCache.Remove(grid);
            _radarCache.Remove(grid);
        }

        var thermalQuery = AllEntityQuery<KsThermalSourceComponent, TransformComponent>();
        while (thermalQuery.MoveNext(out var uid, out var source, out var xform))
        {
            if (xform.GridUid is not { } dirtyGrid || !_dirtySignatureGrids.Contains(dirtyGrid))
                continue;

            var exposed = CountExposedSides(xform, out var gridUid);
            if (exposed == 0)
                continue;

            _thermalCache[gridUid] = _thermalCache.GetValueOrDefault(gridUid) + source.Signature * exposed;

            if (_radarSourceQuery.TryGetComponent(uid, out var radarSource))
                _radarCache[gridUid] = _radarCache.GetValueOrDefault(gridUid) + radarSource.Signature * exposed;
        }

        var radarQuery = AllEntityQuery<KsRadarSourceComponent, TransformComponent>();
        while (radarQuery.MoveNext(out var uid, out var source, out var xform))
        {
            if (_thermalSourceQuery.HasComponent(uid))
                continue;

            if (xform.GridUid is not { } dirtyGrid || !_dirtySignatureGrids.Contains(dirtyGrid))
                continue;

            var exposed = CountExposedSides(xform, out var gridUid);
            if (exposed == 0)
                continue;

            _radarCache[gridUid] = _radarCache.GetValueOrDefault(gridUid) + source.Signature * exposed;
        }

        _dirtySignatureGrids.Clear();
    }

    /// <summary>
    ///     How many of a wall's eight surrounding tiles are open to space, the exposure
    ///         figure both crawlers scale their per-wall value by. Returns 0 (and a default
    ///         <paramref name="gridUid"/>) for a wall that is not an anchored part of a
    ///         grid's skin: a deconstructed or carried wall is not hull.
    /// </summary>
    private int CountExposedSides(TransformComponent xform, out EntityUid gridUid)
    {
        gridUid = default;

        if (!xform.Anchored || xform.GridUid is not { } grid)
            return 0;

        if (!_gridQuery.TryGetComponent(grid, out var gridComp))
            return 0;

        gridUid = grid;

        var indices = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);

        var exposed = 0;
        foreach (var offset in ExposureNeighbors)
        {
            // IsSpace is the blessed "faces vacuum" test (bare void and
            // lattice/scaffolding), the same one the external-mount check uses.
            if (_turf.IsSpace(_map.GetTileRef(grid, gridComp, indices + offset)))
                exposed++;
        }

        return exposed;
    }

    /// <summary>
    ///     Evaluates every declared intel readout against a detected grid. Returns null when
    ///         the sensor declares no intel at all, and skips ids that resolve to nothing: a
    ///         readout whose metric yields no value contributes its
    ///         <see cref="KsSensorIntelPrototype.NoneLabel"/> line, or is dropped entirely
    ///         when that is null.
    /// </summary>
    public Dictionary<ProtoId<KsSensorIntelPrototype>, string>? Evaluate(
        List<ProtoId<KsSensorIntelPrototype>> intel,
        EntityUid targetGrid,
        PhysicsComponent physics,
        MapGridComponent grid)
    {
        if (intel.Count == 0)
            return null;

        var result = new Dictionary<ProtoId<KsSensorIntelPrototype>, string>(intel.Count);

        foreach (var id in intel)
        {
            if (!_proto.TryIndex(id, out var proto))
            {
                if (_warnedUnknown.Add(id.Id))
                    _sawmill.Debug($"Unknown sensor intel id '{id.Id}', skipping.");

                continue;
            }

            var raw = ComputeMetric(proto.Metric, targetGrid, physics, grid);
            if (raw is not { } value)
            {
                // The metric is not applicable to this grid (e.g. TopSpeed on an
                // engineless hull).
                if (proto.NoneLabel is { } none)
                    result[id] = Loc.GetString(none);

                continue;
            }

            result[id] = Format(proto, value);
        }

        return result;
    }

    /// <summary>
    ///     Formats one already-measured value through its prototype's presentation rules
    ///         (scale, thresholds, value format), for readouts whose quantity does not come
    ///         from the target grid and so cannot go through <see cref="Evaluate"/>.
    ///     <see cref="KsSensorMetric.EmitterRange"/> is the case: ELINT measures the
    ///         EMITTING sensor, not the grid it is mounted on, so the grid-metric path
    ///         yields nothing; routing it here keeps the prototype's scale/format live
    ///         instead of half the YAML being silently inert.
    ///     Returns null when the sensor does not declare a readout for
    ///         <paramref name="metric"/>, so the YAML intel list still gates it.
    /// </summary>
    public (ProtoId<KsSensorIntelPrototype> Id, string Value)? FormatDeclaredMetric(
        List<ProtoId<KsSensorIntelPrototype>> declared,
        KsSensorMetric metric,
        float value)
    {
        foreach (var id in declared)
        {
            if (!_proto.TryIndex(id, out var proto) || proto.Metric != metric)
                continue;

            return (id, Format(proto, value));
        }

        return null;
    }

    /// <summary>
    ///     The raw physical quantity a metric measures, or null when it does not apply to
    ///         this grid. The only place a metric maps to server computation; presentation
    ///         (banding/formatting/units) is prototype data handled by <see cref="Format"/>.
    /// </summary>
    private float? ComputeMetric(KsSensorMetric metric, EntityUid targetGrid, PhysicsComponent physics, MapGridComponent grid)
    {
        switch (metric)
        {
            case KsSensorMetric.Mass:
                return physics.Mass;

            case KsSensorMetric.Heat:
                return GetThermalSignature(targetGrid);

            case KsSensorMetric.RadarCrossSection:
                return GetRadarSignature(targetGrid);

            case KsSensorMetric.Area:
                return grid.LocalAABB.Width * grid.LocalAABB.Height;

            case KsSensorMetric.TopSpeed:
                // A grid with no linear thrusters has no entry and yields no value
                // (the readout falls back to its NoneLabel).
                if (_thrustCacheTick != _timing.CurTick)
                {
                    _thrustCacheTick = _timing.CurTick;
                    _thrustCache.Clear();

                    var query = AllEntityQuery<ThrusterComponent, TransformComponent>();
                    while (query.MoveNext(out _, out var thruster, out var xform))
                    {
                        if (thruster.Type != ThrusterType.Linear)
                            continue;

                        if (xform.GridUid is not { } gridUid)
                            continue;

                        _thrustCache[gridUid] = _thrustCache.GetValueOrDefault(gridUid) + thruster.Thrust;
                    }
                }

                if (!_thrustCache.TryGetValue(targetGrid, out var totalThrust))
                    return null;

                return totalThrust / MathF.Max(1f, physics.Mass);

            default:
                return null;
        }
    }

    /// <summary>
    ///     Turns a raw metric value into its display string per the prototype's
    ///         presentation data. Thresholds (when present) classify the scaled
    ///         value into the first band it falls strictly under, with the last
    ///         band a catch-all. Otherwise <see cref="KsSensorIntelPrototype.ValueFormat"/>
    ///         formats the scaled+rounded number; a prototype with neither falls
    ///         back to the rounded integer as a bare string.
    /// </summary>
    private string Format(KsSensorIntelPrototype proto, float value)
    {
        value *= proto.Scale;

        if (proto.Thresholds.Count > 0)
        {
            foreach (var band in proto.Thresholds)
                if (value < band.Below)
                    return Loc.GetString(band.Label);

            // All finite bounds exceeded: a well-formed list ends in a catch-all
            // band (Below defaulting to +inf), so this is that catch-all.
            return Loc.GetString(proto.Thresholds[^1].Label);
        }

        if (proto.ValueFormat is { } fmt)
        {
            // Round 0 renders an integer ("100", not "100.0"); a positive Round
            // keeps that many decimals.
            if (proto.Round <= 0)
                return Loc.GetString(fmt, ("value", (int) MathF.Round(value)));

            return Loc.GetString(fmt, ("value", MathF.Round(value, proto.Round)));
        }

        // Misconfigured prototype (no thresholds, no value format): still show
        // something meaningful rather than an empty line.
        return ((int) MathF.Round(value)).ToString();
    }
}
