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
///     An elevator crossing a gap between two z-levels rides a map of its own for the length of the leg - see
///         <see cref="Transit.KsZLevelGapComponent"/>. Both directions are therefore the same three steps:
///         enter the gap, travel through it, land on the far side. The elevator is never parented to a
///         z-level it is not actually standing on, so there is no asymmetry between going up and going down.
///     Everything that moves a grid lives behind the seam in the Movement region below, which is why this
///         file did not have to change when the movement approach did.
/// </remarks>
public abstract partial class SharedZLevelElevatorSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<ActiveZLevelElevatorComponent> _activeElevatorQuery = default!;
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

        // Run on both sides. The client is driving the same clock off the same replicated timestamps, so
        //      this is what makes the ride move every frame instead of stepping once per server state.
        SetLegProgress((entity.Owner, entity.Comp1), activeComponent.Height);

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
        var departedUid = entity.Comp2.DepartedZLevel;

        // The target was recorded at departure, so a stack relinked mid-flight cannot land this somewhere
        //      nobody sent it. If it has stopped being a z-level, there is nothing left to arrive at.
        if (!_zLevelQuery.TryGetComponent(entity.Comp2.TargetZLevel, out var targetZLevelComponent))
        {
            StopElevator((entity.Owner, entity.Comp1));
            return;
        }

        Entity<KsZLevelComponent> arrivedZLevelEntity = (entity.Comp2.TargetZLevel, targetZLevelComponent);

        if (!TryLeaveGap((entity.Owner, entity.Comp1), arrivedZLevelEntity))
        {
            StopElevator((entity.Owner, entity.Comp1));
            return;
        }

        RemComp<ActiveZLevelElevatorComponent>(entity.Owner);

        // Only on landing: the gap the elevator has spent the leg on is a map of its own with nothing else
        //      on it, so there has never been anything to sweep until now.
        FlattenArrival((entity.Owner, entity.Comp1));

        if (TerminatingOrDeleted(entity.Owner))
            return;

        var arrivedEvent = new ZLevelElevatorArrivedEvent(departedUid, arrivedZLevelEntity.Owner, rising);
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
    ///     Lifts the elevator's grid off a z-level and onto a gap map between two of them.
    /// </summary>
    /// <remarks>
    ///     Server-only work - it makes a map and reparents a grid - so the shared implementation refuses and
    ///         the client simply waits for the transform and component state saying it happened. These four
    ///         methods are the only places an elevator's grid is touched, which is what let the movement
    ///         approach be replaced without the queue, the UI, the buttons or the signals noticing.
    /// </remarks>
    /// <param name="rising">Which end of the gap the elevator starts at.</param>
    /// <returns>Whether the grid is now crossing the gap.</returns>
    protected virtual bool TryEnterGap(
        Entity<ZLevelElevatorComponent> entity,
        Entity<KsZLevelComponent> lowerZLevel,
        Entity<KsZLevelComponent> upperZLevel,
        bool rising)
    {
        return false;
    }

    /// <summary>
    ///     Sets the elevator's grid down on a z-level, and tears down the gap it was crossing.
    /// </summary>
    /// <returns>Whether the grid is now on <paramref name="targetZLevel"/>.</returns>
    protected virtual bool TryLeaveGap(Entity<ZLevelElevatorComponent> entity, Entity<KsZLevelComponent> targetZLevel)
    {
        return false;
    }

    /// <summary>
    ///     Abandons a leg partway through, landing the elevator on whichever floor it was nearer to.
    /// </summary>
    /// <remarks>
    ///     A no-op for an elevator that is not on a gap, so the paths that stop an elevator do not each have
    ///         to work out whether it happened to be mid-flight.
    /// </remarks>
    protected virtual void AbortLeg(Entity<ZLevelElevatorComponent> entity)
    {
    }

    /// <summary>
    ///     Moves the elevator to a new position within the gap it is crossing.
    /// </summary>
    /// <remarks>
    ///     Runs on both sides, unlike the rest of the seam: the gap map and its progress are replicated, and
    ///         a client that only moved the elevator when a server state arrived would show the ride
    ///         stepping rather than travelling.
    /// </remarks>
    /// <param name="progress">0 at the lower z-level's floor plane, 1 at the upper one's.</param>
    protected virtual void SetLegProgress(Entity<ZLevelElevatorComponent> entity, float progress)
    {
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
    ///     Whether the elevator is actually crossing a gap right now.
    /// </summary>
    /// <remarks>
    ///     Not the same question as whether it has an <see cref="ActiveZLevelElevatorComponent"/>, which is
    ///         also attached for the dwell it spends sitting at a floor. Anything a player can see or press
    ///         wants this one: a lift that has arrived, opened up and crushed whatever was underneath it has
    ///         plainly stopped, and a panel still reading "descending" for the length of the dwell is the
    ///         panel being wrong. "Busy, do not send it anywhere" is the other question, and that one is
    ///         still the component.
    /// </remarks>
    public bool IsTravelling(EntityUid uid)
    {
        return _activeElevatorQuery.TryGetComponent(uid, out var activeComponent) &&
               activeComponent.State == ZLevelElevatorState.Travelling;
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
