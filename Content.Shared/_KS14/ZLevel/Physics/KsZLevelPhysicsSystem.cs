using System.Numerics;
using Content.Shared._KS14.CCVar;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.ZLevel.Physics;

/// <summary>
///     Vertical cross-z-level movement: entities transit up and down a z-level stack under gravity or their
///         own momentum, and land on whatever floor plane stops them.
///     Rising is not a separate code path — it is the same integration with a positive
///         <see cref="KsZLevelTransitComponent.VerticalVelocity"/>.
/// </summary>
public sealed partial class KsZLevelPhysicsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedGravitySystem _gravitySystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedMoverController _moverController = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<GravityComponent> _gravityQuery = default!;
    [Dependency] private EntityQuery<KsPendingZLevelTransitComponent> _pendingTransitQuery = default!;
    [Dependency] private EntityQuery<KsZLevelTransitComponent> _transitQuery = default!;
    [Dependency] private EntityQuery<MapComponent> _mapQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<KnockedDownComponent> _knockedDownQuery = default!;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    /// <summary>
    ///     Safety net only. Terminal velocity keeps a transit to a single floor plane crossing per tick at any
    ///         sane cvar values, so this loop normally runs exactly once.
    /// </summary>
    private const int MaxCrossingsPerTick = 8;

    /// <summary>
    ///     Damage dealt per z-level per second of impact speed over
    ///         <see cref="KsCCVars.ZLevelTransitImpactVelocity"/>. Ignores resistances.
    /// </summary>
    private static readonly DamageSpecifier ImpactDamage = new()
    {
        DamageDict = new()
        {
            { "Blunt", FixedPoint2.New(38) },
        },
    };

    /// <summary>
    ///     Damage dealt to whatever a solid entity lands on top of, per z-level per second of impact speed over
    ///         <see cref="KsCCVars.ZLevelTransitImpactVelocity"/>. Resistances apply.
    /// </summary>
    private static readonly DamageSpecifier CrushDamage = new()
    {
        DamageDict = new()
        {
            { "Blunt", FixedPoint2.New(65) },
        },
    };

    private readonly HashSet<Entity<FixturesComponent>> _crushTargets = [];

    /// <summary>
    ///     Entities whose eligibility to start transiting is checked on the next update.
    /// </summary>
    /// <remarks>
    ///     Every trigger that fills this fires from inside somebody else's transform or physics work, and
    ///         starting a transit moves the entity to another map. Doing that synchronously is reentrant
    ///         transform mutation, and at its worst it happens inside GridFixtureSystem's split - which creates
    ///         grid entities and reparents everything off the old grid while its own iteration and the
    ///         broadphase are still mid-flight. Editing a lot of tiles at once on a z-level grid is the ordinary
    ///         way to reach that, and it took the server down outright rather than throwing something catchable.
    /// </remarks>
    private readonly HashSet<EntityUid> _pendingTransitChecks = [];
    private readonly List<EntityUid> _drainedTransitChecks = [];

    private float _transitGravity;
    private TimeSpan _landingKnockdown;
    private TimeSpan _crushStun;
    private float _transitTerminalVelocity;
    private float _transitImpactVelocity;
    private float _landingFootstepVolume;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.ZLevelTransitGravity, value => _transitGravity = value, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelTransitTerminalVelocity, value => _transitTerminalVelocity = value, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelTransitImpactVelocity, value => _transitImpactVelocity = value, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelTransitLandingKnockdown, value => _landingKnockdown = TimeSpan.FromSeconds(value), true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelTransitCrushStun, value => _crushStun = TimeSpan.FromSeconds(value), true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelTransitLandingFootstepVolume, value => _landingFootstepVolume = value, true);
    }

    #region Triggers

    [SubscribeLocalEvent]
    private void OnWeightlessnessChanged(Entity<KsPendingZLevelTransitComponent> entity, ref WeightlessnessChangedEvent args)
    {
        if (args.Weightless)
            return;

        _pendingTransitChecks.Add(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnBodyStatusChanged(Entity<KsPendingZLevelTransitComponent> entity, ref PhysicsBodyStatusChangedEvent args)
    {
        if (args.NewStatus == BodyStatus.InAir)
            return;

        _pendingTransitChecks.Add(entity.Owner);
    }

    [SubscribeLocalEvent(after: [typeof(Shared.Movement.Systems.SharedJetpackSystem)])]
    private void OnPhysicsParentChanged(Entity<PhysicsComponent> entity, ref EntParentChangedMessage args)
    {
        if (_gameTiming.ApplyingState)
            return;

        // Crossing a floor plane re-parents the entity, which lands right back here. Without this the transit
        //      would restart — and so lose all of its speed — at every single z-level boundary.
        if (_transitQuery.HasComponent(entity.Owner))
            return;

        var transformComponent = args.Transform;
        if (entity.Comp.BodyStatus == BodyStatus.InAir ||
            _gravitySystem.IsWeightless(entity.Owner))
        {
            if (_zLevelSystem.TryGetZLevel((entity.Owner, transformComponent), out _))
                EnsureComp<KsPendingZLevelTransitComponent>(entity.Owner);
            else if (_pendingTransitQuery.TryGetComponent(entity.Owner, out var pendingTransitComponent))
                RemComp(entity.Owner, pendingTransitComponent);

            return;
        }

        _pendingTransitChecks.Add(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnPhysicsLand(Entity<PhysicsComponent> entity, ref LandEvent args)
    {
        _pendingTransitChecks.Add(entity.Owner);
    }

    /// <summary>
    ///     Being picked up mid-transit ends it, or the next crossing would teleport the entity straight out of
    ///         whatever is holding it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnInsertedIntoContainer(Entity<KsZLevelTransitComponent> entity, ref EntGotInsertedIntoContainerMessage args)
    {
        RemComp<KsZLevelTransitComponent>(entity.Owner);
    }

    #endregion

    #region Blocking

    [SubscribeLocalEvent]
    private void OnTransitStartup(Entity<KsZLevelTransitComponent> entity, ref ComponentStartup args)
    {
        _actionBlockerSystem.UpdateCanMove(entity.Owner);

        // Nobody stays on their feet through a fall. The duration only governs the sprawl after landing -
        //      OnTransitStandUpAttempt is what keeps the entity down for however long the transit itself lasts.
        // Anything already knocked down is left alone, so this cannot cut a stun short or quietly extend one.
        if (!_knockedDownQuery.HasComponent(entity.Owner))
            _stunSystem.TryKnockdown(entity.Owner, _landingKnockdown, refresh: false, autoStand: true, drop: false);

        var startedEvent = new KsZLevelTransitStartedEvent();
        RaiseLocalEvent(entity.Owner, ref startedEvent);
    }

    [SubscribeLocalEvent]
    private void OnTransitShutdown(Entity<KsZLevelTransitComponent> entity, ref ComponentShutdown args)
    {
        _actionBlockerSystem.UpdateCanMove(entity.Owner);

        var endedEvent = new KsZLevelTransitEndedEvent();
        RaiseLocalEvent(entity.Owner, ref endedEvent);
    }

    [SubscribeLocalEvent]
    private void OnTransitUpdateCanMove(Entity<KsZLevelTransitComponent> entity, ref UpdateCanMoveEvent args)
    {
        // OnTransitShutdown refreshes this while the component is technically still attached.
        if (entity.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        var attemptEvent = new KsZLevelTransitMoveAttemptEvent(true);
        RaiseLocalEvent(entity.Owner, ref attemptEvent);

        if (attemptEvent.Blocked)
            args.Cancel();
    }

    /// <summary>
    ///     Nothing gets to its feet mid-air, however long the fall lasts.
    /// </summary>
    /// <remarks>
    ///     What does most of the work here is actually the transit movement block: <see cref="SharedStunSystem"/>
    ///         gates standing on KnockdownOver, which consults ActionBlocker, so a transiting entity never even
    ///         reaches an attempt. This covers the rest of it, because the crawler branch of TryStanding is the
    ///         only one that raises this event - the branch for everything else drops the knockdown outright.
    ///     So: cancelling here stops a crawler getting up mid-fall even if something has deliberately overridden
    ///         the movement block via <see cref="KsZLevelTransitMoveAttemptEvent"/>.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnTransitStandUpAttempt(Entity<KsZLevelTransitComponent> entity, ref StandUpAttemptEvent args)
    {
        // Autostand is deliberately left alone: the entity should get up by itself once it has landed.
        args.Cancelled = true;
    }

    /// <summary>
    ///     Nothing in transit collides: it is in the air between two floor planes, not standing on the one it
    ///         happens to be drawn over, so it passes through everything until it lands.
    /// </summary>
    /// <remarks>
    ///     Vetoing the contact rather than clearing CanCollide leaves no physics state to save and put back,
    ///         and so nothing that can be left switched off if a transit ends in an unusual way. The engine
    ///         raises this on both bodies of a pair, so subscribing on the transiting one covers both.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnTransitPreventCollide(Entity<KsZLevelTransitComponent> entity, ref PreventCollideEvent args)
    {
        args.Cancelled = true;
    }

    #endregion

    /// <summary>
    ///     Keeps anything mid-transit physically where it is when the z-level it is falling through changes how
    ///         deep it is.
    /// </summary>
    /// <remarks>
    ///     Height is a fraction of Depth, so leaving it alone would teleport everything mid-fall: the same 0.5
    ///         means twice the distance above the floor once a z-level is twice as deep. Rescaling by the ratio
    ///         preserves the real height, and the entity simply has more or less of the fall left.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnZLevelDepthChanged(Entity<KsZLevelComponent> entity, ref KsZLevelDepthChangedEvent args)
    {
        var heightScale = args.PreviousDepth / args.Depth;

        var enumerator = EntityQueryEnumerator<KsZLevelTransitComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out var transitComponent, out var transformComponent))
        {
            if (transformComponent.MapUid != entity.Owner)
                continue;

            // Clamped because making a z-level shallower can put something that was near its ceiling above it.
            SetTransit(
                (uid, transitComponent),
                Math.Clamp(transitComponent.Height * heightScale, 0f, 1f),
                transitComponent.VerticalVelocity
            );
        }
    }

    #region API

    /// <summary>
    ///     Whether this entity is currently moving vertically between z-levels.
    /// </summary>
    public bool IsInTransit(EntityUid uid)
    {
        return _transitQuery.HasComponent(uid);
    }

    /// <summary>
    ///     Starts vertical transit between z-levels, if the entity is on one and is not already transiting.
    /// </summary>
    /// <param name="initialVerticalVelocity">
    ///     Speed in z-levels per second: negative descends, positive ascends. Zero starts an unsupported entity
    ///         falling from rest.
    /// </param>
    /// <returns>Whether the entity is now in transit.</returns>
    public bool TryStartTransit(Entity<TransformComponent?> entity, float initialVerticalVelocity = 0f)
    {
        // The stack is built out of maps and floored with grids; neither is a thing that falls through it.
        // This is not hypothetical. Placing a tile in open space spawns a grid, and the parent change that
        //      comes with it lands straight in OnPhysicsParentChanged - grids carry a PhysicsComponent, are
        //      never weightless and are never InAir, so nothing above stopped them. A brand new grid has no
        //      tiles yet either, so it does not even read as standing on solid floor. It would start falling,
        //      taking the knockdown, collision veto and landing crush with it, and crush whatever was below
        //      using the grid's own world AABB.
        if (_mapGridQuery.HasComponent(entity.Owner) || _mapQuery.HasComponent(entity.Owner))
            return false;

        // Never restart an ongoing transit - that would throw away the speed it has built up - but a fresh
        //      push still adds to it, so something can be launched or slammed mid-fall.
        if (_transitQuery.TryGetComponent(entity.Owner, out var ongoingTransitComponent))
        {
            if (initialVerticalVelocity != 0f)
            {
                ongoingTransitComponent.VerticalVelocity += initialVerticalVelocity;
                Dirty(entity.Owner, ongoingTransitComponent);
            }

            return true;
        }

        if (!EntityManager.TransformQuery.Resolve(entity.Owner, ref entity.Comp, logMissing: false))
            return false;

        if (entity.Comp.MapID == MapId.Nullspace ||
            entity.Comp.Anchored ||
            _containerSystem.IsEntityInContainer(entity.Owner))
            return false;

        if (!_zLevelSystem.TryGetZLevel(entity, out _))
        {
            RemComp<KsPendingZLevelTransitComponent>(entity.Owner);
            return false;
        }

        // Standing on a solid floor with nothing pushing it upwards. A positive velocity skips this check, so
        //      anything can still be launched up off a floor.
        if (initialVerticalVelocity <= 0f &&
            _zLevelSystem.IsFloorSolidAt(entity.Comp.MapID, _transformSystem.GetWorldPosition(entity.Comp)))
            return false;

        var transitComponent = EnsureComp<KsZLevelTransitComponent>(entity.Owner);
        transitComponent.Height = 0f;
        transitComponent.VerticalVelocity = initialVerticalVelocity;
        Dirty(entity.Owner, transitComponent);

        RemComp<KsPendingZLevelTransitComponent>(entity.Owner);
        return true;
    }

    /// <summary>
    ///     Ends an entity's transit where it stands, without an impact.
    /// </summary>
    public void StopTransit(EntityUid uid)
    {
        RemComp<KsZLevelTransitComponent>(uid);
    }

    #endregion

    #region Integration

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Drained here, clear of the transform and physics work that queued them - but into a scratch list
        //      first. Starting a transit knocks the entity down and refreshes its movement, any of which can
        //      raise the very events that queue into the set, and adding to it mid-enumeration would throw
        //      straight back out of Update. Clearing before the loop also keeps anything queued during the
        //      drain for the next tick rather than dropping it.
        _drainedTransitChecks.Clear();
        _drainedTransitChecks.AddRange(_pendingTransitChecks);
        _pendingTransitChecks.Clear();

        foreach (var pendingUid in _drainedTransitChecks)
        {
            if (TerminatingOrDeleted(pendingUid))
                continue;

            TryStartTransit(pendingUid);
        }

        var enumerator = EntityQueryEnumerator<KsZLevelTransitComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out var transitComponent, out var transformComponent))
            UpdateTransit((uid, transitComponent, transformComponent), frameTime);
    }

    private void UpdateTransit(Entity<KsZLevelTransitComponent, TransformComponent> entity, float frameTime)
    {
        var uid = entity.Owner;
        var transitComponent = entity.Comp1;
        var transformComponent = entity.Comp2;

        // Nowhere for a height to mean anything.
        if (transformComponent.MapID == MapId.Nullspace ||
            _containerSystem.IsEntityInContainer(uid) ||
            !_zLevelSystem.TryGetZLevel((uid, transformComponent), out var zLevelEntity))
        {
            RemComp<KsZLevelTransitComponent>(uid);
            return;
        }

        var velocity = transitComponent.VerticalVelocity;
        var hasGravity = HasGravityAt(zLevelEntity.Value, transformComponent.MapID, _transformSystem.GetWorldPosition(transformComponent));

        // Without gravity where it is, the entity does not accelerate - but it keeps whatever momentum it
        //      already had, and can still cross z-levels on it.
        if (hasGravity)
        {
            velocity = Math.Clamp(
                velocity - _transitGravity * frameTime,
                -_transitTerminalVelocity,
                _transitTerminalVelocity
            );
        }

        if (velocity == 0f)
        {
            // Nothing is left to move it: no speed of its own, and no gravity here to lend it any. Ending the
            //      transit matters because a transiting entity is knocked down, cannot move itself and collides
            //      with nothing - a weightless entity resting against a ceiling would be stuck like that for
            //      good. The pending marker is what starts it falling again if gravity ever comes back.
            if (!hasGravity)
            {
                EnsureComp<KsPendingZLevelTransitComponent>(uid);
                RemComp<KsZLevelTransitComponent>(uid);
                return;
            }

            // Momentarily stationary at the top of an arc. Written back rather than dropped, so the next tick
            //      integrates from this zero instead of from the speed gravity just cancelled out.
            SetTransit((uid, transitComponent), transitComponent.Height, velocity);
            return;
        }

        // One unit of Height spans this z-level's Depth, so a deeper z-level takes proportionally longer to
        //      cross, and an entity builds up proportionally more speed crossing it.
        var height = transitComponent.Height + velocity * frameTime / GetDepth(zLevelEntity.Value);

        if (height > 0f && height < 1f)
        {
            SetTransit((uid, transitComponent), height, velocity);
            return;
        }

        // Crossing is predicted, so a fall never stalls at a z-level boundary waiting on the server. That only
        //      works because the whole stack is replicated to the client and rebuilt as one object, so
        //      Node.Previous/Node.Next mean the same thing on both sides.
        for (var crossing = 0; crossing < MaxCrossingsPerTick && (height <= 0f || height >= 1f); crossing++)
        {
            var node = zLevelEntity.Value.Comp.Node;
            if (node is null)
            {
                // Replicated before the stack was rebuilt; the next state will sort it out.
                SetTransit((uid, transitComponent), Math.Clamp(height, 0f, 1f), velocity);
                return;
            }

            var rising = height >= 1f;
            var targetNode = rising ? node.Next : node.Previous;
            var worldPosition = _transformSystem.GetWorldPosition(transformComponent);

            // The floor plane being crossed always belongs to the lower of the two z-levels: our own floor on
            //      the way down, and the upper z-level's floor — our ceiling — on the way up.
            var crossedMapId = rising
                ? targetNode is { } upperNode ? _mapQuery.GetComponent(upperNode.Value.Owner).MapId : MapId.Nullspace
                : transformComponent.MapID;

            // Nothing that way counts as solid, so the bottom of a stack is a floor and the top is a ceiling.
            if (targetNode is not { } target || _zLevelSystem.IsFloorSolidAt(crossedMapId, worldPosition))
            {
                Impact((uid, transitComponent), rising, velocity);
                return;
            }

            var oldZLevelEntity = zLevelEntity.Value;
            var overshoot = (rising ? height - 1f : height) * GetDepth(oldZLevelEntity);
            zLevelEntity = target.Value;

            _transformSystem.SetMapCoordinates(
                uid,
                new MapCoordinates(worldPosition, _mapQuery.GetComponent(target.Value.Owner).MapId)
            );

            // Renormalise into the new z-level's own Depth, keeping the sub-tick overshoot.
            height = (rising ? 0f : 1f) + overshoot / GetDepth(target.Value);

            var changedEvent = new KsZLevelChangedEvent(oldZLevelEntity.Owner, target.Value.Owner, rising);
            RaiseLocalEvent(uid, ref changedEvent);

            if (TerminatingOrDeleted(uid))
                return;
        }

        SetTransit((uid, transitComponent), Math.Clamp(height, 0f, 1f), velocity);
    }

    private void Impact(Entity<KsZLevelTransitComponent> entity, bool rising, float velocity)
    {
        var impactSpeed = MathF.Abs(velocity);

        entity.Comp.Height = rising ? 1f : 0f;
        entity.Comp.VerticalVelocity = 0f;
        Dirty(entity);

        // Bumped its head. Gravity pulls it back down next tick, or it rests against the ceiling — either way
        //      it is not on a floor, so it stays in transit.
        if (rising)
            return;

        _physicsSystem.WakeBody(entity.Owner);

        // One step, on the surface it came down on, louder than a walked one. Resolved through the ordinary
        //      footstep chain rather than a sound of its own, so shoes, puddles, catwalks and everything else
        //      that colours a footstep colours this too.
        // Played on both sides, before the server-only split below: predicted audio is what puts the thud on
        //      the landing tick for whoever is falling rather than half an RTT after it.
        _moverController.TryPlayFootstep(entity.Owner, volumeModifier: _landingFootstepVolume, pitch: 0.6f);

        // Landing itself is predicted so the client stops the sprite in the right place, but its consequences
        //      are not: re-prediction would re-fire them on every rollback tick.
        if (!_netManager.IsServer)
        {
            RemComp<KsZLevelTransitComponent>(entity.Owner);
            return;
        }

        // Clamped because a subscriber is free to force Damaging on an impact under the threshold, and a
        //      negative specifier applied with ignoreResistances would heal rather than hurt.
        var overThreshold = MathF.Max(0f, impactSpeed - _transitImpactVelocity);

        var attemptEvent = new KsZLevelLandAttemptEvent(impactSpeed, impactSpeed >= _transitImpactVelocity);
        RaiseLocalEvent(entity.Owner, ref attemptEvent);

        if (attemptEvent.Damaging)
        {
            _damageableSystem.TryChangeDamage(
                entity.Owner,
                ImpactDamage * overThreshold,
                ignoreResistances: true
            );

            // Ignoring resistances means this can outright destroy or gib whatever just landed.
            if (TerminatingOrDeleted(entity.Owner))
                return;
        }

        // Ended before the land event rather than after it, so that a subscriber can bounce or relaunch the
        //      entity with TryStartTransit without the transit it just started being torn straight back down.
        // It also has to stop transiting before the crush below, because OnTransitPreventCollide vetoes every
        //      contact while the component is attached - asking the collision question first would always say no.
        RemComp<KsZLevelTransitComponent>(entity.Owner);

        // Deliberately not gated on the faller taking damage itself: talking its own impact damage out of
        //      existence should not also spare whatever it came down on.
        if (overThreshold > 0f)
            CrushLandingTargets(entity.Owner, impactSpeed, overThreshold);

        if (TerminatingOrDeleted(entity.Owner))
            return;

        var landEvent = new KsZLevelLandEvent(impactSpeed, attemptEvent.Damaging);
        RaiseLocalEvent(entity.Owner, ref landEvent);
    }

    /// <summary>
    ///     Damages and stuns whatever a solid entity has just landed on top of.
    /// </summary>
    /// <remarks>
    ///     A transiting entity has its contacts vetoed, so a fall raises no collisions of its own and there is
    ///         nothing to react to - whatever it came down on has to be looked up at the moment of impact, and
    ///         then asked whether a collision would have been allowed at all.
    /// </remarks>
    private void CrushLandingTargets(EntityUid uid, float impactSpeed, float overThreshold)
    {
        // Something with no hard fixtures lands on nothing: it passes through whatever is underneath exactly
        //      as it passed through everything on the way down.
        if (!_fixturesQuery.TryGetComponent(uid, out var fixturesComponent) ||
            !_physicsQuery.TryGetComponent(uid, out var physicsComponent) ||
            !HasHardFixture(fixturesComponent))
            return;

        var transformComponent = Transform(uid);

        _crushTargets.Clear();
        _entityLookupSystem.GetEntitiesIntersecting(
            transformComponent.MapID,
            _physicsSystem.GetWorldAABB(uid, fixturesComponent),
            _crushTargets,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Uncontained
        );

        // Both events say only what landed and how hard, neither of which varies from one target to the next,
        //      so one instance of each serves the whole landing.
        var crushAttemptEvent = new KsZLevelCrushAttemptEvent(uid, impactSpeed);
        var crushedEvent = new KsZLevelCrushedEvent(uid, impactSpeed);

        foreach (var target in _crushTargets)
        {
            if (target.Owner == uid ||
                TerminatingOrDeleted(target.Owner) ||
                !_physicsQuery.TryGetComponent(target.Owner, out var targetPhysicsComponent) ||
                !WouldHardFixturesCollide(
                    (uid, fixturesComponent, physicsComponent),
                    (target.Owner, target.Comp, targetPhysicsComponent)))
                continue;

            // Reused, so one target vetoing its own crush must not go on to spare everything after it.
            crushAttemptEvent.Cancelled = false;
            RaiseLocalEvent(target.Owner, ref crushAttemptEvent);

            if (crushAttemptEvent.Cancelled)
                continue;

            // Resistances apply here, unlike the faller's own impact damage: armour ought to help against
            //      having something dropped on you.
            _damageableSystem.TryChangeDamage(target.Owner, CrushDamage * overThreshold, origin: uid);
            _stunSystem.TryAddParalyzeDuration(target.Owner, _crushStun);

            if (TerminatingOrDeleted(target.Owner))
                continue;

            // Fully readonly, so no subscriber can leave anything behind for the next target.
            RaiseLocalEvent(target.Owner, ref crushedEvent);
        }
    }

    private static bool HasHardFixture(FixturesComponent fixturesComponent)
    {
        foreach (var fixture in fixturesComponent.Fixtures.Values)
        {
            if (fixture.Hard)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether a pair of hard fixtures on these two entities would have been allowed to touch.
    /// </summary>
    /// <remarks>
    ///     The hardness and layer/mask tests are the cheap prefilter the broadphase runs first. Raising
    ///         <see cref="PreventCollideEvent"/> on both bodies afterwards, exactly as the engine does when it
    ///         decides whether to build a contact, is what picks up everything else that gets a say - buckling,
    ///         open doors, faction collision, projectile targeting - so a crush is refused wherever a real
    ///         collision would have been refused, without this having to know any of those rules itself.
    /// </remarks>
    private bool WouldHardFixturesCollide(
        Entity<FixturesComponent, PhysicsComponent> entity,
        Entity<FixturesComponent, PhysicsComponent> otherEntity)
    {
        foreach (var fixture in entity.Comp1.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            foreach (var otherFixture in otherEntity.Comp1.Fixtures.Values)
            {
                if (!otherFixture.Hard)
                    continue;

                if ((fixture.CollisionMask & otherFixture.CollisionLayer) == 0x0 &&
                    (otherFixture.CollisionMask & fixture.CollisionLayer) == 0x0)
                    continue;

                var preventCollideEvent = new PreventCollideEvent(
                    entity.Owner, otherEntity.Owner, entity.Comp2, otherEntity.Comp2, fixture, otherFixture);
                RaiseLocalEvent(entity.Owner, ref preventCollideEvent);

                if (preventCollideEvent.Cancelled)
                    continue;

                preventCollideEvent = new PreventCollideEvent(
                    otherEntity.Owner, entity.Owner, otherEntity.Comp2, entity.Comp2, otherFixture, fixture);
                RaiseLocalEvent(otherEntity.Owner, ref preventCollideEvent);

                if (preventCollideEvent.Cancelled)
                    continue;

                return true;
            }
        }

        return false;
    }

    private void SetTransit(Entity<KsZLevelTransitComponent> entity, float height, float velocity)
    {
        entity.Comp.Height = height;
        entity.Comp.VerticalVelocity = velocity;

        // Dirtying on the client too is load-bearing: it is what puts the component in the modified set, so the
        //      engine restores the last server state before re-predicting instead of integrating twice.
        Dirty(entity);
    }

    #endregion

    /// <summary>
    ///     Whether gravity applies where the entity actually is, rather than merely on the z-level it is on:
    ///         the grid under this world position if it has gravity, otherwise the z-level's own map.
    /// </summary>
    /// <remarks>
    ///     Deliberately spatial, and deliberately not
    ///         <see cref="SharedGravitySystem.EntityGridOrMapHaveGravity"/>, which reads the grid the entity is
    ///         parented to. A falling entity is by definition over a hole, so it is usually parented to the map
    ///         rather than to the grid it is falling through - and a shaft inside a station with gravity should
    ///         still pull it down.
    /// </remarks>
    private bool HasGravityAt(Entity<KsZLevelComponent> zLevelEntity, MapId mapId, Vector2 worldPosition)
    {
        if (_mapSystem.TryFindGridAt(mapId, worldPosition, out var gridUid, out _) &&
            _gravityQuery.TryGetComponent(gridUid, out var gridGravityComponent) &&
            gridGravityComponent.Enabled)
            return true;

        return _gravityQuery.TryGetComponent(zLevelEntity.Owner, out var mapGravityComponent) &&
               mapGravityComponent.Enabled;
    }

    /// <summary>
    ///     How far this z-level's floor plane sits below the floor plane above it. Never zero, so it is always
    ///         safe to divide by.
    /// </summary>
    private static float GetDepth(Entity<KsZLevelComponent> zLevelEntity)
    {
        return MathF.Max(zLevelEntity.Comp.Depth, KsZLevelSystem.MinimumDepth);
    }
}
