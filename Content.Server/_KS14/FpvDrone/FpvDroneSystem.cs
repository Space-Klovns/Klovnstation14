// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Server.SurveillanceCamera;
using Content.Shared._KS14.FpvDrone;
using Content.Shared._KS14.RemoteDrone;

namespace Content.Server._KS14.FpvDrone;

public sealed class FpvDroneSystem : SharedFpvDroneSystem
{
    [Dependency] private readonly SurveillanceCameraMonitorSystem _surveillanceMonitorSystem = default!;

    protected override void UpdateFpvSurveillance(Entity<FpvDroneComponent> entity)
    {
        base.UpdateFpvSurveillance(entity);
        if (!TryComp<RemoteDroneComponent>(entity.Owner, out var remoteDroneComponent))
            return;

        if (remoteDroneComponent.LinkedControllerUid is not { } controllerUid ||
            !TryComp<SurveillanceCameraMonitorComponent>(controllerUid, out var controllerSurveillanceMonitorComponent))
        {
            return;
        }

        _surveillanceMonitorSystem.TrySwitchCameraByUid(controllerUid, entity.Owner, monitor: controllerSurveillanceMonitorComponent);
        _surveillanceMonitorSystem.UpdateUserInterface(controllerUid, controllerSurveillanceMonitorComponent);
    }

    protected override void OnDroneDisabled(EntityUid uid)
    {
        base.OnDroneDisabled(uid);
        _surveillanceMonitorSystem.DisconnectCamera(uid, true);
    }
}
