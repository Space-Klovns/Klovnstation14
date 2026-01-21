using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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

    public override void Initialize()
    {
        base.Initialize();
    }

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
    ///         before-control-changed methods before raising events.
    /// </summary>
    /// <returns>Whether there was success.</returns>
    protected virtual bool StartControlling(Entity<RemoteDroneControllerComponent> controllerEntity, EntityUid userUid)
    {
        if (!ResolveDroneLink(controllerEntity, out var droneUid))
            return false;

        _moverController.SetRelay(controllerEntity.Owner, droneUid.Value);
        BeforeStartingControl(controllerEntity, userUid);

        var controlEvent = new RemoteDroneControlStartedEvent(controllerEntity, droneUid.Value);
        RaiseLocalEvent(controllerEntity, ref controlEvent);
        RaiseLocalEvent(droneUid.Value, ref controlEvent);

        return true;
    }

    // If you change this in any way make sure to change description for RemoteDroneControlEndedEvent and change the other instance of this notice if necessary
    /// <summary>
    ///     Called right after raising control-ending events.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void AfterEndingControl(Entity<RemoteDroneControllerComponent> controllerEntity) { }

    /// <inheritdoc cref="StartControlling(Entity{RemoteDroneControllerComponent})"/>
    protected virtual bool StopControlling(Entity<RemoteDroneControllerComponent> controllerEntity)
    {
        if (!ResolveDroneLink(controllerEntity, out var droneUid))
            return false;

        // Clean up relay components
        RemCompDeferred<RelayInputMoverComponent>(controllerEntity);
        RemCompDeferred<MovementRelayTargetComponent>(droneUid.Value);

        var controlEvent = new RemoteDroneControlEndedEvent(controllerEntity, droneUid.Value);
        RaiseLocalEvent(controllerEntity, ref controlEvent);
        RaiseLocalEvent(droneUid.Value, ref controlEvent);

        AfterEndingControl(controllerEntity);

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
