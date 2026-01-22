using Content.Shared._KS14.RemoteDrone;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.FpvDrone;

public abstract class SharedFpvDroneSystem : EntitySystem
{
    [Dependency] private readonly SharedMoverController _moverController = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FpvDroneComponent, RemoteDroneControlStartedEvent>(OnFpvControlStarted);
        SubscribeLocalEvent<FpvDroneComponent, RemoteDroneControlEndedEvent>(OnFpvControlEnded);
    }

    private void OnFpvControlStarted(Entity<FpvDroneComponent> entity, ref RemoteDroneControlStartedEvent args)
    {
        if (!TryComp<PhysicsComponent>(entity, out var physicsComponent))
        {
            DebugTools.Assert($"Tried to handle RemoteDroneControlStartedEvent for FpvDroneComponent on an entity {ToPrettyString(entity.Owner)} without PhysicsComponent.");
            Log.Error($"Tried to handle RemoteDroneControlStartedEvent for FpvDroneComponent on an entity {ToPrettyString(entity.Owner)} without PhysicsComponent.");
            return;
        }

        _physicsSystem.SetBodyStatus(entity.Owner, physicsComponent, BodyStatus.InAir);
        _moverController.SetRelay(args.ControllerEntity.Comp.UserUid!.Value, entity.Owner);
    }

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
    }
}
