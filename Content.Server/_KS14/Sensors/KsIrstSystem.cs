using System.Numerics;
using Content.Shared._KS14.Sensors;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics;

namespace Content.Server._KS14.Sensors;

/// <summary>
///     Answers sensor sweeps and coverage requests for <see cref="KsIrstComponent"/>.
///         Infrared search and track detects a grid by its thermal signature (the
///         summed heat of its exterior walls,
///         <see cref="KsSensorIntelSystem.GetThermalSignature"/>) rather than by
///         clean sight, so a hot ship is picked up far past visual range while a
///         cold one hides. Detection is still line-of-sight gated exactly like
///         visual search (own hull and other grids block it, reusing
///         <see cref="KsLosSensorSystem"/>), but the range a target can be seen at
///         scales with its signature (Model B, see <see cref="KsIrstComponent"/>).
///     A stock IRST contact resolves no name: anonymity is the
///         <see cref="KsSensorComponent.ProvidesName"/> data default rather than a
///         hard rule here, so an identifying IRST is a pure-YAML opt-in. Unlike
///         visual search, this system does NOT answer
///         <see cref="KsSensorPointVisibleEvent"/>: it cannot prove a memory ghost's
///         spot is empty, because a cold ship sitting there is invisible to it. IRST
///         ghosts persist as last-known intel until a sighting revives them, a visual
///         sensor confirms the spot empty, or the target grid is deleted.
/// </summary>
public sealed partial class KsIrstSystem : KsLosSensorSystem
{
    [Dependency] private KsSensorIntelSystem _intel = default!;

    /// <summary>
    ///     Scratch, rebuilt per sweep: grids too cold for this sensor to perceive.
    ///         Their hulls do not cast an IR shadow, so the detection ray bleeds
    ///         through them. The sensor's own grid is never added, so it still blocks.
    /// </summary>
    private readonly HashSet<EntityUid> _transparentGrids = new();

    /// <summary>Sweep scratch: detected grids, with what frames their life signs grid-locally.</summary>
    private readonly Dictionary<EntityUid, (Matrix3x2 InvMatrix, Vector2 LocalCenter)> _lifeSignFrames = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsIrstComponent, KsSensorSweepEvent>(OnSweep);
        SubscribeLocalEvent<KsIrstComponent, KsSensorCoverageEvent>(OnCoverage);
    }

    /// <summary>
    ///     Rebuilds <see cref="_transparentGrids"/> from the grids within <paramref name="maxRange"/>,
    ///         shared by the sweep and the coverage cone so the two can never disagree about which
    ///         cold hulls the ray bleeds through.
    /// </summary>
    private void BuildTransparentSet(MapId mapId, Vector2 sensorPos, float maxRange, EntityUid ownGrid, float minDetectable)
    {
        Grids.Clear();
        MapManager.FindGridsIntersecting(
            mapId,
            new Box2(sensorPos - new Vector2(maxRange), sensorPos + new Vector2(maxRange)),
            ref Grids,
            approx: true,
            includeMap: false);

        _transparentGrids.Clear();
        foreach (var grid in Grids)
        {
            if (grid.Owner != ownGrid && _intel.GetThermalSignature(grid.Owner) < minDetectable)
                _transparentGrids.Add(grid.Owner);
        }
    }

    /// <summary>
    ///     The signature a target actually needs for this array to perceive it at all:
    ///         the declared floor, or the signature at which the range taper bottoms out
    ///         if that bites first. Taking the declared floor alone would let a hull the
    ///         sweep can never detect go on casting an IR shadow over the targets behind it.
    /// </summary>
    private static float PerceptionFloor(KsIrstComponent comp, float maxRange)
    {
        return MathF.Max(comp.MinDetectable, comp.MinDetectableAtMaxRange - maxRange / comp.Factor);
    }

    private void OnSweep(Entity<KsIrstComponent> ent, ref KsSensorSweepEvent args)
    {
        var sensorXform = Transform(args.Sensor);
        if (sensorXform.MapID == MapId.Nullspace)
            return;

        var mapId = sensorXform.MapID;
        var sensorPos = XformSystem.GetWorldPosition(sensorXform);
        var maxRange = args.Sensor.Comp.MaxRange;
        var ownGrid = sensorXform.GridUid;

        // A target's per-signature effective range only ever shrinks inside this broadphase box.
        BuildTransparentSet(mapId, sensorPos, maxRange, ownGrid ?? default, PerceptionFloor(ent.Comp, maxRange));

        _lifeSignFrames.Clear();

        foreach (var grid in Grids)
        {
            var gridUid = grid.Owner;

            if (gridUid == ownGrid)
                continue;

            if (!PhysicsQuery.TryGetComponent(gridUid, out var physics))
                continue;

            // Mirrors SharedShuttleSystem.CanDraw / the visual sweep: junk debris
            // is never surfaced, so IRST stays consistent with the fog of war.
            if (physics.BodyType != BodyType.Static && physics.Mass < args.Sensor.Comp.MinTrackableMass)
                continue;

            if (!GridQuery.TryGetComponent(gridUid, out var gridComp))
                continue;

            // Model B: a target below the absolute sensitivity floor is never seen;
            // otherwise its effective detection range tapers from MaxRange down as
            // its signature falls below the value needed to be seen at MaxRange.
            var signature = _intel.GetThermalSignature(gridUid);
            if (signature < ent.Comp.MinDetectable)
                continue;

            var effRange = Math.Clamp(
                maxRange - ent.Comp.Factor * (ent.Comp.MinDetectableAtMaxRange - signature),
                0f,
                maxRange);

            if (effRange <= 0f)
                continue;

            var (worldPos, worldRot) = XformSystem.GetWorldPositionRotation(gridUid);

            // Same line-of-sight rule as visual search, but bounded by the thermal
            // effective range, and cold grids the sensor can't perceive don't block
            // (their hulls cast no IR shadow). The sensor's own hull still does.
            if (!IsAnyPartVisible(mapId, sensorPos, effRange, gridUid, gridComp.LocalAABB, worldPos, worldRot, ownGrid ?? default, _transparentGrids))
                continue;

            var center = worldPos + worldRot.RotateVec(physics.LocalCenter);

            // IRST is thermal: it reports what it detects regardless of IFF. The name
            // is carried so an IRST with providesName can reveal it; the stock sets keep
            // the default (off), so RunSweeps nulls it and the contact stays a nameless
            // heat blip.
            var intel = _intel.Evaluate(args.Sensor.Comp.Intel, gridUid, physics, gridComp);

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

            _lifeSignFrames[gridUid] = (XformSystem.GetInvWorldMatrix(gridUid), physics.LocalCenter);
        }

        CollectLifeSigns(mapId, sensorPos, maxRange, ownGrid ?? default, ref args);
    }

    /// <summary>
    ///     Aboard a detected grid the hull track is the resolution: no per-creature
    ///         ray, offsets grid-local so dots ride the dead-reckoned contact. A
    ///         free-floater is a point source: full range, no taper, same occluder LOS
    ///         as a grid. Cold hulls and junk grids conceal their riders.
    /// </summary>
    private void CollectLifeSigns(MapId mapId, Vector2 sensorPos, float maxRange, EntityUid ownGrid, ref KsSensorSweepEvent args)
    {
        var rangeSq = maxRange * maxRange;

        var query = AllEntityQuery<MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var mob, out var xform))
        {
            if (mob.CurrentState == MobState.Dead || xform.MapID != mapId)
                continue;

            if (xform.GridUid is { } mobGrid)
            {
                if (!_lifeSignFrames.TryGetValue(mobGrid, out var frame))
                    continue;

                var offset = Vector2.Transform(XformSystem.GetWorldPosition(xform), frame.InvMatrix) - frame.LocalCenter;
                args.LifeSigns ??= new();
                if (!args.LifeSigns.TryGetValue(mobGrid, out var offsets))
                    args.LifeSigns[mobGrid] = offsets = new();
                offsets.Add(offset);
                continue;
            }

            var worldPos = XformSystem.GetWorldPosition(xform);
            if ((worldPos - sensorPos).LengthSquared() > rangeSq)
                continue;

            // No target grid to ignore: a floater occludes nothing, not even itself.
            if (!HasLos(mapId, sensorPos, worldPos, default, ownGrid, _transparentGrids))
                continue;

            args.LifeSignFloaters ??= new();
            args.LifeSignFloaters.TryAdd(uid, worldPos);
        }
    }

    /// <summary>
    ///     Cosmetic: the coverage fan only feeds radar rendering, never detection.
    ///         It bleeds through the same imperceptible (below-<see cref="PerceptionFloor"/>)
    ///         grids the sweep does, or the cone would stop at an invisible cold hull
    ///         while the sweep still detects the hot target beyond it.
    /// </summary>
    private void OnCoverage(Entity<KsIrstComponent> ent, ref KsSensorCoverageEvent args)
    {
        var sensorXform = Transform(args.Sensor);
        if (sensorXform.MapID == MapId.Nullspace)
        {
            args.WorldPoints = null;
            return;
        }

        var ownGrid = sensorXform.GridUid ?? default;
        var maxRange = args.Sensor.Comp.MaxRange;
        var sensorPos = XformSystem.GetWorldPosition(sensorXform);

        BuildTransparentSet(sensorXform.MapID, sensorPos, maxRange, ownGrid, PerceptionFloor(ent.Comp, maxRange));

        args.WorldPoints = ComputeCoverage(args.Sensor, ownGrid, _transparentGrids);
    }
}
