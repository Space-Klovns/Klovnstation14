using System.Numerics;
using Content.Shared._KS14.Sensors;
using Content.Shared._KS14.Sensors.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Sensors;

/// <summary>One active radar emission this tick: an emitting, operational radar.</summary>
/// <param name="ConeReach">ConeRangeFactor * MaxRange: how far the cone (and thus ELINT audibility) reaches.</param>
/// <param name="MaxRange">The radar's own detection range, reported by ELINT as EMITTER RANGE.</param>
/// <param name="BurnThroughFactor">This radar's jam resistance: it burns through a jammer of power P within P * this.</param>
/// <param name="Band">The set's frequency band, identification intel revealed by ELINT.</param>
/// <param name="Pattern">The set's emission pattern over time, alongside <paramref name="Band"/>.</param>
public readonly record struct KsRadarEmission(
    EntityUid Sensor,
    EntityUid Grid,
    MapId MapId,
    Vector2 Pos,
    float ConeReach,
    float MaxRange,
    float BurnThroughFactor,
    ProtoId<KsEmitterBandPrototype>? Band,
    KsEmissionPattern Pattern);

/// <summary>One active jamming emission this tick: an operational jammer.</summary>
/// <param name="Facing">Mount world rotation in radians (the pie slice's centre bearing).</param>
/// <param name="HalfAngle">Half the slice's angular width, in radians.</param>
/// <param name="Power">Jamming power: the slice reach AND the base of the burn-through range.</param>
/// <param name="Band">The jammer's frequency band, identification intel revealed by ELINT.</param>
/// <param name="Pattern">The jammer's emission pattern over time, alongside <paramref name="Band"/>.</param>
public readonly record struct KsJammerEmission(
    EntityUid Jammer,
    EntityUid Grid,
    MapId MapId,
    Vector2 Pos,
    double Facing,
    double HalfAngle,
    float Power,
    ProtoId<KsEmitterBandPrototype>? Band,
    KsEmissionPattern Pattern)
{
    /// <summary>
    ///     Whether <paramref name="point"/> lies inside this slice out to
    ///         <paramref name="reach"/> (range and arc, no line of sight). Used both to
    ///         jam a radar ship (reach = Power) and, with a shorter reach, for ELINT
    ///         to hear the jammer (reach = Power * (1 - IgnoreFraction)).
    /// </summary>
    public bool Contains(Vector2 point, float reach)
    {
        var delta = point - Pos;
        if (delta.LengthSquared() > reach * reach)
            return false;

        var d = Math.Atan2(delta.Y, delta.X) - Facing;
        while (d > Math.PI)
            d -= 2.0 * Math.PI;
        while (d < -Math.PI)
            d += 2.0 * Math.PI;

        return Math.Abs(d) <= HalfAngle;
    }
}

/// <summary>Which jammer is jamming a given radar, for its home-on-jam return.</summary>
public readonly record struct KsJamState(EntityUid Jammer, EntityUid JammerGrid);

/// <summary>
///     Active-emission side of the sensor framework: the radar/jammer emission registry,
///         jamming resolution and jammer coverage drawing.
///     <see cref="RebuildEmissions"/> runs at the very start of the tick, before any
///         sweep, so ELINT (<see cref="KsElintSystem"/>) can read the registry and
///         jamming can suppress a radar's detections the same tick.
/// </summary>
public sealed partial class KsSensorSystem
{
    private readonly List<KsRadarEmission> _radarEmissions = new();
    private readonly List<KsJammerEmission> _jammerEmissions = new();

    /// <summary>Grids running any active emitter (radar or jammer): ELINT on these is self-blinded.</summary>
    private readonly HashSet<EntityUid> _emittingGrids = new();

    /// <summary>Grids running an operational radar this tick. Their jammers are suppressed: radar wins the radar/jammer mutual-exclusion tie.</summary>
    private readonly HashSet<EntityUid> _radarEmittingGrids = new();

    /// <summary>Radars jammed this tick, and by whom (for the home-on-jam return).</summary>
    private readonly Dictionary<EntityUid, KsJamState> _jammedRadars = new();

    /// <summary>Radars jammed last tick, to detect the un-jammed -> jammed rising edge.</summary>
    private readonly HashSet<EntityUid> _wasJammedRadars = new();

    /// <summary>Radars that became jammed THIS tick: they emit one home-on-jam return, then go dark.</summary>
    private readonly HashSet<EntityUid> _newlyJammedRadars = new();

    /// <summary>Grids with at least one jammed radar: drives the console "JAMMED" indicator.</summary>
    private readonly HashSet<EntityUid> _jammedGrids = new();

    /// <summary>Last tick's <see cref="_jammedGrids"/>, so any jam-state change (the falling edge especially, which mutates no pool) forces a console push.</summary>
    private readonly HashSet<EntityUid> _prevJammedGrids = new();

    /// <summary>Last tick's <see cref="_emittingGrids"/>: the ESM deaf chip keys on this set, so its edges must force a push too.</summary>
    private readonly HashSet<EntityUid> _prevEmittingGrids = new();

    private GameTick _emissionsTick = GameTick.Zero;

    public IReadOnlyList<KsRadarEmission> RadarEmissions => _radarEmissions;
    public IReadOnlyList<KsJammerEmission> JammerEmissions => _jammerEmissions;

    /// <summary>Whether any active emitter (radar or jammer) sits on the grid, so its ELINT is deaf.</summary>
    public bool IsGridEmitting(EntityUid grid) => _emittingGrids.Contains(grid);

    /// <summary>Whether any radar on the grid is currently jammed (for the console indicator).</summary>
    public bool IsGridJammed(EntityUid grid) => _jammedGrids.Contains(grid);

    /// <summary>Whether this radar sensor's returns are currently suppressed by jamming.</summary>
    public bool IsRadarJammed(EntityUid sensor) => _jammedRadars.ContainsKey(sensor);

    /// <summary>
    ///     True on the single tick this radar transitions into jamming, handing back the
    ///         jammer it should home on. Used to emit exactly one home-on-jam return
    ///         before the radar goes dark.
    /// </summary>
    public bool TryGetNewlyJammed(EntityUid sensor, out KsJamState state)
    {
        if (_newlyJammedRadars.Contains(sensor))
            return _jammedRadars.TryGetValue(sensor, out state);

        state = default;
        return false;
    }

    /// <summary>
    ///     Rebuilds the emission registry and resolves jamming, once per tick before any
    ///         sweep. Called from <see cref="Update"/>.
    /// </summary>
    public void RebuildEmissions()
    {
        if (_emissionsTick == _timing.CurTick)
            return;

        _emissionsTick = _timing.CurTick;

        _radarEmissions.Clear();
        _jammerEmissions.Clear();
        _emittingGrids.Clear();
        _radarEmittingGrids.Clear();

        var radarQuery = EntityQueryEnumerator<KsRadarComponent, KsSensorComponent, TransformComponent>();
        while (radarQuery.MoveNext(out var uid, out var radar, out var sensor, out var xform))
        {
            if (xform.GridUid is not { } grid)
                continue;

            // On == emitting: an operational radar always emits.
            if (!IsSensorOperational((uid, sensor, xform)))
                continue;

            _radarEmissions.Add(new KsRadarEmission(
                uid,
                grid,
                xform.MapID,
                _transform.GetWorldPosition(xform),
                sensor.MaxRange * radar.ConeRangeFactor,
                sensor.MaxRange,
                radar.BurnThroughFactor,
                radar.Band,
                radar.Pattern));
            _emittingGrids.Add(grid);
            _radarEmittingGrids.Add(grid);
        }

        var jammerQuery = EntityQueryEnumerator<KsJammerComponent, TransformComponent>();
        while (jammerQuery.MoveNext(out var uid, out var jammer, out var xform))
        {
            if (xform.GridUid is not { } grid)
                continue;

            // Radar wins the mutual-exclusion tie: a grid running an operational radar cannot
            // also jam, so a jammer left enabled (via map/VV/admin, since the console toggles
            // flip one off when the other goes on) stays silent while the radar emits.
            if (_radarEmittingGrids.Contains(grid))
                continue;

            if (!IsJammerOperational((uid, jammer, xform)))
                continue;

            _jammerEmissions.Add(new KsJammerEmission(
                uid,
                grid,
                xform.MapID,
                _transform.GetWorldPosition(xform),
                _transform.GetWorldRotation(xform).Theta,
                jammer.HalfAngle * Math.PI / 180.0,
                jammer.JammingPower,
                jammer.Band,
                jammer.Pattern));
            _emittingGrids.Add(grid);
        }

        ResolveJamming();

        // The falling edge especially touches no contact pool, so nothing else would
        // clear the console's JAMMED indicator.
        if (!_jammedGrids.SetEquals(_prevJammedGrids))
        {
            _forceConsolePush = true;

            // Own-grid jam edges leak nothing: your radar already knows it is being
            // flooded (that is the JAMMED alarm).
            foreach (var grid in _jammedGrids)
            {
                if (!_prevJammedGrids.Contains(grid))
                    AppendEmissionLog(EnsureComp<KsSensorContactPoolComponent>(grid), KsEmissionLogKind.JamStart, designation: null, name: null);
            }

            foreach (var grid in _prevJammedGrids)
            {
                if (!_jammedGrids.Contains(grid) && !Deleted(grid))
                    AppendEmissionLog(EnsureComp<KsSensorContactPoolComponent>(grid), KsEmissionLogKind.JamEnd, designation: null, name: null);
            }
        }

        _prevJammedGrids.Clear();
        _prevJammedGrids.UnionWith(_jammedGrids);

        // The ESM deaf chip keys on the emitting set, and pushes are change-gated:
        // a power edge (a switched-on radar browning out) touches no pool, so
        // nothing else would move the chip.
        if (!_emittingGrids.SetEquals(_prevEmittingGrids))
        {
            _forceConsolePush = true;
            _prevEmittingGrids.Clear();
            _prevEmittingGrids.UnionWith(_emittingGrids);
        }
    }

    /// <summary>
    ///     Decides which radars are jammed this tick. A radar is jammed if its grid's
    ///         centre of mass sits inside any jammer's slice (range + arc, no line of
    ///         sight) AND outside that jammer's burn-through range
    ///         (<c>Power * radar.BurnThroughFactor</c>). One un-beaten jammer is enough
    ///         (jammed-if-any); to recover, a radar must leave the cone or push inside
    ///         burn-through of EVERY covering jammer. Faction-blind and does not jam a
    ///         radar on the jammer's own grid.
    /// </summary>
    private void ResolveJamming()
    {
        // Roll last tick's jammed set forward for rising-edge detection, then clear.
        _wasJammedRadars.Clear();
        foreach (var sensor in _jammedRadars.Keys)
            _wasJammedRadars.Add(sensor);

        _jammedRadars.Clear();
        _newlyJammedRadars.Clear();
        _jammedGrids.Clear();

        if (_jammerEmissions.Count == 0)
            return;

        foreach (var radar in _radarEmissions)
        {
            if (!_physicsQuery.TryGetComponent(radar.Grid, out var physics))
                continue;

            var (gridPos, gridRot) = _transform.GetWorldPositionRotation(radar.Grid);
            var com = gridPos + gridRot.RotateVec(physics.LocalCenter);

            foreach (var jammer in _jammerEmissions)
            {
                if (jammer.MapId != radar.MapId || jammer.Grid == radar.Grid)
                    continue;

                if (!jammer.Contains(com, jammer.Power))
                    continue;

                // Inside the burn-through radius this radar overpowers the jammer.
                var burnThrough = jammer.Power * radar.BurnThroughFactor;
                if ((com - jammer.Pos).Length() <= burnThrough)
                    continue;

                _jammedRadars[radar.Sensor] = new KsJamState(jammer.Jammer, jammer.Grid);
                _jammedGrids.Add(radar.Grid);
                if (!_wasJammedRadars.Contains(radar.Sensor))
                    _newlyJammedRadars.Add(radar.Sensor);

                break; // jammed-if-any: one un-beaten jammer suffices.
            }
        }
    }

    /// <summary>
    ///     The single home-on-jam return a radar emits the tick it becomes jammed: the
    ///         jammer itself, ignoring the radar's normal range and line of sight (the
    ///         jamming noise is the loudest thing in the sky). Obscured => Blip + no
    ///         name/intel; TypeOverride => the Jammer tier; QualityOverride => Bearing,
    ///         because a jammed set knows the noise DIRECTION, not the range. The detection
    ///         still carries the true position (the pool needs the truth for dedup, ghost
    ///         pruning and triangulation); only the client snapshot withholds it. An exact
    ///         fix on the jammer only ever comes from a sensor that resolves it in its own
    ///         right (e.g. radar burn-through against a big enough RCS).
    /// </summary>
    public KsSensorDetection BuildHomeOnJamDetection(KsJamState jam)
    {
        var bounds = new Box2();
        var center = Vector2.Zero;
        var isStatic = false;

        if (_gridQuery.TryGetComponent(jam.JammerGrid, out var grid))
            bounds = grid.LocalAABB;

        if (_physicsQuery.TryGetComponent(jam.JammerGrid, out var physics))
        {
            center = physics.LocalCenter;
            isStatic = physics.BodyType == BodyType.Static;
        }

        var (gridPos, gridRot) = _transform.GetWorldPositionRotation(jam.JammerGrid);
        var com = gridPos + gridRot.RotateVec(center);

        return new KsSensorDetection(
            jam.JammerGrid,
            com,
            gridRot,
            Vector2.Zero,
            isStatic,
            bounds,
            center,
            Name: null,
            Intel: null,
            Obscured: true,
            TypeOverride: KsSensorType.Jammer,
            QualityOverride: KsPositionQuality.Bearing);
    }

    /// <summary>
    ///     Whether the grid mounts any radar (Present) and whether any is switched on
    ///         (Active), for the console radar toggle's visibility and ON/OFF label.
    ///         Uses the switch (Enabled), not live emission, so the button reflects intent
    ///         even when the radar is currently unpowered or unmounted.
    /// </summary>
    public (bool Present, bool Active) GridRadarState(EntityUid grid)
    {
        var present = false;
        var active = false;

        var query = EntityQueryEnumerator<KsRadarComponent, KsSensorComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var sensor, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            present = true;
            if (sensor.Enabled)
            {
                active = true;
                break;
            }
        }

        return (present, active);
    }

    /// <summary>
    ///     Flips every radar on the grid on/off together (on == emitting): if any is on
    ///         they all go silent, otherwise they all light up.
    /// </summary>
    public void ToggleGridRadar(EntityUid grid)
    {
        var target = !GridRadarState(grid).Active;
        SetGridRadarsEnabled(grid, target);

        // Radar and jammer are mutually exclusive: going active silences the grid's jammers
        // so the ship never emits both at once (radar also wins the tie in RebuildEmissions).
        if (target)
            SetGridJammersEnabled(grid, false);

        _forceConsolePush = true;
    }

    private void SetGridRadarsEnabled(EntityUid grid, bool enabled)
    {
        var query = EntityQueryEnumerator<KsRadarComponent, KsSensorComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var sensor, out var xform))
        {
            if (xform.GridUid == grid)
                sensor.Enabled = enabled;
        }
    }

    private void SetGridJammersEnabled(EntityUid grid, bool enabled)
    {
        var query = EntityQueryEnumerator<KsJammerComponent, TransformComponent>();
        while (query.MoveNext(out _, out var jammer, out var xform))
        {
            if (xform.GridUid == grid)
                jammer.Enabled = enabled;
        }
    }

    /// <summary>
    ///     Whether an operational radar on this grid is currently silencing its jammers
    ///         (the radar-wins tie in <see cref="RebuildEmissions"/>). Reachable in
    ///         ordinary play, not just via map/VV: <see cref="KsSensorComponent.Enabled"/>
    ///         defaults true, so constructing a radar array on a jamming ship enters this
    ///         state. Every jammer readout mirrors it so the crew are never told they are
    ///         jamming while they are not.
    /// </summary>
    public bool IsGridJammerSuppressed(EntityUid grid) => _radarEmittingGrids.Contains(grid);

    /// <summary>
    ///     Whether the grid mounts any jammer (Present) and whether any is switched on
    ///         (Active), mirroring <see cref="GridRadarState"/>. Uses the switch (Enabled),
    ///         not live emission, so the button reflects intent even when the jammer is
    ///         currently unpowered or unmounted. The one exception is radar-wins
    ///         suppression: not a transient outage but the grid's own radar deliberately
    ///         overriding the switch, so reporting ON there would be a lie.
    /// </summary>
    public (bool Present, bool Active) GridJammerState(EntityUid grid)
    {
        var present = false;
        var active = false;
        var suppressed = IsGridJammerSuppressed(grid);

        var query = EntityQueryEnumerator<KsJammerComponent, TransformComponent>();
        while (query.MoveNext(out _, out var jammer, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            present = true;
            if (jammer.Enabled && !suppressed)
            {
                active = true;
                break;
            }
        }

        return (present, active);
    }

    /// <summary>Flips every jammer on the grid on/off together, mirroring <see cref="ToggleGridRadar"/>.</summary>
    public void ToggleGridJammer(EntityUid grid)
    {
        var target = !GridJammerState(grid).Active;
        SetGridJammersEnabled(grid, target);

        // Mutually exclusive with radar: going active silences the grid's radars.
        if (target)
            SetGridRadarsEnabled(grid, false);

        // Un-jamming an enemy radar flips _jammedGrids, which RebuildEmissions already
        // catches, but the jammer grid's own console still needs this when it carries no
        // radar of its own.
        _forceConsolePush = true;
    }

    /// <summary>
    ///     Whether the grid mounts any ELINT array. Presence only (a passive listener has
    ///         no crew-facing emit switch); gates the ESM tab's precision panel alongside
    ///         <see cref="GridHasRwr"/>. Mounted is enough, like
    ///         <see cref="GridRadarState"/>'s Present: an unpowered array still advertises
    ///         the capability the panel represents.
    /// </summary>
    public bool GridHasElint(EntityUid grid)
    {
        var query = EntityQueryEnumerator<KsElintComponent, KsSensorComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out _, out var xform))
        {
            if (xform.GridUid == grid)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether the grid mounts any radar warning receiver, mirroring
    ///         <see cref="GridHasElint"/>. Gates the ESM tab's warning panel.
    /// </summary>
    public bool GridHasRwr(EntityUid grid)
    {
        var query = EntityQueryEnumerator<KsRwrComponent, KsSensorComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out _, out var xform))
        {
            if (xform.GridUid == grid)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether a jammer is switched on, powered and (if required) externally mounted.
    ///         The emitter-only counterpart of <see cref="IsSensorOperational"/> (a jammer
    ///         is not a sensor).
    /// </summary>
    public bool IsJammerOperational(Entity<KsJammerComponent?, TransformComponent?> jammer)
    {
        if (!Resolve(jammer, ref jammer.Comp1, ref jammer.Comp2))
            return false;

        if (!jammer.Comp1.Enabled || !_power.IsPowered(jammer.Owner))
            return false;

        return !jammer.Comp1.RequireExternalMount || IsExternallyMounted(jammer.Owner, jammer.Comp2);
    }

    /// <summary>
    ///     Appends the operational jammer cones of <paramref name="gridUid"/> to
    ///         <paramref name="regions"/>, so the jammer's own console can aim its slice.
    ///         Unlike a sensor fan this is a plain directional pie slice with no occlusion:
    ///         jamming ignores line of sight, so the drawn cone does too. As with
    ///         <c>CollectSensorFans</c>, the transform and filing grid belong to the
    ///         CONSOLE, so a datalinked ally's slice (<paramref name="relayed"/>) still
    ///         arrives console-grid-local.
    /// </summary>
    private List<KsSensorRegionState>? BuildJammerRegions(EntityUid gridUid, NetEntity gridNet, Matrix3x2 invMatrix, bool relayed, List<KsSensorRegionState>? regions)
    {
        // A suppressed jammer emits nothing, so drawing its wedge would show the crew a
        // coverage area they do not have.
        if (IsGridJammerSuppressed(gridUid))
            return regions;

        var query = EntityQueryEnumerator<KsJammerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var jammer, out var xform))
        {
            if (xform.GridUid != gridUid || !jammer.ShowCoverage)
                continue;

            if (!IsJammerOperational((uid, jammer, xform)))
                continue;

            if (!_fanCache.TryGetValue(uid, out var fan))
            {
                fan = (BuildJammerCone((uid, jammer), xform), true);
                _fanCache[uid] = fan;
            }

            if (fan.World is not { Count: > 2 } world)
                continue;

            var local = new List<Vector2>(world.Count);
            foreach (var p in world)
                local.Add(Vector2.Transform(p, invMatrix));

            regions ??= new();
            regions.Add(new KsSensorRegionState
            {
                Grid = gridNet,
                Sensor = GetNetEntity(uid),
                Type = KsSensorType.Jammer,
                Emitting = true,
                Relayed = relayed,
                Points = local,
            });
        }

        return regions;
    }

    /// <summary>
    ///     A jammer's pie-slice cone in world space: apex at the mount, an arc of radius
    ///         <see cref="KsJammerComponent.JammingPower"/> spanning
    ///         [facing - HalfAngle, facing + HalfAngle] about the mount's rotation.
    ///     The apex is duplicated at index 0/1 and repeated at the end so the client's
    ///         boundary LineStrip (which skips index 0) traces apex -> arc -> apex, i.e.
    ///         both radial edges close the slice, while the triangle-fan fill still uses
    ///         index 0 as its centre.
    /// </summary>
    private List<Vector2>? BuildJammerCone(Entity<KsJammerComponent> jammer, TransformComponent xform)
    {
        if (xform.MapID == MapId.Nullspace)
            return null;

        var origin = _transform.GetWorldPosition(xform);
        var facing = _transform.GetWorldRotation(xform).Theta;
        var half = jammer.Comp.HalfAngle * Math.PI / 180.0;
        var reach = jammer.Comp.JammingPower;
        var rays = Math.Clamp(jammer.Comp.CoverageRays, 4, 360);

        // apex (fan centre) + apex (boundary start), then the arc, then apex (close).
        var points = new List<Vector2>(rays + 4) { origin, origin };

        for (var i = 0; i <= rays; i++)
        {
            var theta = facing - half + 2.0 * half * i / rays;
            var dir = new Vector2(MathF.Cos((float) theta), MathF.Sin((float) theta));
            points.Add(origin + dir * reach);
        }

        points.Add(origin);
        return points;
    }
}
