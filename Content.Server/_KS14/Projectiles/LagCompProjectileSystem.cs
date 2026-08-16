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

namespace Content.Server._KS14.Projectiles;

/// <summary>
/// Compensates physical projectiles for network lag by spawning a temporary, invisible physics "ghost"
/// at each nearby <see cref="LagCompensationComponent"/> entity's rewound position - where they actually
/// were when the shooter's client fired. The ghost carries a copy of the target's hitbox and, via
/// <see cref="PreventCollideEvent"/>, only ever collides with the one projectile it was spawned for; when
/// real physics lands that collision, the hit is redirected onto the real target via
/// <see cref="PredictedProjectileSystem.DoHit"/>. Travel time, obstruction by walls, and hitting the
/// closest thing first all fall out of normal physics simulation instead of being reimplemented by hand.
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
        var currentTime = _timing.CurTime;
        var lagDuration = TimeSpan.FromMilliseconds(shooterSession.Ping * 1.5); // Use 1.5 due to the trip buffer.
        if (lagDuration > LagCompensationSystem.BufferTime)
            lagDuration = LagCompensationSystem.BufferTime;

        var sentTime = currentTime - lagDuration;
        var lagSeconds = (float)lagDuration.TotalSeconds;
        if (lagSeconds <= 0f)
            return;

        var projectileOrigin = _transform.GetMapCoordinates(projectileTransformComponent);
        if (projectileOrigin.MapId == MapId.Nullspace)
            return;

        // How far the target could plausibly have moved during the lag window - not how far the projectile
        // could have flown, since that's what actually determines how far a dodge could have carried them.
        var maxDodgeDistance = Math.Min(projectileSpeed, MaxCompensationSpeed) * lagSeconds;

        SpawnCompensationGhosts(projectileUid, shooterUid, projectileOrigin, maxDodgeDistance, sentTime);
    }

    /// <summary>
    /// Spawns a rewound ghost for every nearby <see cref="LagCompensationComponent"/> entity that could
    /// plausibly have been dodging this shot, and registers each with the projectile so it's cleaned up
    /// once the shot resolves.
    /// </summary>
    private void SpawnCompensationGhosts(
        EntityUid projectileUid,
        EntityUid shooterUid,
        MapCoordinates projectileOrigin,
        float maxDodgeDistance,
        TimeSpan sentTime)
    {
        var candidates = _lookup.GetEntitiesInRange<LagCompensationComponent>(projectileOrigin, maxDodgeDistance + MaxHitboxRadius);

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

            SpawnGhost(projectileUid, targetUid, targetFixture, targetShape, rewoundCoordinates);
        }
    }

    /// <summary>
    /// Spawns a static, invisible physics proxy at <paramref name="coordinates"/> carrying a copy of
    /// <paramref name="targetShape"/>, and ties it to <paramref name="projectileUid"/>.
    /// </summary>
    private void SpawnGhost(EntityUid projectileUid, EntityUid targetUid, Fixture targetFixture, PhysShapeCircle targetShape, EntityCoordinates coordinates)
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
