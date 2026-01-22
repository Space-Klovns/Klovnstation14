using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Utility;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.RemoteDrone;

public abstract class SharedRemoteDroneControllerSystem : EntitySystem
{
    [Dependency] private readonly SharedPowerReceiverSystem _powerReceiverSystem = default!;
    [Dependency] private readonly SharedMoverController _moverController = default!;
    [Dependency] private readonly SharedDeviceLinkSystem _sharedDeviceLinkSystem = default!;

    private EntityQuery<RemoteDroneComponent> _droneQuery;

    public override void Initialize()
    {
        base.Initialize();
        _droneQuery = GetEntityQuery<RemoteDroneComponent>();

        SubscribeLocalEvent<RemoteDroneControllerComponent, ActivateInWorldEvent>(OnControllerActivate);

        //// Ports
        // Drones themselves aren't subscribed to port events, only controllers, to avoid confusion
        SubscribeLocalEvent<RemoteDroneControllerComponent, NewLinkEvent>(OnControllerLinked);
        SubscribeLocalEvent<RemoteDroneControllerComponent, PortDisconnectedEvent>(OnControllerUnlinked);

        //// Startup
        SubscribeLocalEvent<RemoteDroneControllerComponent, ComponentStartup>(OnControllerStartup);
        SubscribeLocalEvent<RemoteDroneComponent, ComponentStartup>(OnDroneStartup);

        //// Shutdown
        SubscribeLocalEvent<RemoteDroneControllerComponent, ComponentShutdown>(OnControllerShutdown);
        SubscribeLocalEvent<RemoteDroneComponent, ComponentShutdown>(OnDroneShutdown);
    }

    #region Events

    private void OnControllerActivate(Entity<RemoteDroneControllerComponent> entity, ref ActivateInWorldEvent args)
    {
        if (!args.Complex || entity.Comp.LinkedDroneUid == null)
            return;

        if (!TryStartControlling(entity, args.User))
            return;

        args.Handled = true;
    }

    private void OnControllerLinked(Entity<RemoteDroneControllerComponent> entity, ref NewLinkEvent args)
    {
        if (args.SourcePort != entity.Comp.SourcePort.ToString())
            return;

        if (_droneQuery.TryGetComponent(args.Sink, out var droneComponent))
        {
            droneComponent.LinkedControllerUid = args.Source;
            Dirty(args.Sink, droneComponent);
        }
        else
        {
            Log.Error($"Tried to link remote drone controller `{ToPrettyString(entity.Owner)}` to entity that doesn't have RemoteDroneComponent `{ToPrettyString(args.Sink)}`.");
            DebugTools.Assert($"Tried to link remote drone controller `{ToPrettyString(entity.Owner)}` to entity that doesn't have RemoteDroneComponent `{ToPrettyString(args.Sink)}`.");
            return;
        }

        entity.Comp.LinkedDroneUid = args.Sink;
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
            Log.Error($"Tried to unlink remote drone controller `{ToPrettyString(controllerEntity.Owner)}` from entity that doesn't have RemoteDroneComponent `{ToPrettyString(droneUid)}`.");
            DebugTools.Assert($"Tried to unlink remote drone controller `{ToPrettyString(controllerEntity.Owner)}` from entity that doesn't have RemoteDroneComponent `{ToPrettyString(droneUid)}`.");
            return;
        }

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
            Log.Error($"Tried to unlink remote drone `{ToPrettyString(entity.Owner)}` from entity that doesn't have RemoteDroneControllerComponent `{ToPrettyString(linkedControllerUid)}`.");
            DebugTools.Assert($"Tried to unlink remote drone `{ToPrettyString(entity.Owner)}` from entity that doesn't have RemoteDroneControllerComponent `{ToPrettyString(linkedControllerUid)}`.");
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

        _moverController.SetRelay(controllerEntity.Owner, droneUid.Value);
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

        controllerEntity.Comp.Controlling = false;

        // Clean up relay components
        RemCompDeferred<RelayInputMoverComponent>(controllerEntity);
        RemCompDeferred<MovementRelayTargetComponent>(droneUid.Value);

        var controlEvent = new RemoteDroneControlEndedEvent(controllerEntity, droneUid.Value);
        RaiseLocalEvent(controllerEntity, ref controlEvent);
        RaiseLocalEvent(droneUid.Value, ref controlEvent);

        AfterEndingControl(controllerEntity);

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
