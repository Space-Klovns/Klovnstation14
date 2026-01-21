using Content.Shared._KS14.RemoteDrone;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._KS14.RemoteDrone;

public sealed class RemoteDroneControllerSystem : SharedRemoteDroneControllerSystem
{
    [Dependency] private readonly ActorSystem _actorSystem = default!;
    [Dependency] private readonly ViewSubscriberSystem _viewSubscriberSystem = default!;

    protected override void BeforeStartingControl(Entity<RemoteDroneControllerComponent> controllerEntity, EntityUid userUid)
    {
        base.BeforeStartingControl(controllerEntity, userUid);

        if (!_actorSystem.TryGetSession(userUid, out var userSession))
            return;

        controllerEntity.Comp.UserSession = userSession;
        _viewSubscriberSystem.AddViewSubscriber(controllerEntity.Comp.LinkedDroneUid!.Value, userSession!);
    }

    // If you change this in any way make sure to change description for RemoteDroneControlEndedEvent and change the other instance of this notice if necessary
    protected override void AfterEndingControl(Entity<RemoteDroneControllerComponent> controllerEntity)
    {
        base.AfterEndingControl(controllerEntity);

        if (controllerEntity.Comp.UserSession is not { } userSession)
            return;

        controllerEntity.Comp.UserSession = null;
        _viewSubscriberSystem.RemoveViewSubscriber(controllerEntity.Comp.LinkedDroneUid!.Value, userSession!);
    }

    protected override bool StopControlling(Entity<RemoteDroneControllerComponent> controllerEntity)
    {
        if (!base.StopControlling(controllerEntity))
            return false;

        return true;
    }
}
