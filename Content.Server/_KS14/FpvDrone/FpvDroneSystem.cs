// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Server.SurveillanceCamera;
using Content.Shared._KS14.FpvDrone;
using Content.Shared._KS14.RemoteDrone;
using Content.Shared.DeviceNetwork.Components;
using Robust.Server.GameObjects;

namespace Content.Server._KS14.FpvDrone;

public sealed class FpvDroneSystem : SharedFpvDroneSystem
{
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly SurveillanceCameraMonitorSystem _surveillanceMonitorSystem = default!;

    protected override void UpdateFpvSurveillance(Entity<FpvDroneComponent> entity)
    {
        base.UpdateFpvSurveillance(entity);
        if (!TryComp<RemoteDroneComponent>(entity.Owner, out var remoteDroneComponent) ||
            !TryComp<DeviceNetworkComponent>(entity.Owner, out var deviceNetworkComponent))
            return;

        if (remoteDroneComponent.LinkedControllerUid is not { } controllerUid ||
            !TryComp<SurveillanceCameraMonitorComponent>(controllerUid, out var controllerSurveillanceMonitorComponent))
        {
            return;
        }

        if (controllerSurveillanceMonitorComponent.KnownMobileCameras.ContainsKey(deviceNetworkComponent.Address))
            return;

        _surveillanceMonitorSystem.KsAddMobileCamera(
            entity,
            controllerSurveillanceMonitorComponent,
            deviceNetworkComponent.Address,
            Name(entity.Owner),
            GetNetEntity(entity.Owner),
            GetNetCoordinates(_transformSystem.ToCoordinates(entity.Owner, _transformSystem.ToMapCoordinates(Transform(entity.Owner).Coordinates)))
        );
        _surveillanceMonitorSystem.TrySwitchCameraByUid(controllerUid, entity.Owner, monitor: controllerSurveillanceMonitorComponent);
        _surveillanceMonitorSystem.UpdateUserInterface(controllerUid, controllerSurveillanceMonitorComponent);
    }

    protected override void DoHeartbeat(EntityUid uid)
    {
        base.DoHeartbeat(uid);

        if (TryComp<SurveillanceCameraMonitorComponent>(uid, out var monitorComponent))
            _surveillanceMonitorSystem.InvokeHeartbeat(uid, monitorComponent);
    }
}
