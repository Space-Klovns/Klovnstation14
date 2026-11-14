using System.Numerics;
using Content.Shared._KS14.Particles;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Client._KS14.Particles;

/// <summary>
///     Opt-in collision of particles with walls (<see cref="KsParticlesPrototype.Collision"/>). Approximate on
///         purpose: each particle is a point of some radius, each step's motion is a segment in the map frame, and
///         the walls are the bounding boxes, in their grid's frame, of the hard fixtures anchored near the emitter.
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
            return;

        // A local particle lives in the emitter's frame: the segment is mapped through the emitter's matrix (as of
        //      this frame, for both ends, so the emitter's own motion is not swept) and the result mapped back.
        var local = prototype.Space == KsParticleSpace.Local;
        var matrix = local ? emitterComponent.Matrix : Matrix3x2.Identity;
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
                start = Vector2.Transform(start, matrix);
                end = Vector2.Transform(end, matrix);
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

        if (cache.BoxCount == 0)
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
    ///         <see cref="ParticleCollisionMask"/> of static bodies anchored there, as boxes in the grid's frame.
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

            var boxStart = cache.BoxCount;
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

                    if (cache.BoxCount >= cache.Boxes.Length)
                        Array.Resize(ref cache.Boxes, Math.Max(16, cache.Boxes.Length * 2));

                    cache.Boxes[cache.BoxCount++] = fixture.Shape.ComputeAABB(shapeTransform, 0).Enlarged(radius);
                }
            }

            if (cache.BoxCount == boxStart)
                continue;

            if (cache.GridCount >= cache.Grids.Length)
                Array.Resize(ref cache.Grids, Math.Max(2, cache.Grids.Length * 2));

            cache.Grids[cache.GridCount++] = new KsParticleColliderGrid
            {
                Uid = gridUid,
                BoxStart = boxStart,
                BoxCount = cache.BoxCount - boxStart,
                LocalToWorld = localToWorld,
                WorldToLocal = worldToLocal,
            };
        }
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

        // Already inside a wall: spawned in one, carried into one by the emitter, or a door closed on it. Out by the
        //      shortest way that is not into another, keeping only the motion away from the face.
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
    ///         fraction along it and the face's outward normal in the map frame. A segment that starts inside a wall
    ///         sets <paramref name="inside"/> and hits nothing.
    /// </summary>
    private static bool TryRaycast(KsParticleCollisionCache cache, Vector2 start, Vector2 displacement, out float fraction, out Vector2 normal, out bool inside)
    {
        fraction = float.MaxValue;
        normal = Vector2.Zero;
        inside = false;
        var moving = displacement.LengthSquared() > 0f;

        var boxes = cache.Boxes;
        var grids = cache.Grids;
        for (var g = 0; g < cache.GridCount; g++)
        {
            ref var grid = ref grids[g];
            var localStart = Vector2.Transform(start, grid.WorldToLocal);
            var localDisplacement = Vector2.TransformNormal(displacement, grid.WorldToLocal);
            var localEnd = localStart + localDisplacement;
            var segmentMin = Vector2.Min(localStart, localEnd);
            var segmentMax = Vector2.Max(localStart, localEnd);

            var end = grid.BoxStart + grid.BoxCount;
            for (var b = grid.BoxStart; b < end; b++)
            {
                ref var box = ref boxes[b];
                // Cheapest first: most boxes are nowhere near this particle.
                if (box.Left > segmentMax.X || box.Right < segmentMin.X || box.Bottom > segmentMax.Y || box.Top < segmentMin.Y)
                    continue;

                if (localStart.X > box.Left && localStart.X < box.Right && localStart.Y > box.Bottom && localStart.Y < box.Top)
                {
                    inside = true;
                    return false;
                }

                if (!moving ||
                    !TrySegmentBox(box, localStart, localDisplacement, out var boxFraction, out var localNormal) ||
                    boxFraction >= fraction)
                    continue;

                fraction = boxFraction;
                normal = Vector2.TransformNormal(localNormal, grid.LocalToWorld);
            }
        }

        return fraction <= 1f;
    }

    /// <summary>
    ///     Slab test of a segment against an axis-aligned box: the fraction at which it enters, from outside, and the
    ///         entered face's outward normal.
    /// </summary>
    private static bool TrySegmentBox(in Box2 box, Vector2 start, Vector2 displacement, out float fraction, out Vector2 normal)
    {
        fraction = 0f;
        normal = Vector2.Zero;

        var enter = float.MinValue;
        var exit = float.MaxValue;
        var enterNormal = Vector2.Zero;

        // X slab. Parallel and outside it misses; the skin keeps a particle sliding along a face just outside it.
        if (MathF.Abs(displacement.X) < 1e-9f)
        {
            if (start.X <= box.Left || start.X >= box.Right)
                return false;
        }
        else
        {
            var inverse = 1f / displacement.X;
            var near = (box.Left - start.X) * inverse;
            var far = (box.Right - start.X) * inverse;
            var nearNormal = new Vector2(-1f, 0f);
            if (near > far)
            {
                (near, far) = (far, near);
                nearNormal = new Vector2(1f, 0f);
            }

            enter = near;
            exit = far;
            enterNormal = nearNormal;
        }

        if (MathF.Abs(displacement.Y) < 1e-9f)
        {
            if (start.Y <= box.Bottom || start.Y >= box.Top)
                return false;
        }
        else
        {
            var inverse = 1f / displacement.Y;
            var near = (box.Bottom - start.Y) * inverse;
            var far = (box.Top - start.Y) * inverse;
            var nearNormal = new Vector2(0f, -1f);
            if (near > far)
            {
                (near, far) = (far, near);
                nearNormal = new Vector2(0f, 1f);
            }

            if (near > enter)
            {
                enter = near;
                enterNormal = nearNormal;
            }

            exit = MathF.Min(exit, far);
        }

        // Entering from outside, within this step's segment.
        if (enter > exit || enter < 0f || enter > 1f)
            return false;

        fraction = enter;
        normal = enterNormal;
        return true;
    }

    private static bool IsInsideAny(KsParticleCollisionCache cache, Vector2 point)
    {
        for (var g = 0; g < cache.GridCount; g++)
        {
            ref var grid = ref cache.Grids[g];
            var localPoint = Vector2.Transform(point, grid.WorldToLocal);
            var end = grid.BoxStart + grid.BoxCount;
            for (var b = grid.BoxStart; b < end; b++)
            {
                if (cache.Boxes[b].ContainsOpen(localPoint))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     The nearest point just outside every box that <paramref name="point"/> is inside, through one of their
    ///         faces, and that face's outward normal in the map frame. False if each way out lands in another wall:
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
            var end = grid.BoxStart + grid.BoxCount;
            for (var b = grid.BoxStart; b < end; b++)
            {
                var box = cache.Boxes[b];
                if (!box.ContainsOpen(localPoint))
                    continue;

                for (var face = 0; face < 4; face++)
                {
                    var (distance, localNormal) = face switch
                    {
                        0 => (localPoint.X - box.Left, new Vector2(-1f, 0f)),
                        1 => (box.Right - localPoint.X, new Vector2(1f, 0f)),
                        2 => (localPoint.Y - box.Bottom, new Vector2(0f, -1f)),
                        _ => (box.Top - localPoint.Y, new Vector2(0f, 1f)),
                    };

                    if (distance >= best)
                        continue;

                    var candidate = Vector2.Transform(localPoint + localNormal * (distance + CollisionSkin), grid.LocalToWorld);
                    if (IsInsideAny(cache, candidate))
                        continue;

                    best = distance;
                    pushed = candidate;
                    normal = Vector2.TransformNormal(localNormal, grid.LocalToWorld);
                }
            }
        }

        return best < float.MaxValue;
    }
}
