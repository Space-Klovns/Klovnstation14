using System.Diagnostics.CodeAnalysis;
using Content.Shared.Trigger;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.ZLevel.Elevators;

/// <summary>
///     Grids that carry themselves up and down a z-level stack, stopping at the floors they are called to.
/// </summary>
/// <remarks>
///     An elevator crossing a gap between two z-levels is always parented to the <em>lower</em> of the two,
///         with <see cref="ActiveZLevelElevatorComponent.Height"/> as its position within that gap. So going
///         up means staying put until the far side is reached, and going down means crossing onto the level
///         below first and then descending through it. That asymmetry is not a quirk of this system - it is
///         the only arrangement the renderer can draw, because a viewport only draws the levels at or below
///         the viewer, so "below my own floor" is not a position anything can be rendered at.
///     Everything that actually moves a grid lives behind <see cref="TryCrossToZLevel"/>, so that the
///         transit-map approach - a throwaway map per gap, which would let a grid be seen mid-flight from
///         another level - can replace it later without touching the queue, the UI, the buttons or the
///         signals.
/// </remarks>
public abstract partial class SharedZLevelElevatorSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<KsZLevelComponent> _zLevelQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;

    /// <summary>
    ///     Elevators whose legs are being advanced this tick.
    /// </summary>
    /// <remarks>
    ///     Drained into a list before anything is advanced, because finishing a leg removes the active
    ///         component and starting the next one adds it straight back - both of which mutate the very
    ///         set an enumerator would be standing in. An arrival also raises events, and a subscriber is
    ///         free to start or stop another elevator from inside one.
    /// </remarks>
    private readonly List<Entity<ZLevelElevatorComponent, ActiveZLevelElevatorComponent>> _activeElevators = [];

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;

        _activeElevators.Clear();

        var enumerator = EntityQueryEnumerator<ActiveZLevelElevatorComponent, ZLevelElevatorComponent>();
        while (enumerator.MoveNext(out var uid, out var activeComponent, out var elevatorComponent))
            _activeElevators.Add((uid, elevatorComponent, activeComponent));

        foreach (var elevatorEntity in _activeElevators)
        {
            // Another elevator's arrival may have deleted this one, or taken its component off it.
            if (TerminatingOrDeleted(elevatorEntity.Owner) ||
                elevatorEntity.Comp2.LifeStage > ComponentLifeStage.Running)
                continue;

            UpdateElevator(elevatorEntity, curTime);
        }
    }

    private void UpdateElevator(Entity<ZLevelElevatorComponent, ActiveZLevelElevatorComponent> entity, TimeSpan curTime)
    {
        var activeComponent = entity.Comp2;

        if (activeComponent.State == ZLevelElevatorState.Dwelling)
        {
            if (curTime < activeComponent.EndTime)
                return;

            // The queue is server-authoritative. The client has the whole of it replicated and could reach
            //      the same answer, but an elevator that mispredicted which floor it was heading for would
            //      drag its passengers' camera a whole z-level sideways when the correction landed.
            if (!_netManager.IsServer)
                return;

            RemComp<ActiveZLevelElevatorComponent>(entity.Owner);
            DecideNextLeg((entity.Owner, entity.Comp1));
            return;
        }

        var progress = GetLegProgress(activeComponent, curTime);
        activeComponent.Height = activeComponent.Rising ? progress : 1f - progress;

        if (progress < 1f)
            return;

        if (!_netManager.IsServer)
            return;

        FinishLeg(entity);
    }

    /// <summary>
    ///     How far through its current leg the elevator is, from 0 to 1.
    /// </summary>
    /// <remarks>
    ///     A leg is scheduled rather than integrated, so unlike a falling entity an elevator can never
    ///         overshoot past the end of one within a tick - the next leg is stamped with the time it
    ///         actually started. That is why there is no multi-crossing loop here and there is one in
    ///         <see cref="Physics.KsZLevelPhysicsSystem"/>.
    /// </remarks>
    private static float GetLegProgress(ActiveZLevelElevatorComponent activeComponent, TimeSpan curTime)
    {
        var duration = (activeComponent.EndTime - activeComponent.StartTime).TotalSeconds;

        // A leg of no length is one that is already over. Guarded rather than assumed, because Depth is only
        //      clamped to MinimumDepth and a SecondsPerDepth of zero is a legal thing for yaml to say.
        if (duration <= 0d)
            return 1f;

        return Math.Clamp((float)((curTime - activeComponent.StartTime).TotalSeconds / duration), 0f, 1f);
    }

    /// <summary>
    ///     Ends the leg the elevator has just completed, arriving it on the far side of the gap.
    /// </summary>
    private void FinishLeg(Entity<ZLevelElevatorComponent, ActiveZLevelElevatorComponent> entity)
    {
        var rising = entity.Comp2.Rising;

        if (!TryGetElevatorZLevel(entity.Owner, out var zLevelEntity))
        {
            StopElevator((entity.Owner, entity.Comp1));
            return;
        }

        var departedZLevelEntity = zLevelEntity.Value;
        var arrivedZLevelEntity = zLevelEntity.Value;

        // A descending leg crossed onto its destination when it began, and has been travelling down through
        //      it ever since, so only a rising one still has a boundary left to cross.
        if (rising)
        {
            if (!_zLevelSystem.TryGetZLevelAbove(departedZLevelEntity!, out var aboveEntity) ||
                !TryCrossToZLevel((entity.Owner, entity.Comp1), aboveEntity.Value))
            {
                // The stack changed under us mid-leg, or the move was refused outright. Stopping where we
                //      stand beats carrying on towards a floor that is no longer there.
                StopElevator((entity.Owner, entity.Comp1));
                return;
            }

            arrivedZLevelEntity = aboveEntity.Value;
        }

        RemComp<ActiveZLevelElevatorComponent>(entity.Owner);

        // Run again on arrival, and not only on the map change: a descending elevator enters its destination
        //      z-level at the *start* of its leg, so anything that stepped into the shaft while it was on
        //      its way down has not been swept yet.
        FlattenArrival((entity.Owner, entity.Comp1));

        if (TerminatingOrDeleted(entity.Owner))
            return;

        var arrivedEvent = new ZLevelElevatorArrivedEvent(departedZLevelEntity.Owner, arrivedZLevelEntity.Owner, rising);
        RaiseLocalEvent(entity.Owner, ref arrivedEvent);

        if (TerminatingOrDeleted(entity.Owner))
            return;

        DecideNextLeg((entity.Owner, entity.Comp1));

        // Arrived, and found nothing else to do, so this is where it has come to rest. A floor it was
        //      actually called to raises this from its dwell instead, and a floor it is only passing raises
        //      nothing at all - a lift going by should not open its doors. Checked by whether a new leg or
        //      a dwell was started rather than tracked with a flag, so the three cases cannot drift apart.
        if (!TerminatingOrDeleted(entity.Owner) &&
            !HasComp<ActiveZLevelElevatorComponent>(entity.Owner))
            RaiseStopped((entity.Owner, entity.Comp1), arrivedZLevelEntity);
    }

    #region Movement

    /// <summary>
    ///     Moves the elevator's grid onto an adjacent z-level, and everything that entails.
    /// </summary>
    /// <remarks>
    ///     Server-only work - it gibs, and it deletes tiles - so the shared implementation refuses and the
    ///         client simply waits for the transform and component state saying it happened. This and
    ///         <see cref="FlattenArrival"/> are the only two places an elevator's grid is touched, which is
    ///         what makes the movement approach swappable.
    /// </remarks>
    /// <returns>Whether the grid is now on <paramref name="targetZLevel"/>.</returns>
    protected virtual bool TryCrossToZLevel(Entity<ZLevelElevatorComponent> entity, Entity<KsZLevelComponent> targetZLevel)
    {
        return false;
    }

    /// <summary>
    ///     Clears whatever is standing where the elevator has just come to rest.
    /// </summary>
    protected virtual void FlattenArrival(Entity<ZLevelElevatorComponent> entity)
    {
    }

    #endregion

    #region Triggers

    [SubscribeLocalEvent]
    private void OnTrigger(Entity<ZLevelElevatorComponent> entity, ref TriggerEvent args)
    {
        // A null key fires every trigger on the entity at once, which for an elevator configured with both
        //      key sets would mean being told to go up and down in the same breath. Only an explicit key
        //      moves one.
        if (args.Key is not { } key)
            return;

        if (entity.Comp.UpKeysIn.Contains(key))
        {
            args.Handled |= TryStartAscent(entity);
            return;
        }

        if (entity.Comp.DownKeysIn.Contains(key))
            args.Handled |= TryStartDescent(entity);
    }

    #endregion

    #region Lookup

    /// <summary>
    ///     Binds an elevator to a named shaft, or to none.
    /// </summary>
    /// <seealso cref="ZLevelElevatorComponent.ShaftId"/>
    public void SetShaftId(Entity<ZLevelElevatorComponent> entity, string? shaftId)
    {
        if (entity.Comp.ShaftId == shaftId)
            return;

        entity.Comp.ShaftId = shaftId;
        Dirty(entity);
    }

    /// <summary>
    ///     Binds a controller to a named shaft, or to none.
    /// </summary>
    /// <seealso cref="ZLevelElevatorControllerComponent.ShaftId"/>
    public void SetShaftId(Entity<ZLevelElevatorControllerComponent> entity, string? shaftId)
    {
        if (entity.Comp.ShaftId == shaftId)
            return;

        entity.Comp.ShaftId = shaftId;
        Dirty(entity);
    }

    /// <summary>
    ///     The z-level the elevator's grid is currently on.
    /// </summary>
    public bool TryGetElevatorZLevel(EntityUid uid, [NotNullWhen(true)] out Entity<KsZLevelComponent>? zLevelEntity)
    {
        zLevelEntity = null;

        if (!EntityManager.TransformQuery.TryGetComponent(uid, out var transformComponent) ||
            transformComponent.MapUid is not { } mapUid ||
            !_zLevelQuery.TryGetComponent(mapUid, out var zLevelComponent))
            return false;

        zLevelEntity = (mapUid, zLevelComponent);
        return true;
    }

    /// <summary>
    ///     The elevator an entity is talking about: the one whose <see cref="ZLevelElevatorComponent.ShaftId"/>
    ///         matches <paramref name="shaftId"/>, or - where that is null - whichever unlabelled elevator in
    ///         the entity's own z-level stack is horizontally nearest.
    /// </summary>
    /// <remarks>
    ///     Nearest rather than "the one on this grid", because the whole point of the controller is that it
    ///         does not have to be on the elevator, and a call button never is.
    /// </remarks>
    public bool TryResolveElevator(
        EntityUid uid,
        string? shaftId,
        [NotNullWhen(true)] out Entity<ZLevelElevatorComponent>? elevatorEntity)
    {
        elevatorEntity = null;

        if (!TryGetElevatorZLevel(uid, out var zLevelEntity))
            return false;

        var position = _transformSystem.GetWorldPosition(uid);
        var nearestDistanceSquared = float.MaxValue;

        var enumerator = EntityQueryEnumerator<ZLevelElevatorComponent, TransformComponent>();
        while (enumerator.MoveNext(out var candidateUid, out var candidateComponent, out var candidateTransformComponent))
        {
            if (shaftId is not null)
            {
                if (candidateComponent.ShaftId != shaftId)
                    continue;
            }
            else if (candidateComponent.ShaftId is not null)
            {
                // A shaft that named itself is opting out of being found by proximity - otherwise every
                //      unlabelled button in a two-shaft lobby would wander onto the labelled lift.
                continue;
            }

            // Only a candidate if the asker's own stack can reach it, so two unrelated stations each running
            //      an unlabelled lift never resolve to each other.
            if (candidateTransformComponent.MapUid is not { } candidateMapUid ||
                !_zLevelSystem.AreInSameStack(zLevelEntity.Value.Owner, candidateMapUid))
                continue;

            var distanceSquared = (_transformSystem.GetWorldPosition(candidateTransformComponent) - position).LengthSquared();
            if (distanceSquared >= nearestDistanceSquared)
                continue;

            nearestDistanceSquared = distanceSquared;
            elevatorEntity = (candidateUid, candidateComponent);
        }

        return elevatorEntity is not null;
    }

    #endregion
}
