using System.Numerics;
using Content.Shared._KS14.Particles;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Client._KS14.Particles;

/// <summary>
///     Opt-in collision of particles with walls (<see cref="KsParticlesPrototype.Collision"/>). Each particle is a
///         point of some radius, each step's motion a segment in the map frame (a local particle's swept with its
///         emitter's own motion), and the walls the real shapes, in their grid's frame, of the hard fixtures anchored
///         near the emitter: convex polygons and circles grown by the radius, exactly at their faces, approximately
///         (by bevels) at their corners.
/// </summary>
public sealed partial class KsParticlesSystem
{
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;

    /// <summary>
    ///     The fixture layers a particle stops at: what a flying thing cannot pass (<see cref="CollisionGroup.FlyingMobMask"/>).
    ///         Walls and windows are <see cref="CollisionGroup.Impassable"/>, closed airlocks, windoors and firelocks
    ///         <see cref="CollisionGroup.HighImpassable"/>. Tables, machines, crates and mobs are low or have neither,
    ///         and particles fly over them.
    /// </summary>
    public const int ParticleCollisionMask = (int)(CollisionGroup.Impassable | CollisionGroup.HighImpassable);

    /// <summary>
    ///     Seconds of simulation between gathers of the walls about an emitter, so a door that opens or closes is
    ///         seen within this long. Particles leaving the gathered region force one sooner.
    /// </summary>
    public const float CollisionRefreshInterval = 0.5f;

    /// <summary>
    ///     How far past the particles' swept bounds walls are gathered, metres, so the next steps rarely need a gather.
    /// </summary>
    public const float CollisionRegionMargin = 1f;

    /// <summary>
    ///     How far outside a wall a particle is put after touching it, metres, so the next step starts clear of it.
    /// </summary>
    public const float CollisionSkin = 0.001f;

    /// <summary>
    ///     Bounces resolved per particle per step; one still moving into a wall after that stops at it.
    /// </summary>
    public const int MaxCollisionIterations = 3;

    /// <summary>
    ///     Below this outward speed after a bounce, m/s, a particle stops bouncing and slides, so one pushed into a
    ///         wall by gravity rests against it instead of jittering.
    /// </summary>
    public const float CollisionRestSpeed = 0.05f;

    /// <summary>
    ///     The widest an emitter's particles may spread, metres, and still collide.
    /// </summary>
    public const float MaxCollisionRegionSize = 32f;

    private List<Entity<MapGridComponent>> _collisionGrids = new();

    /// <summary>
    ///     Below this, a corner's bevel is all but one of its edges and is left out, as is a degenerate edge.
    /// </summary>
    private const float CollisionPlaneEpsilon = 1e-4f;

    /// <summary>
    ///     After a step has moved every particle, moves those whose motion crossed a wall back out of it.
    /// </summary>
    private void Collide(
        KsParticlesComponent particlesComponent,
        KsParticleEmitterComponent emitterComponent,
        KsParticlesPrototype prototype,
        KsParticleCollision collision,
        KsParticleCollisionCache cache,
        float dt)
    {
        if (emitterComponent.Alive == 0 || emitterComponent.MapId == MapId.Nullspace)
        {
            cache.HasPreviousMatrix = false;
            return;
        }

        // A local particle lives in the emitter's frame. Its segment starts where it was in the world, through the
        //      emitter's matrix as of the last step, and ends through this one: the emitter's own motion is swept, so
        //      an emitter carried into a wall does not drag its particles in with it. The result is mapped back.
        var local = prototype.Space == KsParticleSpace.Local;
        var matrix = local ? emitterComponent.Matrix : Matrix3x2.Identity;
        var previousMatrix = local && cache.HasPreviousMatrix ? cache.PreviousMatrix : matrix;
        cache.PreviousMatrix = matrix;
        cache.HasPreviousMatrix = local;

        var inverse = Matrix3x2.Identity;
        if (local && !Matrix3x2.Invert(matrix, out inverse))
            return;

        var alive = emitterComponent.Alive;
        if (cache.Starts.Length < alive)
        {
            var capacity = Math.Max(alive, cache.Starts.Length * 2);
            cache.Starts = new Vector2[capacity];
            cache.Ends = new Vector2[capacity];
        }

        var particles = emitterComponent.Particles;
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        for (var i = 0; i < alive; i++)
        {
            ref var particle = ref particles[i];
            var start = new Vector2(particle.PrevPosition.X, particle.PrevPosition.Y);
            var end = new Vector2(particle.Position.X, particle.Position.Y);
            if (local)
            {
                if (particle.Stuck)
                {
                    // Stuck to a wall, it stays on the wall in the world, not with the emitter as it moves.
                    var stuck = Vector2.Transform(end, previousMatrix);
                    var held = Vector2.Transform(stuck, inverse);
                    particle.Position = new Vector3(held, particle.Position.Z);
                    particle.PrevPosition = particle.Position;
                    start = stuck;
                    end = stuck;
                }
                else
                {
                    start = Vector2.Transform(start, previousMatrix);
                    end = Vector2.Transform(end, matrix);
                }
            }

            cache.Starts[i] = start;
            cache.Ends[i] = end;
            min = Vector2.Min(min, Vector2.Min(start, end));
            max = Vector2.Max(max, Vector2.Max(start, end));
        }

        var radius = MathF.Max(0f, collision.Radius * particlesComponent.Scale);
        var swept = new Box2(min, max);

        // Particles spread over more than a room or two (long-lived ones in world space, left behind) would make every
        //      gather walk a wing of the station: past this they fly free, as without collision.
        if (swept.Width > MaxCollisionRegionSize || swept.Height > MaxCollisionRegionSize)
            return;

        cache.SinceRefresh += dt;
        if (!cache.Valid ||
            cache.MapId != emitterComponent.MapId ||
            cache.SinceRefresh >= CollisionRefreshInterval ||
            cache.Radius != radius ||
            !cache.Region.Contains(swept) ||
            !UpdateColliderGrids(cache))
        {
            RefreshColliders(cache, emitterComponent.MapId, swept.Enlarged(CollisionRegionMargin), radius);
        }

        if (cache.ColliderCount == 0)
            return;

        for (var i = 0; i < emitterComponent.Alive; i++)
        {
            ref var particle = ref particles[i];
            if (particle.Stuck || CollideParticle(ref particle, cache, collision, cache.Starts[i], cache.Ends[i], local, matrix, inverse, dt))
                continue;

            Kill(emitterComponent, i);
            cache.Starts[i] = cache.Starts[emitterComponent.Alive];
            cache.Ends[i] = cache.Ends[emitterComponent.Alive];
            i--;
        }
    }

    /// <summary>
    ///     Takes each gathered grid's frame afresh. False if one is gone, so the walls must be gathered again.
    /// </summary>
    private bool UpdateColliderGrids(KsParticleCollisionCache cache)
    {
        for (var i = 0; i < cache.GridCount; i++)
        {
            ref var grid = ref cache.Grids[i];
            if (!EntityManager.TransformQuery.TryComp(grid.Uid, out var gridTransformComponent))
                return false;

            grid.LocalToWorld = TransformSystem.GetWorldMatrix(gridTransformComponent);
            if (!Matrix3x2.Invert(grid.LocalToWorld, out grid.WorldToLocal))
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Gathers the walls in <paramref name="region"/>: on every grid it touches, the hard fixtures on
    ///         <see cref="ParticleCollisionMask"/> of static bodies anchored there, as their shapes in the grid's frame.
    /// </summary>
    private void RefreshColliders(KsParticleCollisionCache cache, MapId mapId, Box2 region, float radius)
    {
        cache.Invalidate();
        cache.Valid = true;
        cache.MapId = mapId;
        cache.Region = region;
        cache.Radius = radius;
        cache.SinceRefresh = 0f;

        _collisionGrids.Clear();
        _mapSystem.FindGridsIntersecting(mapId, region, ref _collisionGrids, approx: true);

        foreach (var (gridUid, gridComponent) in _collisionGrids)
        {
            if (!EntityManager.TransformQuery.TryComp(gridUid, out var gridTransformComponent))
                continue;

            var localToWorld = TransformSystem.GetWorldMatrix(gridTransformComponent);
            if (!Matrix3x2.Invert(localToWorld, out var worldToLocal))
                continue;

            var colliderStart = cache.ColliderCount;
            var anchored = _mapSystem.GetLocalAnchoredEntities(gridUid, gridComponent, worldToLocal.TransformBox(region));
            while (anchored.MoveNext())
            {
                var anchoredUid = anchored.Current;

                // Cheapest first: most anchored things are pipes, cables and floor clutter with no body that counts.
                if (!_physicsQuery.TryComp(anchoredUid, out var physicsComponent) ||
                    !physicsComponent.CanCollide ||
                    physicsComponent.BodyType != BodyType.Static ||
                    !_fixturesQuery.TryComp(anchoredUid, out var fixturesComponent))
                    continue;

                // Anchored, so its parent is the grid: its local transform is its place in the grid's frame.
                var anchoredTransformComponent = Transform(anchoredUid);
                var shapeTransform = new Robust.Shared.Physics.Transform(anchoredTransformComponent.LocalPosition, anchoredTransformComponent.LocalRotation);
                foreach (var fixture in fixturesComponent.Fixtures.Values)
                {
                    if (!fixture.Hard || (fixture.CollisionLayer & ParticleCollisionMask) == 0)
                        continue;

                    AddCollider(cache, fixture.Shape, shapeTransform, radius);
                }
            }

            if (cache.ColliderCount == colliderStart)
                continue;

            if (cache.GridCount >= cache.Grids.Length)
                Array.Resize(ref cache.Grids, Math.Max(2, cache.Grids.Length * 2));

            cache.Grids[cache.GridCount++] = new KsParticleColliderGrid
            {
                Uid = gridUid,
                ColliderStart = colliderStart,
                ColliderCount = cache.ColliderCount - colliderStart,
                LocalToWorld = localToWorld,
                WorldToLocal = worldToLocal,
            };
        }
    }

    /// <summary>
    ///     One fixture's shape, in the grid's frame, grown by <paramref name="radius"/>. Its own vertices, not its
    ///         bounding box: a diagonal wall is its triangle, and the physics polygon skin is left out, so particles
    ///         meet the faces drawn, not a centimetre short of them. A shape that is neither a polygon nor a circle
    ///         (an edge or chain, not used for walls) falls back to its bounding box.
    /// </summary>
    private static void AddCollider(KsParticleCollisionCache cache, IPhysShape shape, in Robust.Shared.Physics.Transform shapeTransform, float radius)
    {
        if (shape is PhysShapeCircle circleShape)
        {
            var center = Robust.Shared.Physics.Transform.Mul(shapeTransform, circleShape.Position);
            var circleRadius = circleShape.Radius + radius;
            AddColliderEntry(cache, new KsParticleCollider
            {
                Bounds = Box2.CenteredAround(center, new Vector2(circleRadius * 2f)),
                Center = center,
                CircleRadius = circleRadius,
            });
            return;
        }

        var corners = cache.Corners;
        int count;
        switch (shape)
        {
            case PolygonShape polygonShape when polygonShape.VertexCount >= 3:
                count = polygonShape.VertexCount;
                if (corners.Length < count)
                    cache.Corners = corners = new Vector2[Math.Max(count, corners.Length * 2)];

                for (var i = 0; i < count; i++)
                    corners[i] = Robust.Shared.Physics.Transform.Mul(shapeTransform, polygonShape.Vertices[i]);
                break;

            case PhysShapeAabb aabbShape:
                count = 4;
                var bounds = aabbShape.LocalBounds;
                corners[0] = Robust.Shared.Physics.Transform.Mul(shapeTransform, bounds.BottomLeft);
                corners[1] = Robust.Shared.Physics.Transform.Mul(shapeTransform, bounds.BottomRight);
                corners[2] = Robust.Shared.Physics.Transform.Mul(shapeTransform, bounds.TopRight);
                corners[3] = Robust.Shared.Physics.Transform.Mul(shapeTransform, bounds.TopLeft);
                break;

            default:
                count = 4;
                var box = shape.ComputeAABB(shapeTransform, 0);
                corners[0] = box.BottomLeft;
                corners[1] = box.BottomRight;
                corners[2] = box.TopRight;
                corners[3] = box.TopLeft;
                break;
        }

        AddPolygon(cache, corners, count, radius);
    }

    /// <summary>
    ///     A convex polygon's planes, grown by <paramref name="radius"/>: one per edge, then one per corner between its
    ///         two edges' normals, which cuts the grown corner back to within about a fifth of the radius of round.
    /// </summary>
    private static void AddPolygon(KsParticleCollisionCache cache, Vector2[] corners, int count, float radius)
    {
        // Either winding: the normals point away from the polygon's middle.
        var area = 0f;
        for (var i = 0; i < count; i++)
        {
            var next = corners[(i + 1) % count];
            area += corners[i].X * next.Y - next.X * corners[i].Y;
        }

        var winding = area >= 0f ? 1f : -1f;
        var planeStart = cache.PlaneCount;
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        var firstEdge = -1;
        var previousNormal = Vector2.Zero;

        for (var i = 0; i < count; i++)
        {
            var corner = corners[i];
            var edge = corners[(i + 1) % count] - corner;
            min = Vector2.Min(min, corner);
            max = Vector2.Max(max, corner);
            if (edge.LengthSquared() < CollisionPlaneEpsilon * CollisionPlaneEpsilon)
                continue;

            var normal = Vector2.Normalize(new Vector2(edge.Y, -edge.X) * winding);

            // The bevel at this edge's first corner, between it and the edge before.
            if (firstEdge >= 0)
                AddBevel(cache, previousNormal, normal, corner, radius);
            else
                firstEdge = i;

            AddPlane(cache, normal, Vector2.Dot(normal, corner) + radius);
            previousNormal = normal;
        }

        if (cache.PlaneCount - planeStart < 3)
        {
            cache.PlaneCount = planeStart;
            return;
        }

        // And the bevel at the first edge's corner, closing the loop.
        AddBevel(cache, previousNormal, cache.Planes[planeStart].Normal, corners[firstEdge], radius);

        AddColliderEntry(cache, new KsParticleCollider
        {
            Bounds = new Box2(min, max).Enlarged(radius),
            PlaneStart = planeStart,
            PlaneCount = cache.PlaneCount - planeStart,
        });
    }

    private static void AddBevel(KsParticleCollisionCache cache, Vector2 previousNormal, Vector2 normal, Vector2 corner, float radius)
    {
        var sum = previousNormal + normal;
        if (radius <= 0f || sum.LengthSquared() < CollisionPlaneEpsilon || Vector2.Dot(previousNormal, normal) > 1f - CollisionPlaneEpsilon)
            return;

        var bevel = Vector2.Normalize(sum);
        AddPlane(cache, bevel, Vector2.Dot(bevel, corner) + radius);
    }

    private static void AddPlane(KsParticleCollisionCache cache, Vector2 normal, float offset)
    {
        if (cache.PlaneCount >= cache.Planes.Length)
            Array.Resize(ref cache.Planes, Math.Max(32, cache.Planes.Length * 2));

        cache.Planes[cache.PlaneCount++] = new KsParticleColliderPlane { Normal = normal, Offset = offset };
    }

    private static void AddColliderEntry(KsParticleCollisionCache cache, in KsParticleCollider collider)
    {
        if (cache.ColliderCount >= cache.Colliders.Length)
            Array.Resize(ref cache.Colliders, Math.Max(16, cache.Colliders.Length * 2));

        cache.Colliders[cache.ColliderCount++] = collider;
    }

    /// <summary>
    ///     Resolves one particle's motion from <paramref name="start"/> to <paramref name="end"/> (map frame) against
    ///         the walls, writing back its position and velocity if anything was hit. False if it dies.
    /// </summary>
    private static bool CollideParticle(
        ref KsParticle particle,
        KsParticleCollisionCache cache,
        KsParticleCollision collision,
        Vector2 start,
        Vector2 end,
        bool local,
        in Matrix3x2 matrix,
        in Matrix3x2 inverse,
        float dt)
    {
        var velocity = new Vector2(particle.Velocity.X, particle.Velocity.Y);
        if (local)
            velocity = Vector2.TransformNormal(velocity, matrix);

        var displacement = end - start;
        var touched = false;
        Vector2? pushedFrom = null;

        var hit = TryRaycast(cache, start, displacement, out var fraction, out var normal, out var inside);

        // Already inside a wall: spawned in one, or a door closed on it. Out by the shortest way that is not into
        //      another, keeping only the motion away from the face.
        if (inside)
        {
            if (collision.OnHit == KsParticleHitResponse.Die || !TryPushOut(cache, start, out var pushed, out var pushNormal))
                return false;

            start = pushed;
            pushedFrom = pushed;
            touched = true;

            if (collision.OnHit == KsParticleHitResponse.Stick)
            {
                velocity = Vector2.Zero;
                displacement = Vector2.Zero;
                particle.Stuck = true;
            }
            else
            {
                var into = Vector2.Dot(displacement, pushNormal);
                if (into < 0f)
                    displacement -= into * pushNormal;

                velocity = Respond(velocity, pushNormal, collision, CollisionRestSpeed);
            }

            hit = TryRaycast(cache, start, displacement, out fraction, out normal, out inside);
            if (inside)
                return false;
        }

        var final = start + displacement;
        for (var iteration = 0; hit; iteration++)
        {
            touched = true;
            var hitPoint = start + displacement * fraction + normal * CollisionSkin;
            final = hitPoint;

            if (collision.OnHit == KsParticleHitResponse.Die)
                return false;

            if (collision.OnHit == KsParticleHitResponse.Stick)
            {
                velocity = Vector2.Zero;
                particle.Stuck = true;
                break;
            }

            velocity = Respond(velocity, normal, collision, CollisionRestSpeed);
            displacement = Respond(displacement * (1f - fraction), normal, collision, CollisionRestSpeed * dt * (1f - fraction));
            start = hitPoint;

            // Out of bounces: it stops where it last touched, outside.
            if (iteration + 1 >= MaxCollisionIterations)
                break;

            hit = TryRaycast(cache, start, displacement, out fraction, out normal, out inside);
            if (inside)
                break;

            final = start + displacement;
        }

        if (!touched)
            return true;

        var position = local ? Vector2.Transform(final, inverse) : final;
        particle.Position = new Vector3(position, particle.Position.Z);
        if (local)
            velocity = Vector2.TransformNormal(velocity, inverse);
        particle.Velocity = new Vector3(velocity, particle.Velocity.Z);

        // Drawn from where it was put, not from inside the wall it was in.
        if (pushedFrom is { } from)
        {
            var previous = local ? Vector2.Transform(from, inverse) : from;
            particle.PrevPosition = new Vector3(previous, particle.PrevPosition.Z);
        }

        return true;
    }

    /// <summary>
    ///     A velocity or motion meeting a surface of outward <paramref name="normal"/>: the part into it reflected and
    ///         scaled by the bounce, or dropped below <paramref name="rest"/>; the part along it slowed by the friction.
    ///         Motion already leaving the surface is left alone.
    /// </summary>
    private static Vector2 Respond(Vector2 vector, Vector2 normal, KsParticleCollision collision, float rest)
    {
        var into = Vector2.Dot(vector, normal);
        if (into >= 0f)
            return vector;

        var tangent = (vector - into * normal) * (1f - Math.Clamp(collision.Friction, 0f, 1f));
        var outward = -into * Math.Clamp(collision.Bounce, 0f, 1f);
        if (outward < rest)
            outward = 0f;

        return tangent + outward * normal;
    }

    /// <summary>
    ///     The first wall the segment from <paramref name="start"/> by <paramref name="displacement"/> enters: the
    ///         fraction along it and the surface's outward normal in the map frame. A segment that starts inside a
    ///         wall sets <paramref name="inside"/> and hits nothing.
    /// </summary>
    private static bool TryRaycast(KsParticleCollisionCache cache, Vector2 start, Vector2 displacement, out float fraction, out Vector2 normal, out bool inside)
    {
        fraction = float.MaxValue;
        normal = Vector2.Zero;
        inside = false;
        var moving = displacement.LengthSquared() > 0f;

        var colliders = cache.Colliders;
        var grids = cache.Grids;
        for (var g = 0; g < cache.GridCount; g++)
        {
            ref var grid = ref grids[g];
            var localStart = Vector2.Transform(start, grid.WorldToLocal);
            var localDisplacement = Vector2.TransformNormal(displacement, grid.WorldToLocal);
            var localEnd = localStart + localDisplacement;
            var segmentMin = Vector2.Min(localStart, localEnd);
            var segmentMax = Vector2.Max(localStart, localEnd);

            var end = grid.ColliderStart + grid.ColliderCount;
            for (var c = grid.ColliderStart; c < end; c++)
            {
                ref var collider = ref colliders[c];
                var bounds = collider.Bounds;
                // Cheapest first: most walls are nowhere near this particle.
                if (bounds.Left > segmentMax.X || bounds.Right < segmentMin.X || bounds.Bottom > segmentMax.Y || bounds.Top < segmentMin.Y)
                    continue;

                if (SignedDistance(cache, collider, localStart) < 0f)
                {
                    inside = true;
                    return false;
                }

                if (!moving ||
                    !TrySegment(cache, collider, localStart, localDisplacement, out var colliderFraction, out var localNormal) ||
                    colliderFraction >= fraction)
                    continue;

                fraction = colliderFraction;
                normal = Vector2.TransformNormal(localNormal, grid.LocalToWorld);
            }
        }

        return fraction <= 1f;
    }

    /// <summary>
    ///     How far <paramref name="point"/> is outside the grown shape, negative inside. For a polygon, the largest
    ///         distance past any of its planes: exact at the faces, conservative at the bevelled corners.
    /// </summary>
    private static float SignedDistance(KsParticleCollisionCache cache, in KsParticleCollider collider, Vector2 point)
    {
        if (collider.PlaneCount == 0)
            return Vector2.Distance(point, collider.Center) - collider.CircleRadius;

        var distance = float.MinValue;
        var end = collider.PlaneStart + collider.PlaneCount;
        for (var p = collider.PlaneStart; p < end; p++)
        {
            ref var plane = ref cache.Planes[p];
            distance = MathF.Max(distance, Vector2.Dot(plane.Normal, point) - plane.Offset);
        }

        return distance;
    }

    /// <summary>
    ///     Where a segment from outside enters the grown shape: the fraction along it and the outward normal there.
    ///         A polygon is clipped against each of its planes (Cyrus-Beck); a circle solved directly.
    /// </summary>
    private static bool TrySegment(KsParticleCollisionCache cache, in KsParticleCollider collider, Vector2 start, Vector2 displacement, out float fraction, out Vector2 normal)
    {
        fraction = 0f;
        normal = Vector2.Zero;

        if (collider.PlaneCount == 0)
        {
            // |start + t d - c| = r: a t² + 2 b t + k = 0, entering at the smaller root.
            var offset = start - collider.Center;
            var a = displacement.LengthSquared();
            var b = Vector2.Dot(offset, displacement);
            var k = offset.LengthSquared() - collider.CircleRadius * collider.CircleRadius;
            var discriminant = b * b - a * k;
            if (a <= 0f || k < 0f || b >= 0f || discriminant < 0f)
                return false;

            var t = (-b - MathF.Sqrt(discriminant)) / a;
            if (t < 0f || t > 1f)
                return false;

            fraction = t;
            normal = Vector2.Normalize(offset + displacement * t);
            return true;
        }

        var enter = float.MinValue;
        var exit = float.MaxValue;
        var end = collider.PlaneStart + collider.PlaneCount;
        for (var p = collider.PlaneStart; p < end; p++)
        {
            ref var plane = ref cache.Planes[p];
            var outside = Vector2.Dot(plane.Normal, start) - plane.Offset;
            var rate = Vector2.Dot(plane.Normal, displacement);

            // Parallel to this side: outside it, it never gets in; inside it, this side does not limit it.
            if (MathF.Abs(rate) < 1e-9f)
            {
                if (outside >= 0f)
                    return false;

                continue;
            }

            var t = -outside / rate;
            if (rate < 0f)
            {
                // Moving in through this side.
                if (t > enter)
                {
                    enter = t;
                    normal = plane.Normal;
                }
            }
            else
            {
                exit = MathF.Min(exit, t);
            }

            if (enter > exit)
                return false;
        }

        // Entering from outside, within this step's segment.
        if (enter < 0f || enter > 1f || enter > exit)
            return false;

        fraction = enter;
        return true;
    }

    private static bool IsInsideAny(KsParticleCollisionCache cache, Vector2 point)
    {
        for (var g = 0; g < cache.GridCount; g++)
        {
            ref var grid = ref cache.Grids[g];
            var localPoint = Vector2.Transform(point, grid.WorldToLocal);
            var end = grid.ColliderStart + grid.ColliderCount;
            for (var c = grid.ColliderStart; c < end; c++)
            {
                ref var collider = ref cache.Colliders[c];
                if (collider.Bounds.Contains(localPoint) && SignedDistance(cache, collider, localPoint) < 0f)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     The nearest point just outside every wall that <paramref name="point"/> is inside, through one of their
    ///         sides, and that side's outward normal in the map frame. False if each way out lands in another wall:
    ///         the particle is buried.
    /// </summary>
    private static bool TryPushOut(KsParticleCollisionCache cache, Vector2 point, out Vector2 pushed, out Vector2 normal)
    {
        pushed = point;
        normal = Vector2.Zero;
        var best = float.MaxValue;

        for (var g = 0; g < cache.GridCount; g++)
        {
            ref var grid = ref cache.Grids[g];
            var localPoint = Vector2.Transform(point, grid.WorldToLocal);
            var end = grid.ColliderStart + grid.ColliderCount;
            for (var c = grid.ColliderStart; c < end; c++)
            {
                ref var collider = ref cache.Colliders[c];
                if (!collider.Bounds.Contains(localPoint) || SignedDistance(cache, collider, localPoint) >= 0f)
                    continue;

                if (collider.PlaneCount == 0)
                {
                    var offset = localPoint - collider.Center;
                    var length = offset.Length();
                    var outward = length > CollisionPlaneEpsilon ? offset / length : new Vector2(1f, 0f);
                    TryPushCandidate(cache, grid, collider.CircleRadius - length, localPoint, outward, ref best, ref pushed, ref normal);
                    continue;
                }

                var planeEnd = collider.PlaneStart + collider.PlaneCount;
                for (var p = collider.PlaneStart; p < planeEnd; p++)
                {
                    ref var plane = ref cache.Planes[p];
                    TryPushCandidate(cache, grid, plane.Offset - Vector2.Dot(plane.Normal, localPoint), localPoint, plane.Normal, ref best, ref pushed, ref normal);
                }
            }
        }

        return best < float.MaxValue;
    }

    private static void TryPushCandidate(
        KsParticleCollisionCache cache,
        in KsParticleColliderGrid grid,
        float distance,
        Vector2 localPoint,
        Vector2 localNormal,
        ref float best,
        ref Vector2 pushed,
        ref Vector2 normal)
    {
        if (distance < 0f || distance >= best)
            return;

        var candidate = Vector2.Transform(localPoint + localNormal * (distance + CollisionSkin), grid.LocalToWorld);
        if (IsInsideAny(cache, candidate))
            return;

        best = distance;
        pushed = candidate;
        normal = Vector2.TransformNormal(localNormal, grid.LocalToWorld);
    }
}
