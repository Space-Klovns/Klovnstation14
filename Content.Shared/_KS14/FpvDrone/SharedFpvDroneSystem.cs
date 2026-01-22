using Content.Shared._KS14.RemoteDrone;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.FpvDrone;

public abstract class SharedFpvDroneSystem : EntitySystem
{
    [Dependency] private readonly RemoteDroneControllerSystem _droneControllerSystem = default!;
    [Dependency] private readonly SharedMoverController _moverController = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly SharedHandsSystem _handsSystem = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly INetManager _netManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FpvDroneComponent, GettingPickedUpAttemptEvent>(OnAttemptPickup);

        SubscribeLocalEvent<FpvDroneComponent, RemoteDroneControlStartedEvent>(OnFpvControlStarted);
        SubscribeLocalEvent<FpvDroneComponent, RemoteDroneControlEndedEvent>(OnFpvControlEnded);
    }

    private void OnAttemptPickup(Entity<FpvDroneComponent> entity, ref GettingPickedUpAttemptEvent args)
    {
        if (!_droneControllerSystem.ResolveDroneAndController(entity.Owner, out _, out var controllerEntity))
            return;

        if (!controllerEntity.Value.Comp.Controlling)
            return;

        // Only cancel if the drone is currently being controlled.
        args.Cancel();
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
        if (_containerSystem.TryGetContainingContainer(entity.Owner, out _) &&
            TryComp<HandsComponent>(entity.Owner, out var handsComponent))
        {
            foreach (var handId in _handsSystem.EnumerateHands((entity.Owner, handsComponent)))
            {
                var heldItem = _handsSystem.GetHeldItem((entity.Owner, handsComponent), handId);
                if (heldItem != entity.Owner)
                    continue;

                _handsSystem.TryDrop(entity.Owner, handId, checkActionBlocker: false);
            }
        }

        _physicsSystem.SetBodyStatus(entity.Owner, physicsComponent, BodyStatus.InAir);
        _moverController.SetRelay(args.ControllerEntity.Comp.UserUid!.Value, entity.Owner);

        if (_netManager.IsServer)
            entity.Comp.AudioUid ??= _audioSystem.PlayPvs(entity.Comp.AudioSpecifier, entity.Owner)?.Entity;

        if (TryComp<FlyBySoundComponent>(entity.Owner, out var flyBySoundComponent))
            flyBySoundComponent.Prob = entity.Comp.FlybySoundProbability;
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

        QueueDel(entity.Comp.AudioUid);
        entity.Comp.AudioUid = null;

        if (TryComp<FlyBySoundComponent>(entity.Owner, out var flyBySoundComponent))
            flyBySoundComponent.Prob = 0f;
    }
}
