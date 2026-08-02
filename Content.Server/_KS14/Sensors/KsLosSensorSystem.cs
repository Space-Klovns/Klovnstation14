using System.Numerics;
using Content.Shared._KS14.Sensors;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._KS14.Sensors;

/// <summary>
///     Shared line-of-sight and coverage machinery for occlusion-aware sensors (visual
///         search, IRST). Detection is server-side "player vision": a target is only seen
///         if the sensor can trace an unobstructed occluder ray to some part of it, blocked
///         by the sensor's own hull and by any other grid in the way, the same occluders
///         that block a player's sight.
///     Concrete sensors subclass this and subscribe their own behavior marker to the
///         sweep/coverage/point-visible events, reusing the helpers here so the two never
///         drift apart. This base is abstract and never registered on its own.
/// </summary>
public abstract partial class KsLosSensorSystem : EntitySystem
{
    [Dependency] protected IMapManager MapManager = default!;
    [Dependency] protected SharedTransformSystem XformSystem = default!;
    [Dependency] protected OccluderSystem Occluder = default!;

    protected EntityQuery<PhysicsComponent> PhysicsQuery;
    protected EntityQuery<MapGridComponent> GridQuery;

    /// <summary>Reused scratch for the broadphase grid query (passed by ref, so not readonly).</summary>
    protected List<Entity<MapGridComponent>> Grids = new();

    /// <summary>Reused scratch for nearest-occluder ray casts (coverage fan).</summary>
    private readonly List<RayCastResults> _hits = new();

    /// <summary>
    ///     A ray is pulled in by this much before a target sample point so an occluder
    ///         sitting exactly on the target's own hull edge is never mistaken for a
    ///         blocker between us and the target.
    /// </summary>
    protected const float RayContact = 0.05f;

    /// <summary>Number of bounding-box points <see cref="GetAabbSamples"/> writes.</summary>
    protected const int AabbSampleCount = 9;

    public override void Initialize()
    {
        base.Initialize();

        PhysicsQuery = GetEntityQuery<PhysicsComponent>();
        GridQuery = GetEntityQuery<MapGridComponent>();
    }

    /// <summary>
    ///     The sensor's coverage fan in world space: the apex (sensor mount) followed by a
    ///         closed ring of boundary points, each ray pulled in to the nearest occluder so
    ///         the polygon hugs the real field of view.
    ///     <paramref name="ownGrid"/> and <paramref name="transparent"/> mirror detection:
    ///         pass a thermal sensor's transparent (too-cold-to-perceive) grid set and its
    ///         own grid so the fan bleeds through the same hulls the sweep does. Leave them
    ///         at their defaults for a plain geometric fan.
    ///     <paramref name="reach"/> overrides how far the fan reaches; leave null to use
    ///         <see cref="KsSensorComponent.MaxRange"/>. Radar passes a longer reach
    ///         (ConeRangeFactor times MaxRange) so its emitting cone extends past the range
    ///         it can actually resolve.
    ///     Public so tests can read a sensor's computed coverage directly.
    /// </summary>
    public List<Vector2>? ComputeCoverage(Entity<KsSensorComponent> sensor, EntityUid ownGrid = default, HashSet<EntityUid>? transparent = null, float? reach = null)
    {
        var xform = Transform(sensor);
        if (xform.MapID == MapId.Nullspace)
            return null;

        var mapId = xform.MapID;
        var origin = XformSystem.GetWorldPosition(xform);
        var range = reach ?? sensor.Comp.MaxRange;
        var rays = Math.Clamp(sensor.Comp.CoverageRays, 8, 720);

        // Apex first (for a triangle-fan fill), then a closed ring of boundary
        // points: i runs to rays inclusive so the last point rejoins the first.
        var points = new List<Vector2>(rays + 2) { origin };

        for (var i = 0; i <= rays; i++)
        {
            var theta = MathF.Tau * i / rays;
            var dir = new Vector2(MathF.Cos(theta), MathF.Sin(theta));
            points.Add(origin + dir * CastReach(mapId, origin, dir, range, ownGrid, transparent));
        }

        return points;
    }

    /// <summary>
    ///     The single-hit cast returns the first hit in traversal order, not the
    ///         nearest - but it terminates the walk where the list overload drags the
    ///         whole corridor. So: probe to bound the ray, then collect within that
    ///         bound and take the minimum (the nearest hit cannot lie past the probe's).
    ///         Bleeds through <paramref name="transparent"/> grids exactly as detection does.
    /// </summary>
    private float CastReach(MapId mapId, Vector2 origin, Vector2 dir, float range, EntityUid ownGrid = default, HashSet<EntityUid>? transparent = null)
    {
        var ray = new Ray(origin, dir);
        var state = new LosIgnoreState(EntityUid.Invalid, ownGrid, transparent);

        if (Occluder.IntersectRay(mapId, ray, range, state, LosIgnore) is not { } first)
            return range;

        _hits.Clear();
        Occluder.IntersectRay(_hits, mapId, ray, first.Distance, state, LosIgnore);

        var reach = first.Distance;
        foreach (var hit in _hits)
        {
            if (hit.Distance < reach)
                reach = hit.Distance;
        }

        return reach;
    }

    /// <summary>
    ///     The nine bounding-box sample points a detection ray targets: the centre, the
    ///         four corners, and the four edge midpoints. Enough that a hull poking any
    ///         part past cover is spotted without a ray per tile.
    /// </summary>
    protected static void GetAabbSamples(Box2 aabb, Span<Vector2> into)
    {
        var c = aabb.Center;
        into[0] = c;
        into[1] = aabb.BottomLeft;
        into[2] = aabb.BottomRight;
        into[3] = aabb.TopRight;
        into[4] = aabb.TopLeft;
        into[5] = new Vector2(c.X, aabb.Bottom);
        into[6] = new Vector2(c.X, aabb.Top);
        into[7] = new Vector2(aabb.Left, c.Y);
        into[8] = new Vector2(aabb.Right, c.Y);
    }

    /// <summary>
    ///     Whether the sensor has an unobstructed line of sight to any sampled point of the
    ///         target's bounding box (its <see cref="MapGridComponent.LocalAABB"/>, the same
    ///         rectangle the radar draws as the silhouette), within range.
    ///     Sampling the bounding box, not the individual hull tiles, keeps this cheap and
    ///         consistent with what the contact renders as; a concave grid can therefore be
    ///         spotted via an empty box corner, an accepted approximation until per-tile
    ///         silhouettes exist.
    ///     Occluders on the target grid itself are ignored so a ship never hides its own far
    ///         side from a detection its near side already earns.
    ///     <paramref name="ownGrid"/> and <paramref name="transparent"/> support a thermal
    ///         sensor: occluders on a grid it cannot perceive are skipped so the ray bleeds
    ///         through, except on the sensor's own grid, which always blocks. Leave them at
    ///         their defaults for plain geometric line of sight.
    /// </summary>
    protected bool IsAnyPartVisible(
        MapId mapId,
        Vector2 sensorPos,
        float range,
        EntityUid targetGrid,
        Box2 localAabb,
        Vector2 gridWorldPos,
        Angle gridWorldRot,
        EntityUid ownGrid = default,
        HashSet<EntityUid>? transparent = null)
    {
        Span<Vector2> samples = stackalloc Vector2[AabbSampleCount];
        GetAabbSamples(localAabb, samples);

        var rangeSq = range * range;

        foreach (var local in samples)
        {
            var world = gridWorldPos + gridWorldRot.RotateVec(local);

            if ((world - sensorPos).LengthSquared() > rangeSq)
                continue;

            if (HasLos(mapId, sensorPos, world, targetGrid, ownGrid, transparent))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     True when no blocking occluder lies strictly between the two points, using the
    ///         same occluders that block player vision. Occluders on
    ///         <paramref name="ignoreGrid"/> (the target) never block; for a thermal sensor,
    ///         nor do those on a grid in <paramref name="transparent"/> (the ray bleeds
    ///         through a hull too cold to perceive), save on <paramref name="ownGrid"/>.
    /// </summary>
    protected bool HasLos(MapId mapId, Vector2 from, Vector2 to, EntityUid ignoreGrid, EntityUid ownGrid = default, HashSet<EntityUid>? transparent = null)
    {
        return CastLos(mapId, from, to, ignoreGrid, ownGrid, transparent) == null;
    }

    /// <summary>
    ///     The blocking occluder hit between the two points, or null for a clear
    ///         line of sight. Same rules as <see cref="HasLos"/>.
    /// </summary>
    protected RayCastResults? CastLos(MapId mapId, Vector2 from, Vector2 to, EntityUid ignoreGrid, EntityUid ownGrid = default, HashSet<EntityUid>? transparent = null)
    {
        var delta = to - from;
        var dist = delta.Length();

        if (dist <= RayContact)
            return null;

        var ray = new Ray(from, delta / dist);
        var state = new LosIgnoreState(ignoreGrid, ownGrid, transparent);
        return Occluder.IntersectRay(mapId, ray, dist - RayContact, state, LosIgnore);
    }

    /// <summary>
    ///     Ray-cast ignore predicate (true = skip this occluder): disabled occluders (e.g.
    ///         open doors) never block; occluders on the target grid are skipped so a target
    ///         does not occlude itself; occluders on a thermally transparent grid (one the
    ///         sensor cannot perceive) are skipped so the ray bleeds through, except on the
    ///         sensor's own grid, which always blocks.
    /// </summary>
    private static bool LosIgnore(Entity<OccluderComponent, TransformComponent> occ, LosIgnoreState state)
    {
        if (!occ.Comp1.Enabled)
            return true;

        if (occ.Comp2.GridUid is not { } grid)
            return false;

        if (state.IgnoreGrid.IsValid() && grid == state.IgnoreGrid)
            return true;

        return state.Transparent != null && grid != state.OwnGrid && state.Transparent.Contains(grid);
    }

    /// <summary>State threaded through the occluder ray cast's ignore predicate.</summary>
    private readonly record struct LosIgnoreState(EntityUid IgnoreGrid, EntityUid OwnGrid, HashSet<EntityUid>? Transparent);
}
