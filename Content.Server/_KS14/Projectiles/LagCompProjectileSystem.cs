using Content.Server.Movement.Components;
using Content.Server.Movement.Systems;
using Content.Shared._Trauma.Projectiles;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;
using Robust.Server.Player;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;
using System.Numerics;

namespace Content.Server._KS14.Projectiles;

/// <summary>
/// Compensates physical projectiles for network lag by spawning a temporary, invisible physics "ghost"
/// at each nearby <see cref="LagCompensationComponent"/> entity's rewound position - where they actually
/// were when the shooter's client fired. The ghost carries a copy of the target's hitbox and, via
/// <see cref="PreventCollideEvent"/>, only ever collides with the one projectile it was spawned for; when
/// real physics lands that collision, the hit is redirected onto the real target via
/// <see cref="PredictedProjectileSystem.DoHit"/>. Travel time, obstruction by walls, and hitting the
/// closest thing first all fall out of normal physics simulation instead of being reimplemented by hand.
/// Each ghost is re-rewound every tick so it stays a constant lag-duration behind its real target instead
/// of staying frozen at the position it was spawned at, keeping it accurate for the whole projectile flight.
/// Only covers physical projectiles; hitscan has its own path.
/// </summary>
public sealed partial class LagCompProjectileSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private PredictedProjectileSystem _projectile = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private FixtureSystem _fixtures = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    [Dependency] private EntityQuery<LagCompensationComponent> _lagCompensationQuery = default;
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default;

    private const string GhostFixtureId = "lag-compensation-ghost";

    // Generous upper bound on mob hitbox radii, padding the broadphase lookup so a target whose center sits
    // just past the max dodge distance isn't missed even though its hitbox still overlaps that point.
    private const float MaxHitboxRadius = 2.5f;

    // TODO LCDC: use movement speed instead
    // How far a mob could plausibly have moved during the lag window - this bounds the search, not the
    // projectile's own speed. A ghost is only ever worth spawning within dodging range of the muzzle;
    // real physics (travel time, obstruction) takes care of everything from there.
    // Base sprint speed (see MovementSpeedModifierComponent.DefaultBaseSprintSpeed); ignores speed buffs.
    private const float MaxCompensationSpeed = 5.5f;

    // Safety net in case a ghost is somehow never resolved by a collision or projectile cleanup.
    private const float GhostLifetime = 7f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerShotProjectileEvent>(OnShotProjectile);

        SubscribeLocalEvent<LagCompensationGhostComponent, PreventCollideEvent>(OnGhostPreventCollide);
        SubscribeLocalEvent<LagCompensationGhostComponent, StartCollideEvent>(OnGhostStartCollide);

        SubscribeLocalEvent<LagCompensatingProjectileComponent, PreventCollideEvent>(OnProjectilePreventCollide);
        SubscribeLocalEvent<LagCompensatingProjectileComponent, ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<LagCompensatingProjectileComponent, EntityTerminatingEvent>(OnProjectileTerminating);
    }

    private void OnShotProjectile(ref PlayerShotProjectileEvent args)
    {
        var projectileUid = args.Projectile;
        var shooterUid = args.User;

        if (!_lagCompensationQuery.HasComp(shooterUid))
            return;

        // Only a real player's shot needs compensating - NPCs/turrets simulate entirely server-side already,
        // so there's no client round trip to correct for. This also gives us that player's session, which is
        // the only server-trusted source of latency we have (see the CurTime comment below).
        if (!_playerManager.TryGetSessionByEntity(shooterUid, out var shooterSession))
            return;

        if (!_physicsQuery.TryComp(projectileUid, out var projectilePhysicsComponent) ||
            !_xformQuery.TryComp(projectileUid, out var projectileTransformComponent))
        {
            return;
        }

        var projectileSpeed = projectilePhysicsComponent.LinearVelocity.Length();
        if (projectileSpeed <= 0f)
            return;

        // IMPORTANT: a client-sent IGameTiming.CurTime is NOT comparable to the server's. RobustToolbox
        // deliberately runs the client's tick counter (and thus CurTime) ahead of the server's last-confirmed
        // tick, by a margin derived from that same client's ping, purely so predicted input arrives roughly
        // when the server needs it - it is not a synchronized wall clock. Diffing a client-reported CurTime
        // against server CurTime therefore doesn't measure this shot's actual one-way lag; it usually comes
        // out at or below zero for ordinary connections, which silently disabled compensation entirely.
        // Ping is the same (server-trusted, if coarse) estimate LagCompensationSystem's own melee/hitscan
        // rewind already relies on for exactly this reason.
        var lagDuration = TimeSpan.FromMilliseconds(shooterSession.Ping * 1.5); // Use 1.5 due to the trip buffer.
        if (lagDuration > LagCompensationSystem.BufferTime)
            lagDuration = LagCompensationSystem.BufferTime;

        var lagSeconds = (float)lagDuration.TotalSeconds;
        if (lagSeconds <= 0f)
            return;

        var projectileOrigin = _transform.GetMapCoordinates(projectileTransformComponent);
        if (projectileOrigin.MapId == MapId.Nullspace)
            return;

        // How far the target could plausibly have moved during the lag window - not how far the projectile
        // could have flown, since that's what actually determines how far a dodge could have carried them.
        var maxDodgeDistance = Math.Min(projectileSpeed, MaxCompensationSpeed) * lagSeconds;
        var projectileDirection = projectilePhysicsComponent.LinearVelocity / projectileSpeed;

        SpawnCompensationGhosts(projectileUid, shooterUid, projectileOrigin, projectileDirection, projectileSpeed, maxDodgeDistance, lagDuration);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = _timing.CurTime;
        var enumerator = EntityQueryEnumerator<LagCompensationGhostComponent, TransformComponent>();

        while (enumerator.MoveNext(out var ghostUid, out var ghostComponent, out var ghostTransformComponent))
        {
            UpdateGhostPosition((ghostUid, ghostComponent), ghostTransformComponent, currentTime);
        }
    }

    /// <summary>
    /// Re-rewinds a live ghost to stay <see cref="LagCompensationGhostComponent.LagDuration"/> behind its
    /// real target's recorded position, instead of leaving it frozen at its spawn-time snapshot. Keeps the
    /// ghost accurate across the projectile's whole flight, however long that takes.
    /// </summary>
    private void UpdateGhostPosition(Entity<LagCompensationGhostComponent> ghost, TransformComponent ghostTransformComponent, TimeSpan currentTime)
    {
        var targetUid = ghost.Comp.Target;

        if (TerminatingOrDeleted(targetUid) ||
            !_lagCompensationQuery.TryComp(targetUid, out var targetLagCompensationComponent) ||
            !_xformQuery.TryComp(targetUid, out var targetTransformComponent))
        {
            RemoveGhost(ghost.Comp.Projectile, ghost);
            return;
        }

        var rewoundTime = currentTime - ghost.Comp.LagDuration;
        var rewoundCoordinates = GetCompensatedCoordinates((targetUid, targetLagCompensationComponent, targetTransformComponent), rewoundTime);
        var rewoundMapCoordinates = _transform.ToMapCoordinates(rewoundCoordinates);

        // Target hopped maps since the ghost was spawned - leave the ghost where it last validly was rather
        // than snapping it across maps into whatever entity happens to occupy those coordinates there.
        if (rewoundMapCoordinates.MapId != ghostTransformComponent.MapID)
            return;

        _transform.SetCoordinates(ghost.Owner, ghostTransformComponent, rewoundCoordinates);
    }

    /// <summary>
    /// Spawns a rewound ghost for every <see cref="LagCompensationComponent"/> entity near the projectile's
    /// whole flight path - not just its muzzle point - that could plausibly have been dodging this shot, and
    /// registers each with the projectile so it's cleaned up once the shot resolves.
    /// </summary>
    private void SpawnCompensationGhosts(
        EntityUid projectileUid,
        EntityUid shooterUid,
        MapCoordinates projectileOrigin,
        Vector2 projectileDirection,
        float projectileSpeed,
        float maxDodgeDistance,
        TimeSpan lagDuration)
    {
        // Ghosts never outlive GhostLifetime, so a candidate further down the trajectory than the projectile
        // could travel in that time isn't worth spawning one for - its ghost would time out before the
        // projectile could ever reach it.
        var maxTravelDistance = projectileSpeed * GhostLifetime;
        var halfWidth = maxDodgeDistance + MaxHitboxRadius;

        // A rectangle hugging the projectile's entire flight path rather than a circle around its muzzle -
        // a dodging target could plausibly be anywhere near the trajectory line, not only near where the gun
        // was fired from. Box2Rotated rotates its Box's corners *around* Origin, so the box itself has to be
        // built in world space starting at the muzzle, not at local (0,0) - otherwise the whole rectangle
        // rotates into empty space nowhere near the projectile. Straight down the trajectory (+X after
        // rotation) starts exactly at the muzzle, so nothing behind the shooter is ever included.
        var muzzlePosition = projectileOrigin.Position;
        var localBounds = new Box2(
            muzzlePosition.X,
            muzzlePosition.Y - halfWidth,
            muzzlePosition.X + maxTravelDistance,
            muzzlePosition.Y + halfWidth);
        var searchBounds = new Box2Rotated(localBounds, projectileDirection.ToAngle(), muzzlePosition);

        // Dynamic/Static cover every mob hitbox; Sundries (non-collidable bodies), Sensors (non-hard
        // fixtures), and Contained (recursing into container contents) are all irrelevant for a hard mob
        // hitbox and just add search cost. Approximate skips the tighter per-fixture polygon check in favour
        // of broadphase AABBs, which is fine here since every downstream check re-validates precisely anyway.
        const LookupFlags candidateFlags = LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Approximate;

        var candidates = new HashSet<Entity<LagCompensationComponent>>();
        _lookup.GetEntitiesIntersecting(projectileOrigin.MapId, searchBounds, candidates, candidateFlags);

        var sentTime = _timing.CurTime - lagDuration;

        foreach (var candidate in candidates)
        {
            var targetUid = candidate.Owner;

            if (targetUid == shooterUid || targetUid == projectileUid)
                continue;

            if (!_xformQuery.TryComp(targetUid, out var targetTransformComponent) || targetTransformComponent.MapID != projectileOrigin.MapId)
                continue;

            if (!_fixturesQuery.TryComp(targetUid, out var targetFixturesComponent) ||
                FindHardFixture(targetFixturesComponent) is not { } targetFixture ||
                targetFixture.Shape is not PhysShapeCircle targetShape)
            {
                // Every mob hitbox in this game is a circle; skip anything unexpected rather than guess a shape.
                continue;
            }

            var rewoundCoordinates = GetCompensatedCoordinates((targetUid, candidate.Comp, targetTransformComponent), sentTime);
            var rewoundMapCoordinates = _transform.ToMapCoordinates(rewoundCoordinates);

            if (rewoundMapCoordinates.MapId != projectileOrigin.MapId)
                continue;

            SpawnGhost(projectileUid, targetUid, targetFixture, targetShape, rewoundCoordinates, lagDuration);
        }
    }

    /// <summary>
    /// Spawns an invisible physics proxy at <paramref name="coordinates"/> carrying a copy of
    /// <paramref name="targetShape"/>, and ties it to <paramref name="projectileUid"/>. Kept in sync with
    /// its real target every tick afterward by <see cref="UpdateGhostPosition"/> - this is just its initial
    /// position.
    /// </summary>
    private void SpawnGhost(EntityUid projectileUid, EntityUid targetUid, Fixture targetFixture, PhysShapeCircle targetShape, EntityCoordinates coordinates, TimeSpan lagDuration)
    {
        var ghostUid = Spawn(null, coordinates);

        var ghostTransformComponent = Transform(ghostUid);
        ghostTransformComponent.GridTraversal = false;

        var ghostPhysicsComponent = AddComp<PhysicsComponent>(ghostUid);
        var ghostFixturesComponent = EnsureComp<FixturesComponent>(ghostUid);

        // Not hard: a physical push-apart response is meaningless for a body that's deleted the instant it's
        // touched, and PredictedProjectileSystem's own OnStartCollide only processes hard fixtures - keeping
        // this soft means that generic handler leaves the ghost alone and OnGhostStartCollide is the only
        // thing that ever reacts to it.
        var ghostShape = new PhysShapeCircle(targetShape.Radius, targetShape.Position);
        _fixtures.TryCreateFixture(
            ghostUid,
            ghostShape,
            GhostFixtureId,
            hard: false,
            collisionLayer: targetFixture.CollisionLayer,
            collisionMask: targetFixture.CollisionMask,
            manager: ghostFixturesComponent,
            body: ghostPhysicsComponent);

        _physics.WakeBody(ghostUid, body: ghostPhysicsComponent);

        var ghostComponent = AddComp<LagCompensationGhostComponent>(ghostUid);
        ghostComponent.Projectile = projectileUid;
        ghostComponent.Target = targetUid;
        ghostComponent.LagDuration = lagDuration;

        var despawnComponent = EnsureComp<TimedDespawnComponent>(ghostUid);
        despawnComponent.Lifetime = GhostLifetime;

        var projectileComponent = EnsureComp<LagCompensatingProjectileComponent>(projectileUid);
        projectileComponent.Ghosts.Add(ghostUid);
        projectileComponent.IgnoredRealTargets.Add(targetUid);
    }

    /// <summary>
    /// Returns the coordinates an entity's <see cref="LagCompensationComponent"/> recorded closest to
    /// <paramref name="targetTime"/>, falling back to its current position if it has no history.
    /// </summary>
    private static EntityCoordinates GetCompensatedCoordinates(Entity<LagCompensationComponent, TransformComponent> entity, TimeSpan targetTime)
    {
        var (_, lagCompensationComponent, targetTransformComponent) = entity;

        if (lagCompensationComponent.Positions.Count == 0)
            return targetTransformComponent.Coordinates;

        var coordinates = targetTransformComponent.Coordinates;

        foreach (var (time, position, _) in lagCompensationComponent.Positions)
        {
            coordinates = position;

            if (time >= targetTime)
                break;
        }

        return coordinates;
    }

    private static Fixture? FindHardFixture(FixturesComponent fixturesComponent)
    {
        foreach (var fixture in fixturesComponent.Fixtures.Values)
        {
            if (fixture.Hard)
                return fixture;
        }

        return null;
    }

    /// <summary>
    /// Ghosts only ever collide with the one projectile they were spawned for.
    /// </summary>
    private void OnGhostPreventCollide(Entity<LagCompensationGhostComponent> ghost, ref PreventCollideEvent args)
    {
        if (args.Cancelled)
            return;

        if (args.OtherEntity != ghost.Comp.Projectile)
            args.Cancelled = true;
    }

    /// <summary>
    /// A projectile never collides with the real entity behind a ghost it's already carrying - the ghost
    /// is the one deciding whether that target gets hit.
    /// </summary>
    private void OnProjectilePreventCollide(Entity<LagCompensatingProjectileComponent> projectile, ref PreventCollideEvent args)
    {
        if (args.Cancelled)
            return;

        if (projectile.Comp.IgnoredRealTargets.Contains(args.OtherEntity))
            args.Cancelled = true;
    }

    /// <summary>
    /// The ghost caught the projectile: resolve this candidate (win or lose) and, if it wins, redirect
    /// the hit onto the real target.
    /// </summary>
    private void OnGhostStartCollide(Entity<LagCompensationGhostComponent> ghost, ref StartCollideEvent args)
    {
        if (args.OtherEntity != ghost.Comp.Projectile || args.OtherFixtureId != SharedProjectileSystem.ProjectileFixture)
            return;

        var projectileUid = ghost.Comp.Projectile;
        var targetUid = ghost.Comp.Target;

        RemoveGhost(projectileUid, ghost);

        if (TerminatingOrDeleted(targetUid) || TerminatingOrDeleted(projectileUid) || !CanReallyCollide(projectileUid, targetUid))
            return;

        _projectile.DoHit(projectileUid, targetUid);
    }

    /// <summary>
    /// The shot resolved - whether against a ghost or a real, un-ghosted target - so every remaining
    /// ghost from this shot is stale.
    /// </summary>
    private void OnProjectileHit(Entity<LagCompensatingProjectileComponent> projectile, ref ProjectileHitEvent args)
    {
        CleanupGhosts(projectile);
    }

    private void OnProjectileTerminating(Entity<LagCompensatingProjectileComponent> projectile, ref EntityTerminatingEvent args)
    {
        CleanupGhosts(projectile);
    }

    /// <summary>
    /// Re-raises the same <see cref="PreventCollideEvent"/> the physics engine would for a direct
    /// projectile/target collision, since the ghost stood in for the target instead. Lets faction/dodge/
    /// require-target/etc. rules still apply to the redirected hit.
    /// </summary>
    private bool CanReallyCollide(EntityUid projectileUid, EntityUid targetUid)
    {
        if (!_physicsQuery.TryComp(projectileUid, out var projectilePhysicsComponent) ||
            !_fixturesQuery.TryComp(projectileUid, out var projectileFixturesComponent) ||
            !projectileFixturesComponent.Fixtures.TryGetValue(SharedProjectileSystem.ProjectileFixture, out var projectileFixture) ||
            !_physicsQuery.TryComp(targetUid, out var targetPhysicsComponent) ||
            !_fixturesQuery.TryComp(targetUid, out var targetFixturesComponent) ||
            FindHardFixture(targetFixturesComponent) is not { } targetFixture)
        {
            return false;
        }

        var preventCollideEvent = new PreventCollideEvent(projectileUid, targetUid, projectilePhysicsComponent, targetPhysicsComponent, projectileFixture, targetFixture);
        RaiseLocalEvent(projectileUid, ref preventCollideEvent);
        if (preventCollideEvent.Cancelled)
            return false;

        preventCollideEvent = new PreventCollideEvent(targetUid, projectileUid, targetPhysicsComponent, projectilePhysicsComponent, targetFixture, projectileFixture);
        RaiseLocalEvent(targetUid, ref preventCollideEvent);
        return !preventCollideEvent.Cancelled;
    }

    private void RemoveGhost(EntityUid projectileUid, Entity<LagCompensationGhostComponent> ghost)
    {
        if (TryComp<LagCompensatingProjectileComponent>(projectileUid, out var projectileComponent))
        {
            projectileComponent.Ghosts.Remove(ghost.Owner);
            projectileComponent.IgnoredRealTargets.Remove(ghost.Comp.Target);
        }

        QueueDel(ghost.Owner);
    }

    private void CleanupGhosts(Entity<LagCompensatingProjectileComponent> projectile)
    {
        foreach (var ghostUid in projectile.Comp.Ghosts)
        {
            QueueDel(ghostUid);
        }

        projectile.Comp.Ghosts.Clear();
        projectile.Comp.IgnoredRealTargets.Clear();
    }
}
