using Content.Server.SurveillanceCamera;
using Content.Shared._KS14.FpvDrone;
using Content.Shared._KS14.RemoteDrone;
using Content.Shared.DeviceNetwork.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._KS14.FpvDrone;

public sealed class FpvDroneSystem : SharedFpvDroneSystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly SurveillanceCameraMonitorSystem _surveillanceMonitorSystem = default!;

    private static TimeSpan _nextUpdate = TimeSpan.MinValue;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1.5);

    // TODO LCDC: Something less horrible than this
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameTiming.CurTime < _nextUpdate)
            return;

        _nextUpdate = _gameTiming.CurTime + UpdateInterval;

        var fpvEqe = EntityQueryEnumerator<FpvDroneComponent, RemoteDroneComponent, DeviceNetworkComponent>();
        while (fpvEqe.MoveNext(out var droneUid, out _, out var remoteDroneComponent, out var deviceNetworkComponent))
        {
            if (remoteDroneComponent.LinkedControllerUid is not { } controllerUid ||
                !TryComp<SurveillanceCameraMonitorComponent>(controllerUid, out var controllerSurveillanceMonitorComponent))
            {
                continue;
            }

            if (controllerSurveillanceMonitorComponent.KnownMobileCameras.ContainsKey(deviceNetworkComponent.Address))
                continue;

            _surveillanceMonitorSystem.KsAddMobileCamera(
                droneUid,
                controllerSurveillanceMonitorComponent,
                deviceNetworkComponent.Address,
                Name(droneUid),
                GetNetEntity(droneUid),
                GetNetCoordinates(_transformSystem.ToCoordinates(droneUid, _transformSystem.ToMapCoordinates(Transform(droneUid).Coordinates)))
            );
        }
    }
}
