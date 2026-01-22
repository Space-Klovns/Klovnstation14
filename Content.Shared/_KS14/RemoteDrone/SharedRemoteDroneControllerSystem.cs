using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.SurveillanceCamera;
using Content.Shared.UserInterface;
using Robust.Shared.Utility;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.RemoteDrone;

public abstract class SharedRemoteDroneControllerSystem : EntitySystem
{
    [Dependency] private readonly SharedPowerReceiverSystem _powerReceiverSystem = default!;
    [Dependency] private readonly SharedDeviceLinkSystem _sharedDeviceLinkSystem = default!;

    private EntityQuery<RemoteDroneComponent> _droneQuery;

    public override void Initialize()
    {
        base.Initialize();
        _droneQuery = GetEntityQuery<RemoteDroneComponent>();

        //// Ports
        // Drones themselves aren't subscribed to port events, only controllers, to avoid confusion
        SubscribeLocalEvent<RemoteDroneControllerComponent, LinkAttemptEvent>(OnControllerLinkAttempt);
        SubscribeLocalEvent<RemoteDroneControllerComponent, NewLinkEvent>(OnControllerLinked);
        SubscribeLocalEvent<RemoteDroneControllerComponent, PortDisconnectedEvent>(OnControllerUnlinked);

        //// Startup
        SubscribeLocalEvent<RemoteDroneControllerComponent, ComponentStartup>(OnControllerStartup);
        SubscribeLocalEvent<RemoteDroneComponent, ComponentStartup>(OnDroneStartup);

        //// Shutdown
        SubscribeLocalEvent<RemoteDroneControllerComponent, ComponentShutdown>(OnControllerShutdown);
        SubscribeLocalEvent<RemoteDroneComponent, ComponentShutdown>(OnDroneShutdown);

        //// UI

        SubscribeLocalEvent<RemoteDroneControllerComponent, ActivatableUIOpenAttemptEvent>(OnInterfaceOpenedAttempt);
        SubscribeLocalEvent<RemoteDroneControllerComponent, AfterActivatableUIOpenEvent>(OnInterfaceOpened);
        Subs.BuiEvents<RemoteDroneControllerComponent>(SurveillanceCameraMonitorUiKey.Key, subs =>
        {
            subs.Event<BoundUIClosedEvent>(OnInterfaceClosed);
        });
    }

    #region Events

    private void OnInterfaceOpenedAttempt(Entity<RemoteDroneControllerComponent> entity, ref ActivatableUIOpenAttemptEvent args)
    {
        if (entity.Comp.LinkedDroneUid != null)
            return;

        args.Cancel();
    }

    private void OnInterfaceOpened(Entity<RemoteDroneControllerComponent> entity, ref AfterActivatableUIOpenEvent args)
    {
        TryStartControlling(entity, args.User);
    }

    private void OnInterfaceClosed(Entity<RemoteDroneControllerComponent> entity, ref BoundUIClosedEvent args)
    {
        if (args.Actor != entity.Comp.UserUid)
            return;

        if (!TryComp<ActivatableUIComponent>(entity, out var activatableUiComponent) ||
            !args.UiKey.Equals(activatableUiComponent.Key))
            return;

        TryStopControlling(entity);
    }

    // this might break if this event becomes pure
    private void OnControllerLinkAttempt(Entity<RemoteDroneControllerComponent> entity, ref LinkAttemptEvent args)
    {
        if (args.SourcePort != entity.Comp.SourcePort.ToString())
            return;

        if (!_droneQuery.TryGetComponent(args.Sink, out var droneComponent))
        {
            Log.Error($"Tried to link remote drone controller `{ToPrettyString(entity.Owner)}` to entity that doesn't have RemoteDroneComponent `{ToPrettyString(args.Sink)}`.");
            DebugTools.Assert($"Tried to link remote drone controller `{ToPrettyString(entity.Owner)}` to entity that doesn't have RemoteDroneComponent `{ToPrettyString(args.Sink)}`.");

            return;
        }

        droneComponent.LinkedControllerUid = args.Source;
        Dirty(args.Sink, droneComponent);
    }

    private void OnControllerLinked(Entity<RemoteDroneControllerComponent> entity, ref NewLinkEvent args)
    {
        if (args.SourcePort != entity.Comp.SourcePort.ToString())
            return;

        // remove earlier link if it exists
        if (entity.Comp.LinkedDroneUid is { } alreadyLinkedDroneUid)
            _sharedDeviceLinkSystem.RemoveSinkFromSource(entity.Owner, alreadyLinkedDroneUid);

        entity.Comp.LinkedDroneUid = args.Sink;

        var linkEvent = new RemoteDroneLinkedEvent(entity, args.Sink);
        RaiseLocalEvent(entity, ref linkEvent);
        RaiseLocalEvent(args.Sink, ref linkEvent);

        Dirty(entity);
    }

    private void OnControllerUnlinked(Entity<RemoteDroneControllerComponent> entity, ref PortDisconnectedEvent args)
    {
        if (args.Port != entity.Comp.SourcePort.ToString())
            return;

        TryHandleUnlink(entity, args.Sink);
    }

    private void TryHandleUnlink(Entity<RemoteDroneControllerComponent> controllerEntity, EntityUid droneUid)
    {
        if (_droneQuery.TryGetComponent(droneUid, out var droneComponent))
        {
            droneComponent.LinkedControllerUid = null;
            Dirty(controllerEntity);
        }
        else
        {
            DebugTools.Assert($"Tried to unlink remote drone controller `{ToPrettyString(controllerEntity.Owner)}` from entity that doesn't have RemoteDroneComponent `{ToPrettyString(droneUid)}`.");
            Log.Error($"Tried to unlink remote drone controller `{ToPrettyString(controllerEntity.Owner)}` from entity that doesn't have RemoteDroneComponent `{ToPrettyString(droneUid)}`.");
            return;
        }

        var unlinkEvent = new RemoteDroneUnlinkedEvent(controllerEntity, droneUid);
        RaiseLocalEvent(controllerEntity, ref unlinkEvent);
        RaiseLocalEvent(droneUid, ref unlinkEvent);

        TryStopControlling(controllerEntity);
        controllerEntity.Comp.LinkedDroneUid = null;
    }

    private void OnControllerStartup(Entity<RemoteDroneControllerComponent> entity, ref ComponentStartup args)
    {
        _sharedDeviceLinkSystem.EnsureSourcePorts(entity, entity.Comp.SourcePort);
    }

    private void OnDroneStartup(Entity<RemoteDroneComponent> entity, ref ComponentStartup args)
    {
        _sharedDeviceLinkSystem.EnsureSinkPorts(entity, entity.Comp.SinkPort);
    }

    private void OnControllerShutdown(Entity<RemoteDroneControllerComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.LinkedDroneUid is not { } linkedDroneUid)
            return;

        TryHandleUnlink(entity, linkedDroneUid);
    }

    private void OnDroneShutdown(Entity<RemoteDroneComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.LinkedControllerUid is not { } linkedControllerUid)
            return;

        if (TryComp<RemoteDroneControllerComponent>(linkedControllerUid, out var controllerComp))
            TryHandleUnlink((linkedControllerUid, controllerComp), entity);
        else
        {
            DebugTools.Assert($"Tried to unlink remote drone `{ToPrettyString(entity.Owner)}` from entity that doesn't have RemoteDroneControllerComponent `{ToPrettyString(linkedControllerUid)}`.");
            Log.Error($"Tried to unlink remote drone `{ToPrettyString(entity.Owner)}` from entity that doesn't have RemoteDroneControllerComponent `{ToPrettyString(linkedControllerUid)}`.");
        }
    }

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ResolveDroneLink(in Entity<RemoteDroneControllerComponent> controllerEntity, [NotNullWhen(true)] out EntityUid? linkedDroneUid)
    {
        if (controllerEntity.Comp.LinkedDroneUid is not { } lDU)
        {
            DebugTools.Assert($"Tried to resolve a drone link for controller `{ToPrettyString(controllerEntity.Owner)}` that has no linked drone.");

            linkedDroneUid = null;
            return false;
        }

        linkedDroneUid = lDU;
        return true;
    }

    /// <summary>
    ///     Called right before raising control-starting events.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void BeforeStartingControl(Entity<RemoteDroneControllerComponent> controllerEntity, EntityUid userUid) { }

    /// <summary>
    ///     Assumes the controller is currently linked to a drone. Calls necessary
    ///         before-control-changed methods before raising events. Will dirty the controller entity.
    /// </summary>
    /// <returns>Whether there was success.</returns>
    protected bool TryStartControlling(Entity<RemoteDroneControllerComponent> controllerEntity, EntityUid userUid)
    {
        if (controllerEntity.Comp.Controlling)
            return false;

        if (!ResolveDroneLink(controllerEntity, out var droneUid))
            return false;

        controllerEntity.Comp.Controlling = true;
        controllerEntity.Comp.UserUid = userUid;

        BeforeStartingControl(controllerEntity, userUid);

        var controlEvent = new RemoteDroneControlStartedEvent(controllerEntity, droneUid.Value);
        RaiseLocalEvent(controllerEntity, ref controlEvent);
        RaiseLocalEvent(droneUid.Value, ref controlEvent);

        Dirty(controllerEntity);
        return true;
    }

    // If you change this in any way make sure to change description for RemoteDroneControlEndedEvent and change the other instance of this notice if necessary
    /// <summary>
    ///     Called right after raising control-ending events.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void AfterEndingControl(Entity<RemoteDroneControllerComponent> controllerEntity) { }

    /// <inheritdoc cref="TryStartControlling(Entity{RemoteDroneControllerComponent}, EntityUid)"/>
    protected bool TryStopControlling(Entity<RemoteDroneControllerComponent> controllerEntity)
    {
        if (!controllerEntity.Comp.Controlling)
            return false;

        if (!ResolveDroneLink(controllerEntity, out var droneUid))
            return false;

        var controlEvent = new RemoteDroneControlEndedEvent(controllerEntity, droneUid.Value);
        RaiseLocalEvent(controllerEntity, ref controlEvent);
        RaiseLocalEvent(droneUid.Value, ref controlEvent);

        AfterEndingControl(controllerEntity);

        controllerEntity.Comp.Controlling = false;
        controllerEntity.Comp.UserUid = null;

        Dirty(controllerEntity);
        return true;
    }

    protected bool IsApcOrBatteryPowered(EntityUid uid)
    {
        if (TryComp<BatteryComponent>(uid, out var batteryComponent) &&
            batteryComponent.State == BatteryState.Empty)
            return false;

        return _powerReceiverSystem.IsPowered(uid);
    }
}
