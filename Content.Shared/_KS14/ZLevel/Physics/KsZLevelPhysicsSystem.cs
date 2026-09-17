using System.Numerics;
using Content.Shared._KS14.CCVar;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Content.Shared.Movement.Events;
using Content.Shared.Throwing;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
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
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<GravityComponent> _gravityQuery = default!;
    [Dependency] private EntityQuery<KsPendingZLevelTransitComponent> _pendingTransitQuery = default!;
    [Dependency] private EntityQuery<KsZLevelTransitComponent> _transitQuery = default!;
    [Dependency] private EntityQuery<MapComponent> _mapQuery = default!;

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
            { "Blunt", FixedPoint2.New(5) },
        },
    };

    private float _transitGravity;
    private float _transitTerminalVelocity;
    private float _transitImpactVelocity;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.ZLevelTransitGravity, value => _transitGravity = value, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelTransitTerminalVelocity, value => _transitTerminalVelocity = value, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelTransitImpactVelocity, value => _transitImpactVelocity = value, true);
    }

    #region Triggers

    [SubscribeLocalEvent]
    private void OnWeightlessnessChanged(Entity<KsPendingZLevelTransitComponent> entity, ref WeightlessnessChangedEvent args)
    {
        if (args.Weightless)
            return;

        TryStartTransit(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnBodyStatusChanged(Entity<KsPendingZLevelTransitComponent> entity, ref PhysicsBodyStatusChangedEvent args)
    {
        if (args.NewStatus == BodyStatus.InAir)
            return;

        TryStartTransit(entity.Owner);
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

        TryStartTransit((entity.Owner, transformComponent));
    }

    [SubscribeLocalEvent]
    private void OnPhysicsLand(Entity<PhysicsComponent> entity, ref LandEvent args)
    {
        TryStartTransit(entity.Owner);
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

    #region Self-movement blocking

    [SubscribeLocalEvent]
    private void OnTransitStartup(Entity<KsZLevelTransitComponent> entity, ref ComponentStartup args)
    {
        _actionBlockerSystem.UpdateCanMove(entity.Owner);

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

    #endregion

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
        // Never restart or reset an ongoing transit; see OnPhysicsParentChanged.
        if (_transitQuery.HasComponent(entity.Owner))
            return true;

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
            IsFloorSolidAt(entity.Comp.MapID, _transformSystem.GetWorldPosition(entity.Comp)))
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

        // Without gravity where it is, the entity does not accelerate - but it keeps whatever momentum it
        //      already had, and can still cross z-levels on it.
        if (HasGravityAt(zLevelEntity.Value, transformComponent.MapID, _transformSystem.GetWorldPosition(transformComponent)))
        {
            velocity = Math.Clamp(
                velocity - _transitGravity * frameTime,
                -_transitTerminalVelocity,
                _transitTerminalVelocity
            );
        }

        // Hovering weightless, or resting against a ceiling. Costs a query iteration and nothing else.
        if (velocity == 0f)
            return;

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
            if (targetNode is not { } target || IsFloorSolidAt(crossedMapId, worldPosition))
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

        // Landing itself is predicted so the client stops the sprite in the right place, but its consequences
        //      are not: re-prediction would re-fire them on every rollback tick.
        if (!_netManager.IsServer)
        {
            RemComp<KsZLevelTransitComponent>(entity.Owner);
            return;
        }

        var attemptEvent = new KsZLevelLandAttemptEvent(impactSpeed, impactSpeed >= _transitImpactVelocity);
        RaiseLocalEvent(entity.Owner, ref attemptEvent);

        if (attemptEvent.Damaging)
        {
            _damageableSystem.TryChangeDamage(
                entity.Owner,
                ImpactDamage * (impactSpeed - _transitImpactVelocity),
                ignoreResistances: true
            );

            // Ignoring resistances means this can outright destroy or gib whatever just landed.
            if (TerminatingOrDeleted(entity.Owner))
                return;
        }

        var landEvent = new KsZLevelLandEvent(impactSpeed, attemptEvent.Damaging);
        RaiseLocalEvent(entity.Owner, ref landEvent);

        if (TerminatingOrDeleted(entity.Owner))
            return;

        RemComp<KsZLevelTransitComponent>(entity.Owner);
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
    ///     Whether the z-level floor plane on this map is solid at this world position.
    /// </summary>
    /// <remarks>
    ///     Deliberately spatial rather than reading <see cref="TransformComponent.GridUid"/>: a crossing
    ///         re-parents the entity, so the cached grid is only correct once the crossing is already done.
    /// </remarks>
    private bool IsFloorSolidAt(MapId mapId, Vector2 worldPosition)
    {
        // Open space: nothing to stand on, and nothing to bump into.
        if (!_mapSystem.TryFindGridAt(mapId, worldPosition, out var gridUid, out var mapGridComponent))
            return false;

        return !_mapSystem.GetTileRef((gridUid, mapGridComponent), new MapCoordinates(worldPosition, mapId)).Tile.IsEmpty;
    }

    /// <summary>
    ///     How far this z-level's floor plane sits below the floor plane above it. Never zero, so it is always
    ///         safe to divide by.
    /// </summary>
    private static float GetDepth(Entity<KsZLevelComponent> zLevelEntity)
    {
        return MathF.Max(zLevelEntity.Comp.Depth, 0.01f);
    }
}
