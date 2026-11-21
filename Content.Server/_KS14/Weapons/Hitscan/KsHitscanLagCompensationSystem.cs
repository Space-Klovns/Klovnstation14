using System.Numerics;
using Content.Server._KS14.Projectiles;
using Content.Server.Movement.Components;
using Content.Server.Movement.Systems;
using Content.Shared._KS14.Weapons.Hitscan;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Weapons.Hitscan;

/// <summary>
///     Lag compensation for hitscan shots, as <see cref="LagCompProjectileSystem"/> is for bullets: a player's shot is
///         taken against where each mob was when that player's client fired, not where the server has it by the time
///         the shot arrives. Without it, a shot the shooter saw land on a moving target missed on the server, and the
///         damage their client predicted was put back.
/// </summary>
/// <remarks>
///     A hitscan shot is over in the tick it is fired, so nothing is spawned for it. The ray is cast as ever, and what
///         it crossed is corrected (<see cref="KsModifyHitscanRaycastResultsEvent"/>): every mob with a
///         <see cref="LagCompensationComponent"/> near the ray is taken out of the results where it stands now, and put
///         back in where its hitbox, at its rewound position, meets the ray. Walls and everything else stay as cast,
///         so what is nearest still wins, and a mob that was behind cover then is still behind it.
/// </remarks>
public sealed partial class KsHitscanLagCompensationSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private LagCompProjectileSystem _lagCompProjectileSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<LagCompensationComponent> _lagCompensationQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    /// <summary>
    ///     The mobs near a shot's ray: scratch for <see cref="Compensate"/>.
    /// </summary>
    private readonly HashSet<Entity<LagCompensationComponent>> _candidates = new();

    /// <summary>
    ///     The mobs whose place in a shot's results was decided by where they were: scratch for <see cref="Compensate"/>.
    /// </summary>
    private readonly HashSet<EntityUid> _compensated = new();

    /// <summary>
    ///     Used in place of the shooter's ping, when set: for integration tests, whose channels report a ping of zero, to
    ///         take a real shot through the whole path.
    /// </summary>
    [ViewVariables]
    public TimeSpan? LagDurationOverride;

    [SubscribeLocalEvent]
    private void OnModifyRaycastResults(ref KsModifyHitscanRaycastResultsEvent args)
    {
        // Only a player's shot: an NPC's or a turret's is fired on the server, against what the server has.
        if (!_playerManager.TryGetSessionByEntity(args.Shooter, out var shooterSession))
            return;

        // Ping, not a time the client sends: see LagCompProjectileSystem.OnShotProjectile.
        var lagDuration = LagDurationOverride ?? TimeSpan.FromMilliseconds(shooterSession.Ping * 1.5); // 1.5 for the trip buffer
        if (lagDuration > LagCompensationSystem.BufferTime)
            lagDuration = LagCompensationSystem.BufferTime;

        Compensate(ref args, lagDuration);
    }

    /// <summary>
    ///     Corrects the results of a ray as though its shooter fired it <paramref name="lagDuration"/> ago.
    ///         <see cref="OnModifyRaycastResults"/> gets that from the shooter's ping; an integration test, whose
    ///         channels report a ping of zero, passes its own.
    /// </summary>
    public void Compensate(ref KsModifyHitscanRaycastResultsEvent args, TimeSpan lagDuration)
    {
        var lagSeconds = (float)lagDuration.TotalSeconds;
        if (lagSeconds <= 0f || args.MapId == MapId.Nullspace)
            return;

        var results = args.Results;

        // A box along the whole ray, as wide as the fastest mob could have got from it in that time, hitbox included.
        var halfWidth = _lagCompProjectileSystem.GetMaxCompensationSpeed(args.MapId, args.Shooter) * lagSeconds + LagCompProjectileSystem.MaxHitboxRadius;
        var localBounds = new Box2(args.Origin.X, args.Origin.Y - halfWidth, args.Origin.X + args.MaxDistance, args.Origin.Y + halfWidth);
        var searchBounds = new Box2Rotated(localBounds, args.Direction.ToAngle(), args.Origin);

        _candidates.Clear();
        _entityLookupSystem.GetEntitiesIntersecting(args.MapId,
            searchBounds,
            _candidates,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Approximate);

        // And whatever the ray crossed where it stands now, however it got there.
        foreach (var result in results)
        {
            if (_lagCompensationQuery.TryComp(result.HitEntity, out var lagCompensationComponent))
                _candidates.Add((result.HitEntity, lagCompensationComponent));
        }

        var sentTime = _gameTiming.CurTime - lagDuration;
        _compensated.Clear();
        _rewoundHits.Clear();

        foreach (var candidate in _candidates)
        {
            var targetUid = candidate.Owner;
            if (targetUid == args.Shooter ||
                !_physicsQuery.TryComp(targetUid, out var physicsComponent) ||
                !physicsComponent.CanCollide ||
                _containerSystem.IsEntityInContainer(targetUid))
                continue;

            // Every mob's hitbox is a circle; anything else is left as the ray found it.
            if (GetHitbox(targetUid, args.CollisionMask) is not { } hitbox)
                continue;

            var transformComponent = Transform(targetUid);
            var rewoundCoordinates = LagCompProjectileSystem.GetCompensatedCoordinates((targetUid, candidate.Comp, transformComponent), sentTime);
            var rewoundMapCoordinates = _transformSystem.ToMapCoordinates(rewoundCoordinates);
            if (rewoundMapCoordinates.MapId != args.MapId)
                continue;

            _compensated.Add(targetUid);

            if (TryIntersectCircle(args.Origin, args.Direction, args.MaxDistance, rewoundMapCoordinates.Position + hitbox.Position, hitbox.Radius, out var distance))
                _rewoundHits.Add(new RayCastResults(distance, args.Origin + args.Direction * distance, targetUid));
        }

        // Where each of them stands now no longer counts: where it was does, or it was not on the ray.
        var removed = false;
        for (var i = results.Count - 1; i >= 0; i--)
        {
            if (!_compensated.Contains(results[i].HitEntity))
                continue;

            results.RemoveAt(i);
            removed = true;
        }

        if (removed || _rewoundHits.Count > 0)
        {
            results.AddRange(_rewoundHits);
            results.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        }

        _candidates.Clear();
        _compensated.Clear();
        _rewoundHits.Clear();
    }

    /// <summary>
    ///     Where the mobs of a shot were on its ray: scratch for <see cref="Compensate"/>.
    /// </summary>
    private readonly List<RayCastResults> _rewoundHits = new();

    /// <summary>
    ///     The hard circle fixture of <paramref name="uid"/> that a ray with <paramref name="collisionMask"/> collides with.
    /// </summary>
    private PhysShapeCircle? GetHitbox(EntityUid uid, int collisionMask)
    {
        if (!_fixturesQuery.TryComp(uid, out var fixturesComponent))
            return null;

        foreach (var fixture in fixturesComponent.Fixtures.Values)
        {
            if (fixture.Hard && (fixture.CollisionLayer & collisionMask) != 0 && fixture.Shape is PhysShapeCircle circle)
                return circle;
        }

        return null;
    }

    /// <summary>
    ///     Where a ray first meets a circle, if it does within <paramref name="maxDistance"/>: at once, from inside it.
    /// </summary>
    public static bool TryIntersectCircle(Vector2 origin, Vector2 direction, float maxDistance, Vector2 centre, float radius, out float distance)
    {
        distance = 0f;

        var toCentre = centre - origin;
        var along = Vector2.Dot(toCentre, direction);
        var offLineSquared = toCentre.LengthSquared() - along * along;
        var radiusSquared = radius * radius;
        if (offLineSquared > radiusSquared)
            return false;

        var halfChord = MathF.Sqrt(radiusSquared - offLineSquared);
        if (along + halfChord < 0f)
            return false; // wholly behind

        distance = MathF.Max(0f, along - halfChord);
        return distance <= maxDistance;
    }
}
