// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Popups;
using Content.Shared.SurveillanceCamera;
using Content.Shared.UserInterface;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.RemoteDrone;

public sealed class RemoteDroneSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedDeviceLinkSystem _sharedDeviceLinkSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;

    private EntityQuery<RemoteDroneComponent> _droneQuery;

    public override void Initialize()
    {
        base.Initialize();
        _droneQuery = GetEntityQuery<RemoteDroneComponent>();

        //// Ports
        SubscribeLocalEvent<RemoteDroneComponent, LinkAttemptEvent>(OnDroneLinkAttempt);
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
        if (entity.Comp.Controlling)
        {
            args.Cancel();
            if (!args.Silent)
                _popupSystem.PopupClient(Loc.GetString("remote-drone-controller-already-in-use"), entity.Owner, args.User);

            return;
        }

        if (entity.Comp.LinkedDroneUid is not { } droneUid)
        {
            args.Cancel();
            if (!args.Silent)
                _popupSystem.PopupClient(Loc.GetString("remote-drone-controller-no-linked-drone"), entity.Owner, args.User);

            return;
        }

        if (AttemptControlDroneWasCancelled(entity, droneUid, args.User))
        {
            args.Cancel();
            if (!args.Silent)
                _popupSystem.PopupClient(Loc.GetString("remote-drone-controller-bad-connection"), entity.Owner, args.User, PopupType.SmallCaution);

            return;
        }
    }

    private void OnInterfaceOpened(Entity<RemoteDroneControllerComponent> entity, ref AfterActivatableUIOpenEvent args)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return;

        TryStartControlling(entity, args.User);
    }

    private void OnInterfaceClosed(Entity<RemoteDroneControllerComponent> entity, ref BoundUIClosedEvent args)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return;

        if (args.Actor != entity.Comp.UserUid)
            return;

        if (!TryComp<ActivatableUIComponent>(entity, out var activatableUiComponent) ||
            !args.UiKey.Equals(activatableUiComponent.Key))
            return;

        TryStopControlling(entity);
    }

    // Drones can only link to drone controllers
    private void OnDroneLinkAttempt(Entity<RemoteDroneComponent> entity, ref LinkAttemptEvent args)
    {
        if (args.SinkPort != entity.Comp.SinkPort.ToString())
            return;

        if (HasComp<RemoteDroneControllerComponent>(args.Source))
            return;

        args.Cancel();
    }

    // this might break if this event becomes pure
    private void OnControllerLinkAttempt(Entity<RemoteDroneControllerComponent> entity, ref LinkAttemptEvent args)
    {
        if (args.SourcePort != entity.Comp.SourcePort.ToString())
            return;

        if (!_droneQuery.TryGetComponent(args.Sink, out var droneComponent))
        {
            args.Cancel();
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

    /// <returns>Whether RemoteDroneAttemptControlEvent was cancelled.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AttemptControlDroneWasCancelled(Entity<RemoteDroneControllerComponent> controllerEntity, EntityUid droneUid, EntityUid userUid)
    {
        var attemptEvent = new RemoteDroneAttemptControlEvent(controllerEntity, droneUid, userUid);
        RaiseLocalEvent(controllerEntity, ref attemptEvent);
        RaiseLocalEvent(droneUid, ref attemptEvent);

        return attemptEvent.Cancelled;
    }

    /// <summary>
    ///     Assumes the controller is currently linked to a drone. Calls necessary
    ///         before-control-changed methods before raising events. Will dirty the controller entity.
    /// </summary>
    /// <returns>Whether there was success.</returns>
    public bool TryStartControlling(Entity<RemoteDroneControllerComponent> controllerEntity, EntityUid userUid)
    {
        if (controllerEntity.Comp.Controlling)
            return false;

        if (!ResolveDroneLink(controllerEntity, out var droneUid))
            return false;

        if (AttemptControlDroneWasCancelled(controllerEntity, droneUid.Value, userUid))
        {
            _popupSystem.PopupClient(Loc.GetString("remote-drone-controller-bad-connection"), controllerEntity.Owner, userUid, PopupType.SmallCaution);
            return false;
        }

        controllerEntity.Comp.Controlling = true;
        controllerEntity.Comp.UserUid = userUid;

        var controlEvent = new RemoteDroneControlStartedEvent(controllerEntity, droneUid.Value);
        RaiseLocalEvent(controllerEntity, ref controlEvent);
        RaiseLocalEvent(droneUid.Value, ref controlEvent);

        Dirty(controllerEntity);
        return true;
    }

    /// <inheritdoc cref="TryStartControlling(Entity{RemoteDroneControllerComponent}, EntityUid)"/>
    public bool TryStopControlling(Entity<RemoteDroneControllerComponent> controllerEntity)
    {
        if (!controllerEntity.Comp.Controlling)
            return false;

        if (!ResolveDroneLink(controllerEntity, out var droneUid))
            return false;

        var controlEvent = new RemoteDroneControlEndedEvent(controllerEntity, droneUid.Value);
        RaiseLocalEvent(controllerEntity, ref controlEvent);
        RaiseLocalEvent(droneUid.Value, ref controlEvent);

        controllerEntity.Comp.Controlling = false;
        controllerEntity.Comp.UserUid = null;

        Dirty(controllerEntity);
        return true;
    }

    /// <summary>
    ///    Resolves the drone controller entity for a given drone UID.
    /// </summary>
    /// <returns>Whether both drone component and controller entity were successfully resolved.</returns>
    public bool ResolveDroneAndController(EntityUid droneUid, [MaybeNullWhen(false)] out RemoteDroneComponent droneComponent, [NotNullWhen(true)] out Entity<RemoteDroneControllerComponent>? controllerEntity)
    {
        if (!_droneQuery.TryGetComponent(droneUid, out droneComponent) ||
            droneComponent.LinkedControllerUid is not { } lCU ||
            !TryComp<RemoteDroneControllerComponent>(lCU, out var droneControllerComponent))
        {
            controllerEntity = null;
            return false;
        }

        controllerEntity = (lCU, droneControllerComponent);
        return true;
    }
}
