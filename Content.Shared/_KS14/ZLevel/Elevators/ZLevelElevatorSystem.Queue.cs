namespace Content.Shared._KS14.ZLevel.Elevators;

/// <summary>
///     Which floor an elevator goes to next.
/// </summary>
/// <remarks>
///     This is the ordinary lift rule, sometimes called an elevator sweep: keep going the way you are already
///         going for as long as anything is still calling from that direction, then turn round. A lift that
///         simply served its nearest call instead would oscillate around a busy middle floor and never reach
///         the ends of the shaft.
/// </remarks>
public abstract partial class SharedZLevelElevatorSystem : EntitySystem
{
    private readonly List<EntityUid> _staleCalls = [];

    #region API

    /// <summary>
    ///     Calls the elevator to a z-level, so that it stops there on the next sweep that passes it.
    /// </summary>
    /// <remarks>
    ///     A call already in the set is not an error - a button being leaned on should be free, not a way to
    ///         reorder the queue.
    /// </remarks>
    /// <returns>Whether the z-level is now called.</returns>
    public bool TryCallToZLevel(Entity<ZLevelElevatorComponent> entity, Entity<KsZLevelComponent?> zLevelEntity)
    {
        if (!TryGetElevatorZLevel(entity.Owner, out var currentZLevelEntity))
            return false;

        // A call to a z-level the elevator cannot reach would sit in the set forever, quietly holding the
        //      lift's direction hostage on every decision it made afterwards.
        if (!_zLevelSystem.AreInSameStack(currentZLevelEntity.Value.Owner, zLevelEntity.Owner))
            return false;

        if (!entity.Comp.CalledZLevels.Add(zLevelEntity.Owner))
            return true;

        Dirty(entity);

        // Only nudge an idle elevator: one that is mid-leg or dwelling reconsiders when that finishes, and
        //      deciding again now would abandon a leg it is halfway through.
        if (!HasComp<ActiveZLevelElevatorComponent>(entity.Owner))
            DecideNextLeg(entity);

        return true;
    }

    /// <summary>
    ///     Sends the elevator up one z-level, regardless of what is called.
    /// </summary>
    /// <returns>Whether the elevator is now on its way.</returns>
    public bool TryStartAscent(Entity<ZLevelElevatorComponent> entity)
    {
        return TryStartLeg(entity, rising: true);
    }

    /// <summary>
    ///     Sends the elevator down one z-level, regardless of what is called.
    /// </summary>
    /// <returns>Whether the elevator is now on its way.</returns>
    public bool TryStartDescent(Entity<ZLevelElevatorComponent> entity)
    {
        return TryStartLeg(entity, rising: false);
    }

    /// <summary>
    ///     Brings the elevator to a halt where it stands and forgets everything it was called to.
    /// </summary>
    /// <remarks>
    ///     Does not undo a leg in progress - there is no partial z-level for a grid to be left on - so an
    ///         elevator stopped mid-flight finishes crossing and then stays put.
    /// </remarks>
    public void StopElevator(Entity<ZLevelElevatorComponent> entity)
    {
        entity.Comp.CalledZLevels.Clear();
        entity.Comp.Direction = ZLevelElevatorDirection.Idle;
        Dirty(entity);

        if (!HasComp<ActiveZLevelElevatorComponent>(entity.Owner))
            return;

        RemComp<ActiveZLevelElevatorComponent>(entity.Owner);

        if (TryGetElevatorZLevel(entity.Owner, out var zLevelEntity))
            RaiseStopped(entity, zLevelEntity.Value);
    }

    #endregion

    /// <summary>
    ///     Works out where a stationary elevator goes next, and sends it there.
    /// </summary>
    internal void DecideNextLeg(Entity<ZLevelElevatorComponent> entity)
    {
        if (!TryGetElevatorZLevel(entity.Owner, out var currentZLevelEntity))
        {
            SetDirection(entity, ZLevelElevatorDirection.Idle);
            return;
        }

        PruneStaleCalls(entity, currentZLevelEntity.Value);

        // Serve the floor we are standing on before going anywhere, so a lift called to where it already is
        //      opens up instead of setting off on a round trip.
        if (entity.Comp.CalledZLevels.Remove(currentZLevelEntity.Value.Owner))
        {
            Dirty(entity);
            StartDwell(entity, currentZLevelEntity.Value);
            return;
        }

        var direction = ChooseDirection(entity, currentZLevelEntity.Value);
        if (direction == ZLevelElevatorDirection.Idle)
        {
            SetDirection(entity, ZLevelElevatorDirection.Idle);
            return;
        }

        SetDirection(entity, direction);

        if (!TryStartLeg(entity, rising: direction == ZLevelElevatorDirection.Up))
            SetDirection(entity, ZLevelElevatorDirection.Idle);
    }

    /// <summary>
    ///     Which way to sweep from <paramref name="currentZLevelEntity"/>, given what is called.
    /// </summary>
    private ZLevelElevatorDirection ChooseDirection(
        Entity<ZLevelElevatorComponent> entity,
        Entity<KsZLevelComponent> currentZLevelEntity)
    {
        var currentIndex = _zLevelSystem.GetStackIndex(currentZLevelEntity.Owner);
        if (currentIndex < 0)
            return ZLevelElevatorDirection.Idle;

        var nearestAbove = int.MaxValue;
        var nearestBelow = int.MaxValue;

        foreach (var calledUid in entity.Comp.CalledZLevels)
        {
            var calledIndex = _zLevelSystem.GetStackIndex(calledUid);
            if (calledIndex < 0)
                continue;

            var distance = Math.Abs(calledIndex - currentIndex);
            if (calledIndex > currentIndex)
                nearestAbove = Math.Min(nearestAbove, distance);
            else if (calledIndex < currentIndex)
                nearestBelow = Math.Min(nearestBelow, distance);
        }

        var hasAbove = nearestAbove != int.MaxValue;
        var hasBelow = nearestBelow != int.MaxValue;

        return entity.Comp.Direction switch
        {
            // Keep going the way we were for as long as anything is still calling that way, however much
            //      nearer something behind us is. This is the whole of what makes a lift feel like a lift.
            ZLevelElevatorDirection.Up when hasAbove => ZLevelElevatorDirection.Up,
            ZLevelElevatorDirection.Down when hasBelow => ZLevelElevatorDirection.Down,

            // Nothing left that way, so turn round if there is anything the other way.
            ZLevelElevatorDirection.Up when hasBelow => ZLevelElevatorDirection.Down,
            ZLevelElevatorDirection.Down when hasAbove => ZLevelElevatorDirection.Up,

            // Starting from rest there is no sweep to continue, so the nearest call wins.
            _ when hasAbove && hasBelow => nearestAbove <= nearestBelow
                ? ZLevelElevatorDirection.Up
                : ZLevelElevatorDirection.Down,
            _ when hasAbove => ZLevelElevatorDirection.Up,
            _ when hasBelow => ZLevelElevatorDirection.Down,

            _ => ZLevelElevatorDirection.Idle,
        };
    }

    /// <summary>
    ///     Drops calls to z-levels that have since been deleted or left the elevator's stack.
    /// </summary>
    /// <remarks>
    ///     Without this, a z-level unlinked while it was called would keep answering "something is still
    ///         calling from up there" to every direction decision the lift ever made again, and it would
    ///         park at the top of the shaft for the rest of the round.
    /// </remarks>
    private void PruneStaleCalls(Entity<ZLevelElevatorComponent> entity, Entity<KsZLevelComponent> currentZLevelEntity)
    {
        _staleCalls.Clear();

        foreach (var calledUid in entity.Comp.CalledZLevels)
        {
            if (!TerminatingOrDeleted(calledUid) &&
                _zLevelSystem.AreInSameStack(currentZLevelEntity.Owner, calledUid))
                continue;

            _staleCalls.Add(calledUid);
        }

        if (_staleCalls.Count == 0)
            return;

        foreach (var staleUid in _staleCalls)
            entity.Comp.CalledZLevels.Remove(staleUid);

        Dirty(entity);
    }

    /// <summary>
    ///     Sends the elevator across one gap, up or down.
    /// </summary>
    /// <remarks>
    ///     A descending leg crosses onto the z-level below immediately and then travels down through it,
    ///         because a grid in a gap is always parented to the lower of the two z-levels. A rising one
    ///         stays where it is and crosses at the far end.
    /// </remarks>
    /// <returns>Whether the elevator is now travelling.</returns>
    private bool TryStartLeg(Entity<ZLevelElevatorComponent> entity, bool rising)
    {
        // Departing is a server decision, like everything else about the queue. The client is told a leg
        //      started by the replicated component, and works its progress out from the times on it; if it
        //      started one of its own it would be animating a journey the server has not agreed to.
        if (!_netManager.IsServer)
            return false;

        // Already busy. Refusing rather than restarting is what stops a mashed trigger from holding an
        //      elevator in place by resetting its leg every tick.
        if (HasComp<ActiveZLevelElevatorComponent>(entity.Owner))
            return false;

        // Only a grid can be an elevator: everything aboard rides along through the transform hierarchy,
        //      and a loose entity has KsZLevelPhysicsSystem for this instead.
        if (!_mapGridQuery.HasComponent(entity.Owner))
            return false;

        if (!TryGetElevatorZLevel(entity.Owner, out var currentZLevelEntity))
            return false;

        // Nothing that way. The ends of a stack are a floor and a ceiling, the same as they are to anything
        //      falling through it.
        if (!_zLevelSystem.TryGetAdjacentZLevel(currentZLevelEntity.Value!, rising, out var targetZLevelEntity))
            return false;

        var departedUid = currentZLevelEntity.Value.Owner;

        var departingEvent = new ZLevelElevatorDepartingEvent(departedUid, rising);
        RaiseLocalEvent(entity.Owner, ref departingEvent);

        if (TerminatingOrDeleted(entity.Owner))
            return false;

        // The gap belongs to the lower of the two z-levels, so descending crosses first and then travels.
        var gapZLevelEntity = rising ? currentZLevelEntity.Value : targetZLevelEntity.Value;
        if (!rising && !TryCrossToZLevel(entity, targetZLevelEntity.Value))
            return false;

        if (TerminatingOrDeleted(entity.Owner))
            return false;

        var duration = TimeSpan.FromSeconds(
            Math.Max(gapZLevelEntity.Comp.Depth, KsZLevelSystem.MinimumDepth) * entity.Comp.SecondsPerDepth);

        var curTime = _gameTiming.CurTime;
        var activeComponent = EnsureComp<ActiveZLevelElevatorComponent>(entity.Owner);
        activeComponent.State = ZLevelElevatorState.Travelling;
        activeComponent.Rising = rising;
        activeComponent.StartTime = curTime;
        activeComponent.EndTime = curTime + duration;
        activeComponent.Height = rising ? 0f : 1f;
        Dirty(entity.Owner, activeComponent);

        return true;
    }

    /// <summary>
    ///     Holds the elevator at a floor for its dwell time before it decides where to go next.
    /// </summary>
    private void StartDwell(Entity<ZLevelElevatorComponent> entity, Entity<KsZLevelComponent> zLevelEntity)
    {
        var curTime = _gameTiming.CurTime;

        var activeComponent = EnsureComp<ActiveZLevelElevatorComponent>(entity.Owner);
        activeComponent.State = ZLevelElevatorState.Dwelling;
        activeComponent.StartTime = curTime;
        activeComponent.EndTime = curTime + entity.Comp.DwellTime;
        activeComponent.Height = 0f;
        Dirty(entity.Owner, activeComponent);

        RaiseStopped(entity, zLevelEntity);
    }

    private void RaiseStopped(Entity<ZLevelElevatorComponent> entity, Entity<KsZLevelComponent> zLevelEntity)
    {
        var stoppedEvent = new ZLevelElevatorStoppedEvent(zLevelEntity.Owner);
        RaiseLocalEvent(entity.Owner, ref stoppedEvent);
    }

    private void SetDirection(Entity<ZLevelElevatorComponent> entity, ZLevelElevatorDirection direction)
    {
        if (entity.Comp.Direction == direction)
            return;

        entity.Comp.Direction = direction;
        Dirty(entity);
    }
}
