using Content.Server.DeviceLinking.Systems;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Elevators;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.ZLevel.Elevators;

/// <summary>
///     Everything that talks to an elevator rather than being one: call buttons, controller consoles, and
///         the device link signals an installation drives its doors from.
/// </summary>
public sealed partial class ZLevelElevatorSystem
{
    [Dependency] private DeviceLinkSystem _deviceLinkSystem = default!;
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private UseDelaySystem _useDelaySystem = default!;
    [Dependency] private UserInterfaceSystem _userInterfaceSystem = default!;

    private readonly List<Entity<KsZLevelComponent>> _stackEntities = [];
    private readonly List<ZLevelElevatorFloor> _floors = [];

    #region Call buttons

    [SubscribeLocalEvent]
    private void OnCallButtonActivate(Entity<ZLevelElevatorCallButtonComponent> entity, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = TryPressCallButton(entity, args.User);
    }

    private bool TryPressCallButton(Entity<ZLevelElevatorCallButtonComponent> entity, EntityUid user)
    {
        if (_useDelaySystem.IsDelayed(entity.Owner))
            return false;

        _audioSystem.PlayPvs(entity.Comp.ClickSound, entity.Owner);

        if (!TryGetElevatorZLevel(entity.Owner, out var zLevelEntity))
        {
            _popupSystem.PopupEntity(Loc.GetString("zlevel-elevator-call-no-shaft"), entity.Owner, user);
            return true;
        }

        if (!TryResolveElevator(entity.Owner, entity.Comp.ShaftId, out var elevatorEntity))
        {
            _popupSystem.PopupEntity(Loc.GetString("zlevel-elevator-call-no-shaft"), entity.Owner, user);
            return true;
        }

        // Already here and not going anywhere, so there is nothing to call. Said out loud rather than
        //      silently ignored, because a button that does nothing reads as a broken one.
        if (!entity.Comp.CallWhenPresent &&
            !HasComp<ActiveZLevelElevatorComponent>(elevatorEntity.Value.Owner) &&
            TryGetElevatorZLevel(elevatorEntity.Value.Owner, out var elevatorZLevelEntity) &&
            elevatorZLevelEntity.Value.Owner == zLevelEntity.Value.Owner)
        {
            _popupSystem.PopupEntity(Loc.GetString("zlevel-elevator-call-already-here"), entity.Owner, user);
            _useDelaySystem.TryResetDelay(entity.Owner);
            return true;
        }

        _useDelaySystem.TryResetDelay(entity.Owner);

        if (!TryCallToZLevel(elevatorEntity.Value, zLevelEntity.Value.Owner))
        {
            _popupSystem.PopupEntity(Loc.GetString("zlevel-elevator-call-no-shaft"), entity.Owner, user);
            return true;
        }

        _popupSystem.PopupEntity(Loc.GetString("zlevel-elevator-call-called"), entity.Owner, user);
        return true;
    }

    #endregion

    #region Controller

    [SubscribeLocalEvent]
    private void OnControllerUiOpened(Entity<ZLevelElevatorControllerComponent> entity, ref BoundUIOpenedEvent args)
    {
        UpdateControllerUi(entity);
    }

    [SubscribeLocalEvent]
    private void OnControllerSelectFloor(Entity<ZLevelElevatorControllerComponent> entity, ref ZLevelElevatorSelectFloorMessage args)
    {
        if (!TryResolveElevator(entity.Owner, entity.Comp.ShaftId, out var elevatorEntity))
            return;

        var zLevelUid = GetEntity(args.ZLevel);

        // Everything here comes off the wire, so the z-level has to be re-checked rather than trusted:
        //      TryCallToZLevel refuses anything outside the elevator's own stack, which is what stops a
        //      crafted message parking the lift against a z-level it can never reach.
        TryCallToZLevel(elevatorEntity.Value, zLevelUid);

        UpdateControllerUi(entity);
    }

    /// <summary>
    ///     Pushes the floor list and the elevator's current position to whoever has the console open.
    /// </summary>
    private void UpdateControllerUi(Entity<ZLevelElevatorControllerComponent> entity)
    {
        if (!_userInterfaceSystem.IsUiOpen(entity.Owner, ZLevelElevatorControllerUiKey.Key))
            return;

        _floors.Clear();

        if (!TryResolveElevator(entity.Owner, entity.Comp.ShaftId, out var elevatorEntity) ||
            !TryGetElevatorZLevel(elevatorEntity.Value.Owner, out var currentZLevelEntity) ||
            !_zLevelSystem.TryGetStack(currentZLevelEntity.Value.Owner, _stackEntities))
        {
            _userInterfaceSystem.SetUiState(
                entity.Owner,
                ZLevelElevatorControllerUiKey.Key,
                new ZLevelElevatorControllerState([], null, ZLevelElevatorDirection.Idle, moving: false)
            );
            return;
        }

        // The floor list is the stack, because a map is saved standalone and only joins a stack at runtime -
        //      there is nothing an elevator could have been told about its floors when it was drawn.
        for (var index = 0; index < _stackEntities.Count; index++)
        {
            var stackEntity = _stackEntities[index];

            _floors.Add(new ZLevelElevatorFloor(
                GetNetEntity(stackEntity.Owner),
                index + 1,
                elevatorEntity.Value.Comp.CalledZLevels.Contains(stackEntity.Owner),
                IsFootprintObstructed(elevatorEntity.Value.Owner, stackEntity)
            ));
        }

        _userInterfaceSystem.SetUiState(
            entity.Owner,
            ZLevelElevatorControllerUiKey.Key,
            new ZLevelElevatorControllerState(
                [.. _floors],
                GetNetEntity(currentZLevelEntity.Value.Owner),
                elevatorEntity.Value.Comp.Direction,
                HasComp<ActiveZLevelElevatorComponent>(elevatorEntity.Value.Owner)
            )
        );
    }

    /// <summary>
    ///     Whether the elevator would have to cut its way onto this z-level.
    /// </summary>
    /// <remarks>
    ///     Shown rather than acted on: the floor stays selectable, because refusing it would let anyone
    ///         weld a sheet of plating over a shaft to take a floor off the lift's panel. An operator ought
    ///         to know they are about to make a hole, though.
    /// </remarks>
    private bool IsFootprintObstructed(EntityUid elevatorUid, Entity<KsZLevelComponent> zLevelEntity)
    {
        if (!TryComp<MapComponent>(zLevelEntity.Owner, out var mapComponent) ||
            !_fixturesQuery.TryGetComponent(elevatorUid, out var fixturesComponent) ||
            !_physicsQuery.HasComponent(elevatorUid))
            return false;

        // Shrunk to match ClearObstructingTiles, so the panel never calls a floor obstructed that
        //      the elevator would not actually cut into.
        var worldAabb = _physicsSystem.GetWorldAABB(elevatorUid, fixturesComponent).Enlarged(-BorderTolerance);

        _intersectingGrids.Clear();
        _mapSystem.FindGridsIntersecting(
            mapComponent.MapId,
            worldAabb,
            ref _intersectingGrids,
            approx: false,
            includeMap: false
        );

        foreach (var otherGridEntity in _intersectingGrids)
        {
            if (otherGridEntity.Owner == elevatorUid)
                continue;

            var tileEnumerator = _mapSystem.GetTilesIntersecting(
                otherGridEntity.Owner,
                otherGridEntity.Comp,
                worldAabb,
                ignoreEmpty: true
            );

            if (tileEnumerator.MoveNext(out _))
                return true;
        }

        return false;
    }

    #endregion

    #region Signals

    [SubscribeLocalEvent]
    private void OnSignalInit(Entity<ZLevelElevatorSignalComponent> entity, ref ComponentInit args)
    {
        _deviceLinkSystem.EnsureSourcePorts(entity.Owner, entity.Comp.StoppedPort, entity.Comp.MovingPort);
    }

    [SubscribeLocalEvent]
    private void OnElevatorStopped(Entity<ZLevelElevatorComponent> entity, ref ZLevelElevatorStoppedEvent args)
    {
        // Called rather than subscribed separately: only one system may take a given component and event
        //      pair, so everything that reacts to a stop goes through here.
        StopMovementAudio(entity);

        InvokeSignals(entity.Owner, stopped: true);
        RefreshControllers(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnElevatorDeparting(Entity<ZLevelElevatorComponent> entity, ref ZLevelElevatorDepartingEvent args)
    {
        StartMovementAudio(entity);

        InvokeSignals(entity.Owner, stopped: false);
        RefreshControllers(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnElevatorArrived(Entity<ZLevelElevatorComponent> entity, ref ZLevelElevatorArrivedEvent args)
    {
        RefreshControllers(entity.Owner);
    }

    /// <summary>
    ///     Fires the stop or departure port on every emitter bound to this elevator.
    /// </summary>
    /// <remarks>
    ///     Emitters are found by asking each of them which elevator it means, rather than kept in a list on
    ///         the elevator: an emitter riding the lift resolves by proximity, and its answer changes every
    ///         time the lift moves, so a list would go stale on the first trip.
    /// </remarks>
    private void InvokeSignals(EntityUid elevatorUid, bool stopped)
    {
        var enumerator = EntityQueryEnumerator<ZLevelElevatorSignalComponent>();
        while (enumerator.MoveNext(out var uid, out var signalComponent))
        {
            if (!TryResolveElevator(uid, signalComponent.ShaftId, out var resolvedEntity) ||
                resolvedEntity.Value.Owner != elevatorUid)
                continue;

            _deviceLinkSystem.InvokePort(uid, stopped ? signalComponent.StoppedPort : signalComponent.MovingPort);
        }
    }

    private void RefreshControllers(EntityUid elevatorUid)
    {
        var enumerator = EntityQueryEnumerator<ZLevelElevatorControllerComponent>();
        while (enumerator.MoveNext(out var uid, out var controllerComponent))
        {
            if (!TryResolveElevator(uid, controllerComponent.ShaftId, out var resolvedEntity) ||
                resolvedEntity.Value.Owner != elevatorUid)
                continue;

            UpdateControllerUi((uid, controllerComponent));
        }
    }

    #endregion
}
