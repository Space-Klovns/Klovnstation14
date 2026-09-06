using System.Numerics;
using Content.Shared._KS14.Sensors;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics;

namespace Content.Server._KS14.Sensors;

/// <summary>
///     Answers sensor sweeps and coverage requests for <see cref="KsVisualSearchComponent"/>.
///         Detection is line-of-sight: a grid is only seen if the sensor can trace an
///         unobstructed ray to some part of it (any-part-visible), blocked by the sensor's
///         own hull and by any other grid in the way, the same occluders that block a
///         player's vision. The coverage fan is the drawable field of view, cut short in
///         every direction wherever the first occluder sits.
///     The line-of-sight and coverage machinery lives in <see cref="KsLosSensorSystem"/>,
///         shared with IRST; this system only decides what visual search sees.
/// </summary>
public sealed partial class KsVisualSearchSystem : KsLosSensorSystem
{
    [Dependency] private KsSensorIntelSystem _intel = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsVisualSearchComponent, KsSensorSweepEvent>(OnSweep);
        SubscribeLocalEvent<KsVisualSearchComponent, KsSensorCoverageEvent>(OnCoverage);
        SubscribeLocalEvent<KsVisualSearchComponent, KsSensorPointVisibleEvent>(OnPointVisible);
    }

    /// <summary>
    ///     Whether this sensor plainly sees a given world point right now: within range and
    ///         with an unobstructed line of sight, using the very same occluder ray it
    ///         detects with. Nothing is ignored, so the sensor's own hull and any other grid
    ///         can hide the point. Memory expiry uses this to confirm a ghost's last spot is
    ///         now empty (the target has left it).
    ///     A target the sweep would ignore as junk debris is never confirmed empty: a clear
    ///         view of a grid the sensor can't detect anyway is no evidence it left, so such
    ///         a ghost is left untouched.
    /// </summary>
    private void OnPointVisible(Entity<KsVisualSearchComponent> ent, ref KsSensorPointVisibleEvent args)
    {
        var xform = Transform(args.Sensor);
        if (xform.MapID == MapId.Nullspace || xform.MapID != args.MapId)
            return;

        // The sweep never tracks sub-threshold debris, so seeing "nothing" where such
        // a grid was proves nothing.
        if (args.TargetGrid.IsValid()
            && PhysicsQuery.TryGetComponent(args.TargetGrid, out var targetPhysics)
            && targetPhysics.BodyType != BodyType.Static
            && targetPhysics.Mass < args.Sensor.Comp.MinTrackableMass)
        {
            return;
        }

        var sensorPos = XformSystem.GetWorldPosition(xform);
        var range = args.Sensor.Comp.MaxRange;

        if ((args.WorldPos - sensorPos).LengthSquared() > range * range)
            return;

        args.Visible = HasLos(args.MapId, sensorPos, args.WorldPos, EntityUid.Invalid);
    }

    private void OnSweep(Entity<KsVisualSearchComponent> ent, ref KsSensorSweepEvent args)
    {
        var sensorXform = Transform(args.Sensor);
        if (sensorXform.MapID == MapId.Nullspace)
            return;

        var mapId = sensorXform.MapID;
        var sensorPos = XformSystem.GetWorldPosition(sensorXform);
        var range = args.Sensor.Comp.MaxRange;
        var ownGrid = sensorXform.GridUid;

        Grids.Clear();
        MapManager.FindGridsIntersecting(
            mapId,
            new Box2(sensorPos - new Vector2(range), sensorPos + new Vector2(range)),
            ref Grids,
            approx: true,
            includeMap: false);

        foreach (var grid in Grids)
        {
            var gridUid = grid.Owner;

            if (gridUid == ownGrid)
                continue;

            if (!PhysicsQuery.TryGetComponent(gridUid, out var physics))
                continue;

            // Mirrors SharedShuttleSystem.CanDraw.
            if (physics.BodyType != BodyType.Static && physics.Mass < args.Sensor.Comp.MinTrackableMass)
                continue;

            if (!GridQuery.TryGetComponent(gridUid, out var gridComp))
                continue;

            var (worldPos, worldRot) = XformSystem.GetWorldPositionRotation(gridUid);

            // A target fully behind our own hull or behind another grid is not
            // detected at all.
            if (!IsAnyPartVisible(mapId, sensorPos, range, gridUid, gridComp.LocalAABB, worldPos, worldRot))
                continue;

            var center = worldPos + worldRot.RotateVec(physics.LocalCenter);

            // An IFF-obscured (Hide) target is still spotted (full fog of war
            // means detection defeats stealth) but only as an anonymous blip:
            // no name, no intel, no clean silhouette.
            var obscured = TryComp<IFFComponent>(gridUid, out var iff)
                && (iff.Flags & IFFFlags.Hide) != 0x0;

            var intel = obscured ? null : _intel.Evaluate(args.Sensor.Comp.Intel, gridUid, physics, gridComp);

            args.Detections.Add(new KsSensorDetection(
                gridUid,
                center,
                worldRot,
                physics.LinearVelocity,
                physics.BodyType == BodyType.Static,
                gridComp.LocalAABB,
                physics.LocalCenter,
                obscured ? null : MetaData(gridUid).EntityName,
                intel,
                obscured));
        }
    }

    /// <summary>Purely cosmetic: the coverage fan feeds radar rendering, never detection.</summary>
    private void OnCoverage(Entity<KsVisualSearchComponent> ent, ref KsSensorCoverageEvent args)
    {
        args.WorldPoints = ComputeCoverage(args.Sensor);
    }
}
