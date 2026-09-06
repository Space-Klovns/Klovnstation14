using System.Numerics;
using Content.Shared._KS14.Sensors;
using Robust.Shared.Map;
using Robust.Shared.Physics;

namespace Content.Server._KS14.Sensors;

/// <summary>
///     Answers sensor sweeps and coverage requests for <see cref="KsRadarComponent"/>.
///         Active radar works like IRST (the same Model B curve, the same occluder-gated
///         line of sight WITH bleed-through) but keyed on a grid's radar cross-section
///         (<see cref="KsSensorIntelSystem.GetRadarSignature"/>) instead of its heat, and
///         as an ACTIVE sensor:
///     <list type="bullet">
///         <item>Its coverage cone reaches <see cref="KsRadarComponent.ConeRangeFactor"/>
///             times MaxRange, past the range it can resolve; the outer band is where
///             enemy ELINT hears it first (the cone carries <c>Emitting</c>).</item>
///         <item>A grid this set cannot perceive at all (see <see cref="PerceptionFloor"/>)
///             casts no radar shadow, so the detection ray passes through it and a bright
///             ship behind a stealthy one is still seen. RCS governs only a target's own
///             return strength, never whether it is cover. The sensor's own hull always blocks.</item>
///     </list>
///     Radar does NOT answer <see cref="KsSensorPointVisibleEvent"/>: a stealthy
///         (sub-<see cref="KsRadarComponent.MinDetectable"/>) ship on a ghost's last spot is
///         invisible to it, so a clear view there proves nothing. Radar ghosts persist until
///         a sighting revives them, a visual sensor confirms the spot empty, or the grid dies.
/// </summary>
public sealed partial class KsRadarSystem : KsLosSensorSystem
{
    [Dependency] private KsSensorIntelSystem _intel = default!;
    [Dependency] private KsSensorSystem _sensors = default!;

    /// <summary>
    ///     Scratch, rebuilt per sweep/cone: grids too radar-faint for this sensor to
    ///         perceive. Their hulls cast no radar shadow, so the ray bleeds through them
    ///         (a stealthy ship is no cover against radar). The sensor's own grid is never
    ///         added, so it still blocks.
    /// </summary>
    private readonly HashSet<EntityUid> _transparentGrids = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsRadarComponent, KsSensorSweepEvent>(OnSweep);
        SubscribeLocalEvent<KsRadarComponent, KsSensorCoverageEvent>(OnCoverage);
    }

    /// <summary>
    ///     The RCS a target actually needs for this set to perceive it at all: the
    ///         declared floor, or the RCS at which the range taper bottoms out if that
    ///         bites first. Taking the declared floor alone would let a hull the sweep
    ///         can never detect go on casting a radar shadow over the targets behind it.
    /// </summary>
    private static float PerceptionFloor(KsRadarComponent comp, float maxRange)
    {
        return MathF.Max(comp.MinDetectable, comp.MinDetectableAtMaxRange - maxRange / comp.Factor);
    }

    /// <summary>
    ///     Rebuilds <see cref="_transparentGrids"/> from the grids within <paramref name="range"/>
    ///         (RCS below the floor), shared by the sweep and the coverage cone so the two never
    ///         disagree about which faint hulls the ray bleeds through.
    /// </summary>
    private void BuildTransparentSet(MapId mapId, Vector2 sensorPos, float range, EntityUid ownGrid, float minDetectable)
    {
        Grids.Clear();
        MapManager.FindGridsIntersecting(
            mapId,
            new Box2(sensorPos - new Vector2(range), sensorPos + new Vector2(range)),
            ref Grids,
            approx: true,
            includeMap: false);

        _transparentGrids.Clear();
        foreach (var grid in Grids)
        {
            if (grid.Owner != ownGrid && _intel.GetRadarSignature(grid.Owner) < minDetectable)
                _transparentGrids.Add(grid.Owner);
        }
    }

    private void OnSweep(Entity<KsRadarComponent> ent, ref KsSensorSweepEvent args)
    {
        // A jammed radar's returns are suppressed: it produces no normal contacts.
        // On the tick it becomes jammed a homing set emits one return revealing the
        // jammer (magenta), then goes dark. Radar-exclusive tracks it was feeding
        // decay to ghosts via the normal expiry; anything IRST/visual/datalink also
        // holds stays live on those sources.
        // Checked before any sweep work: jamming is resolved in RebuildEmissions at
        // the top of the tick, so the broadphase, the RCS crawl and a fan of occluder
        // raycasts would be paid for only to discard the results.
        if (_sensors.IsRadarJammed(args.Sensor))
        {
            if (ent.Comp.HomeOnJam && _sensors.TryGetNewlyJammed(args.Sensor, out var jam))
                args.Detections.Add(_sensors.BuildHomeOnJamDetection(jam));
            return;
        }

        var sensorXform = Transform(args.Sensor);
        if (sensorXform.MapID == MapId.Nullspace)
            return;

        var mapId = sensorXform.MapID;
        var sensorPos = XformSystem.GetWorldPosition(sensorXform);
        var maxRange = args.Sensor.Comp.MaxRange;
        var ownGrid = sensorXform.GridUid;

        // A target's per-signature effective range only ever shrinks inside this broadphase box.
        BuildTransparentSet(mapId, sensorPos, maxRange, ownGrid ?? default, PerceptionFloor(ent.Comp, maxRange));

        foreach (var grid in Grids)
        {
            var gridUid = grid.Owner;

            if (gridUid == ownGrid)
                continue;

            if (!PhysicsQuery.TryGetComponent(gridUid, out var physics))
                continue;

            // Mirrors SharedShuttleSystem.CanDraw / the other sweeps: junk debris is
            // never surfaced, so radar stays consistent with the fog of war.
            if (physics.BodyType != BodyType.Static && physics.Mass < args.Sensor.Comp.MinTrackableMass)
                continue;

            if (!GridQuery.TryGetComponent(gridUid, out var gridComp))
                continue;

            // Model B on radar cross-section: a target below the RCS floor is never seen;
            // otherwise its effective range tapers from MaxRange as its RCS falls below the
            // value needed to be seen at MaxRange.
            var signature = _intel.GetRadarSignature(gridUid);
            if (signature < ent.Comp.MinDetectable)
                continue;

            var effRange = Math.Clamp(
                maxRange - ent.Comp.Factor * (ent.Comp.MinDetectableAtMaxRange - signature),
                0f,
                maxRange);

            if (effRange <= 0f)
                continue;

            var (worldPos, worldRot) = XformSystem.GetWorldPositionRotation(gridUid);

            // Same line-of-sight rule as IRST: bounded by the effective range, and a grid
            // the radar cannot perceive (below PerceptionFloor) does not block (its hull
            // casts no radar shadow). The sensor's own hull still does.
            if (!IsAnyPartVisible(mapId, sensorPos, effRange, gridUid, gridComp.LocalAABB, worldPos, worldRot, ownGrid ?? default, _transparentGrids))
                continue;

            var center = worldPos + worldRot.RotateVec(physics.LocalCenter);

            var intel = _intel.Evaluate(args.Sensor.Comp.Intel, gridUid, physics, gridComp);

            // The name is carried so a radar with providesName can reveal it; a radar
            // that does not (the default) has RunSweeps null it out.
            args.Detections.Add(new KsSensorDetection(
                gridUid,
                center,
                worldRot,
                physics.LinearVelocity,
                physics.BodyType == BodyType.Static,
                gridComp.LocalAABB,
                physics.LocalCenter,
                MetaData(gridUid).EntityName,
                intel));
        }
    }

    /// <summary>
    ///     The emitting cone: reaching <see cref="KsRadarComponent.ConeRangeFactor"/> times
    ///         MaxRange, and bleeding through the same faint grids the sweep does (built out
    ///         to the full cone reach, so a stealthy hull does not cut the drawn cone short
    ///         while the sweep sees past it). The band past MaxRange is
    ///         illuminated-but-not-resolved, where enemy ELINT hears it first.
    /// </summary>
    private void OnCoverage(Entity<KsRadarComponent> ent, ref KsSensorCoverageEvent args)
    {
        var sensorXform = Transform(args.Sensor);
        if (sensorXform.MapID == MapId.Nullspace)
        {
            args.WorldPoints = null;
            return;
        }

        var ownGrid = sensorXform.GridUid ?? default;
        var sensorPos = XformSystem.GetWorldPosition(sensorXform);
        var reach = args.Sensor.Comp.MaxRange * ent.Comp.ConeRangeFactor;

        BuildTransparentSet(sensorXform.MapID, sensorPos, reach, ownGrid, PerceptionFloor(ent.Comp, args.Sensor.Comp.MaxRange));

        // An operational radar is always emitting (on == emitting): the cone files
        // as an active emitter so it pulses as a "you are lit up" tell.
        args.Emitting = true;
        args.WorldPoints = ComputeCoverage(args.Sensor, ownGrid, _transparentGrids, reach: reach);
    }
}
