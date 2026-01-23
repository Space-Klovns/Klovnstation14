// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared._KS14.RemoteDrone;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Item;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.FpvDrone;

/// <summary>
///     Assumes that FPV drone's surveillance monitor components have NeverAutomaticallyHeartbeat set to true.
/// </summary>
public abstract class SharedFpvDroneSystem : EntitySystem
{
    [Dependency] private readonly SharedDeviceLinkSystem _deviceLinkSystem = default!;
    [Dependency] private readonly SharedMoverController _moverController = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly SharedHandsSystem _handsSystem = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly PowerCellSystem _powerCellSystem = default!;
    [Dependency] private readonly RemoteDroneSystem _droneControllerSystem = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly INetManager _netManager = default!;

    /// <summary>
    ///     Minimum charge for an FPV to work.
    /// </summary>
    public const float ChargeThreshold = 0.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FpvDroneComponent, MapInitEvent>(OnFpvMapInit);
        SubscribeLocalEvent<FpvDroneComponent, SignalReceivedEvent>(OnFpvSignalReceived);

        SubscribeLocalEvent<FpvDroneComponent, RemoteDroneLinkedEvent>(OnFpvLinked);
        SubscribeLocalEvent<FpvDroneComponent, RemoteDroneUnlinkedEvent>(OnFpvUnlinked);
        SubscribeLocalEvent<FpvDroneComponent, PowerCellChangedEvent>(OnFpvCellChanged);
        SubscribeLocalEvent<FpvDroneComponent, BatteryStateChangedEvent>(OnFpvBatteryStateChanged);

        SubscribeLocalEvent<FpvDroneComponent, PowerCellSlotEmptyEvent>(OnFpvPowerCellEmpty);
        SubscribeLocalEvent<FpvDroneComponent, GettingPickedUpAttemptEvent>(OnFpvAttemptPickup);

        SubscribeLocalEvent<FpvDroneComponent, RemoteDroneAttemptControlEvent>(OnFpvAttemptControl);
        SubscribeLocalEvent<FpvDroneComponent, RemoteDroneControlStartedEvent>(OnFpvControlStarted);
        SubscribeLocalEvent<FpvDroneComponent, RemoteDroneControlEndedEvent>(OnFpvControlEnded);
    }

    private void OnFpvMapInit(Entity<FpvDroneComponent> entity, ref MapInitEvent args)
    {
        _deviceLinkSystem.EnsureSinkPorts(entity.Owner, entity.Comp.DropStoragePort);
    }

    private void OnFpvSignalReceived(Entity<FpvDroneComponent> entity, ref SignalReceivedEvent args)
    {
        if (args.Port != entity.Comp.DropStoragePort.ToString())
            return;

        if (!_containerSystem.TryGetContainer(entity.Owner, entity.Comp.EmptiedContainerId, out var container))
            return;

        var removedEntities = _containerSystem.EmptyContainer(container, force: true);
        if (removedEntities.Count != 0)
            _popupSystem.PopupEntity(Loc.GetString("fpv-drone-payload-dropped", ("name", Identity.Name(entity.Owner, EntityManager))), entity.Owner, PopupType.MediumCaution);
    }

    private void OnFpvLinked(Entity<FpvDroneComponent> entity, ref RemoteDroneLinkedEvent args)
    {
        var fpvControllerComponent = EntityManager.ComponentFactory.GetComponent<FpvDroneControllerComponent>();
        fpvControllerComponent.HasSufficientCharge = _powerCellSystem.HasCharge(entity.Owner, ChargeThreshold);
        AddComp(args.ControllerEntity, fpvControllerComponent);
    }

    private void OnFpvUnlinked(Entity<FpvDroneComponent> entity, ref RemoteDroneUnlinkedEvent args)
    {
        RemComp<FpvDroneControllerComponent>(args.ControllerEntity);
    }

    private void OnFpvCellChanged(Entity<FpvDroneComponent> entity, ref PowerCellChangedEvent args)
    {
        if (args.Ejected)
            TryUpdateFpvChargeState(entity, false);
        else
            TryUpdateFpvChargeState(entity, _powerCellSystem.HasCharge(entity.Owner, ChargeThreshold));
    }

    private void OnFpvBatteryStateChanged(Entity<FpvDroneComponent> entity, ref BatteryStateChangedEvent args)
    {
        TryUpdateFpvChargeState(entity, args.NewState != BatteryState.Empty);
    }

    private void TryUpdateFpvChargeState(EntityUid droneUid, bool hasSufficientCharge)
    {
        if (!_droneControllerSystem.ResolveDroneAndController(droneUid, out _, out var controllerEntity))
            return;

        if (!TryComp<FpvDroneControllerComponent>(controllerEntity, out var fpvControllerComponent))
        {
            DebugTools.Assert($"FPV drone's controller didnt have FpvDroneControllerComponent.");
            return;
        }

        fpvControllerComponent.HasSufficientCharge = hasSufficientCharge;
        Dirty(controllerEntity.Value.Owner, fpvControllerComponent);
    }

    private void OnFpvPowerCellEmpty(Entity<FpvDroneComponent> entity, ref PowerCellSlotEmptyEvent args)
    {
        if (!_droneControllerSystem.ResolveDroneAndController(entity.Owner, out _, out var controllerEntity) ||
            !controllerEntity.Value.Comp.Controlling)
            return;

        _droneControllerSystem.TryStopControlling(controllerEntity.Value);
    }

    private void OnFpvAttemptPickup(Entity<FpvDroneComponent> entity, ref GettingPickedUpAttemptEvent args)
    {
        if (!_droneControllerSystem.ResolveDroneAndController(entity.Owner, out _, out var controllerEntity) ||
            !controllerEntity.Value.Comp.Controlling)
            return;

        // Only cancel if the drone is currently being controlled.
        args.Cancel();
    }

    private void OnFpvAttemptControl(Entity<FpvDroneComponent> entity, ref RemoteDroneAttemptControlEvent args)
    {
        if (!TryComp<FpvDroneControllerComponent>(args.ControllerEntity, out var fpvControllerComponent))
        {
            DebugTools.Assert($"FPV drone's controller didnt have FpvDroneControllerComponent.");
            args.Cancelled = true;
            return;
        }

        args.Cancelled |= !fpvControllerComponent.HasSufficientCharge;
    }

    private void OnFpvControlStarted(Entity<FpvDroneComponent> entity, ref RemoteDroneControlStartedEvent args)
    {
        if (!TryComp<PhysicsComponent>(entity, out var physicsComponent))
        {
            DebugTools.Assert($"Tried to handle RemoteDroneControlStartedEvent for FpvDroneComponent on an entity {ToPrettyString(entity.Owner)} without PhysicsComponent.");
            Log.Error($"Tried to handle RemoteDroneControlStartedEvent for FpvDroneComponent on an entity {ToPrettyString(entity.Owner)} without PhysicsComponent.");
            return;
        }

        // if the drone is being held in any hand then try to drop it
        if (_containerSystem.TryGetContainingContainer(entity.Owner, out var container) &&
            TryComp<HandsComponent>(container.Owner, out var handsComponent))
        {
            foreach (var handId in _handsSystem.EnumerateHands((container.Owner, handsComponent)))
            {
                var heldItem = _handsSystem.GetHeldItem((container.Owner, handsComponent), handId);
                if (heldItem != entity.Owner)
                    continue;

                _handsSystem.TryDrop(container.Owner, handId, checkActionBlocker: false);
            }
        }

        _physicsSystem.SetBodyStatus(entity.Owner, physicsComponent, BodyStatus.InAir);
        _moverController.SetRelay(args.ControllerEntity.Comp.UserUid!.Value, entity.Owner);

        if (_netManager.IsServer)
            entity.Comp.AudioUid ??= _audioSystem.PlayPvs(entity.Comp.AudioSpecifier, entity.Owner)?.Entity;

        if (TryComp<FlyBySoundComponent>(entity.Owner, out var flyBySoundComponent))
        {
            flyBySoundComponent.Prob = entity.Comp.FlybySoundProbability;
            Dirty(entity.Owner, flyBySoundComponent);
        }

        _powerCellSystem.SetDrawEnabled(entity.Owner, true);
        if (TryComp<PowerCellSlotComponent>(entity.Owner, out var powerCellSlotComponent))
            _itemSlotsSystem.SetLock(entity.Owner, powerCellSlotComponent.CellSlotId, true);

        _appearanceSystem.SetData(entity.Owner, FpvDroneVisuals.Active, true);
        DoHeartbeat(args.ControllerEntity.Owner);
        UpdateFpvSurveillance(entity);
    }

    // Does nothing on client
    protected virtual void UpdateFpvSurveillance(Entity<FpvDroneComponent> entity) { }

    // Does nothing on client
    protected virtual void DoHeartbeat(EntityUid uid) { }

    private void OnFpvControlEnded(Entity<FpvDroneComponent> entity, ref RemoteDroneControlEndedEvent args)
    {
        if (!TryComp<PhysicsComponent>(entity, out var physicsComponent))
        {
            DebugTools.Assert($"Tried to handle RemoteDroneControlEndedEvent for FpvDroneComponent on an entity {ToPrettyString(entity.Owner)} without PhysicsComponent.");
            Log.Error($"Tried to handle RemoteDroneControlEndedEvent for FpvDroneComponent on an entity {ToPrettyString(entity.Owner)} without PhysicsComponent.");
            return;
        }

        _physicsSystem.SetBodyStatus(entity.Owner, physicsComponent, BodyStatus.OnGround);

        // Clean up relay components
        if (args.ControllerEntity.Comp.UserUid is { } userUid &&
            !Deleted(userUid))
            RemCompDeferred<RelayInputMoverComponent>(args.ControllerEntity.Comp.UserUid!.Value);

        RemCompDeferred<MovementRelayTargetComponent>(entity.Owner);

        QueueDel(entity.Comp.AudioUid);
        entity.Comp.AudioUid = null;

        if (TryComp<FlyBySoundComponent>(entity.Owner, out var flyBySoundComponent))
        {
            flyBySoundComponent.Prob = 0f;
            Dirty(entity.Owner, flyBySoundComponent);
        }

        _powerCellSystem.SetDrawEnabled(entity.Owner, false);
        if (TryComp<PowerCellSlotComponent>(entity.Owner, out var powerCellSlotComponent))
            _itemSlotsSystem.SetLock(entity.Owner, powerCellSlotComponent.CellSlotId, false);

        _appearanceSystem.SetData(entity.Owner, FpvDroneVisuals.Active, false);
        DoHeartbeat(args.ControllerEntity.Owner);
    }
}
