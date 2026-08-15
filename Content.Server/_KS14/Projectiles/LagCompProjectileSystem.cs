using System.Numerics;
using Content.Server.Movement.Components;
using Content.Server.Movement.Systems;
using Content.Shared._Trauma.Projectiles;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Projectiles;

/// <summary>
/// Rewinds nearby <see cref="LagCompensationComponent"/> entities to where they were when the shooter's
/// client actually fired, and credits an immediate hit via <see cref="PredictedProjectileSystem.DoHit"/>
/// if the shot would have connected back then. Only covers physical projectiles; hitscan has its own path.
/// </summary>
public sealed partial class LagCompProjectileSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PredictedProjectileSystem _projectile = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private RayCastSystem _rayCastSystem = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    [Dependency] private EntityQuery<LagCompensationComponent> _lagCompensationQuery = default;
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default;

    // Generous upper bound on mob hitbox radii, padding the broadphase lookup so a target whose center sits
    // just past the catch-up distance isn't missed even though its hitbox still overlaps that point.
    private const float MaxHitboxRadius = 1f;

    // Caps the catch-up window to how far a mob could plausibly have dodged, not how far the projectile
    // could have flown. DoHit() resolves synchronously at spawn, so anything found within the window reads
    // as an instant hit - sizing it off a fast weapon's own speed would let it reach across half a room.
    // Base sprint speed (see MovementSpeedModifierComponent.DefaultBaseSprintSpeed); ignores speed buffs.
    private const float MaxCompensationSpeed = 5.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerShotProjectileEvent>(OnShotProjectile);
    }

    private void OnShotProjectile(ref PlayerShotProjectileEvent args)
    {
        if (args.ClientShootTime is not { } clientShootTime)
            return;

        if (!_lagCompensationQuery.HasComp(args.User))
            return;

        if (!_physicsQuery.TryComp(args.Projectile, out var projectileBody) ||
            !_fixturesQuery.TryComp(args.Projectile, out var projectileFixtures) ||
            !projectileFixtures.Fixtures.TryGetValue(SharedProjectileSystem.ProjectileFixture, out var projectileFixture) ||
            !_xformQuery.TryComp(args.Projectile, out var projectileXform))
        {
            return;
        }

        var speed = projectileBody.LinearVelocity.Length();
        if (speed <= 0f)
            return;

        // Clamp to how far LagCompensationComponent actually keeps history, and never rewind past "now".
        var curTime = _timing.CurTime;
        var earliestTime = curTime - LagCompensationSystem.BufferTime;
        var sentTicks = Math.Clamp(clientShootTime.Ticks, earliestTime.Ticks, curTime.Ticks);
        var sentTime = TimeSpan.FromTicks(sentTicks);

        var lagSeconds = (float)(curTime - sentTime).TotalSeconds;
        if (lagSeconds <= 0f)
            return;

        var origin = _transform.GetMapCoordinates(projectileXform);
        if (origin.MapId == MapId.Nullspace)
            return;

        var direction = projectileBody.LinearVelocity / speed;

        // How far the target could plausibly have moved during the lag window - not how far the projectile
        // could have flown, or a fast weapon would compensate hits far past where any dodge could reach.
        var catchUpDistance = Math.Min(speed, MaxCompensationSpeed) * lagSeconds;

        var target = TryFindCompensatedTarget(args.Projectile, args.User, projectileBody, projectileFixture,
            origin, direction, catchUpDistance, sentTime);

        if (target is { } targetUid)
            _projectile.DoHit(args.Projectile, targetUid);
    }

    /// <summary>
    /// Walks nearby lag-compensated entities back to <paramref name="sentTime"/> and returns the closest one
    /// (along the shot's path) whose rewound position was actually in the way, if any.
    /// </summary>
    private EntityUid? TryFindCompensatedTarget(
        EntityUid projectile,
        EntityUid shooter,
        PhysicsComponent projectileBody,
        Fixture projectileFixture,
        MapCoordinates origin,
        Vector2 direction,
        float catchUpDistance,
        TimeSpan sentTime)
    {
        var candidates = _lookup.GetEntitiesInRange<LagCompensationComponent>(origin, catchUpDistance + MaxHitboxRadius);

        EntityUid? bestUid = null;
        var bestAlong = float.MaxValue;

        foreach (var candidate in candidates)
        {
            var uid = candidate.Owner;

            if (uid == shooter || uid == projectile)
                continue;

            if (!_xformQuery.TryComp(uid, out var xform) || xform.MapID != origin.MapId)
                continue;

            if (!_fixturesQuery.TryComp(uid, out var fixtures) || FindHardFixture(fixtures) is not { } fixture)
                continue;

            var radius = fixture.Shape.Radius;

            // Already touching the muzzle right now: normal physics is about to hit them on its own, so
            // stepping in here too would credit the same shot twice.
            var currentMap = _transform.GetMapCoordinates(xform);
            if (currentMap.MapId == origin.MapId &&
                Vector2.DistanceSquared(currentMap.Position, origin.Position) <= radius * radius)
            {
                continue;
            }

            var rewoundCoordinates = GetCompensatedCoordinates((uid, candidate.Comp, xform), sentTime);
            var rewoundMap = _transform.ToMapCoordinates(rewoundCoordinates);

            if (rewoundMap.MapId != origin.MapId ||
                !TryGetPathHit(origin.Position, direction, catchUpDistance, rewoundMap.Position, radius, out var along) ||
                along >= bestAlong)
            {
                continue;
            }

            if (!_physicsQuery.TryComp(uid, out var body) ||
                !CanCollide(projectile, projectileBody, projectileFixture, uid, body, fixture) ||
                IsPathObstructed(origin.MapId, origin.Position, rewoundMap.Position, uid, projectileFixture.CollisionMask, projectile, shooter))
            {
                continue;
            }

            bestUid = uid;
            bestAlong = along;
        }

        return bestUid;
    }

    /// <summary>
    /// Returns the coordinates an entity's <see cref="LagCompensationComponent"/> recorded closest to
    /// <paramref name="targetTime"/>, falling back to its current position if it has no history.
    /// </summary>
    private static EntityCoordinates GetCompensatedCoordinates(Entity<LagCompensationComponent, TransformComponent> entity, TimeSpan targetTime)
    {
        var (_, lagComp, xform) = entity;

        if (lagComp.Positions.Count == 0)
            return xform.Coordinates;

        var coordinates = xform.Coordinates;

        foreach (var (time, position, _) in lagComp.Positions)
        {
            coordinates = position;

            if (time >= targetTime)
                break;
        }

        return coordinates;
    }

    private static Fixture? FindHardFixture(FixturesComponent fixtures)
    {
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Hard)
                return fixture;
        }

        return null;
    }

    /// <summary>
    /// Checks whether <paramref name="point"/> lies within <paramref name="radius"/> of the segment from
    /// <paramref name="origin"/> to <paramref name="origin"/> + <paramref name="direction"/> * <paramref name="maxDistance"/>.
    /// </summary>
    private static bool TryGetPathHit(Vector2 origin, Vector2 direction, float maxDistance, Vector2 point, float radius, out float along)
    {
        along = Math.Clamp(Vector2.Dot(point - origin, direction), 0f, maxDistance);
        var closest = origin + direction * along;
        return Vector2.DistanceSquared(point, closest) <= radius * radius;
    }

    /// <summary>
    /// Re-raises the same <see cref="PreventCollideEvent"/> the physics engine would, since we're forcing a
    /// hit without going through its normal contact pipeline. Lets faction/dodge/etc. rules still apply.
    /// </summary>
    private bool CanCollide(
        EntityUid projectile, PhysicsComponent projectileBody, Fixture projectileFixture,
        EntityUid target, PhysicsComponent targetBody, Fixture targetFixture)
    {
        var ev = new PreventCollideEvent(projectile, target, projectileBody, targetBody, projectileFixture, targetFixture);
        RaiseLocalEvent(projectile, ref ev);
        if (ev.Cancelled)
            return false;

        ev = new PreventCollideEvent(target, projectile, targetBody, projectileBody, targetFixture, projectileFixture);
        RaiseLocalEvent(target, ref ev);
        return !ev.Cancelled;
    }

    private bool IsPathObstructed(MapId mapId, Vector2 origin, Vector2 targetPosition, EntityUid target, int collisionMask, EntityUid projectile, EntityUid shooter)
    {
        var filter = new QueryFilter
        {
            MaskBits = collisionMask,
            IsIgnored = uid => uid == projectile || uid == shooter,
        };

        var result = _rayCastSystem.CastRayClosest(mapId, origin, targetPosition - origin, filter);
        return result.Hit && result.Results[0].Entity != target;
    }
}
