// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using Content.Shared._KS14.Movement;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._KS14.PhysicsVehicle;

// LCDC TODO: FIX THIS SHIT SOMETIME
/// <summary>
///     Handles vehicles that specifically turn left/right (cant move left/right)
///         and move forward/backward.
/// </summary>
public sealed class PhysicsVehicleSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;

    public override void Initialize()
    {
        base.Initialize();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        SubscribeLocalEvent<PhysicsVehicleComponent, UpdateWishDirEvent>(OnUpdateWishDir);
        SubscribeLocalEvent<PhysicsVehicleComponent, MoveInputEvent>(OnMoveInput);
        //MoveInputEvent
    }

    // Makes sure that the vehicle can't move while turning
    private void OnUpdateWishDir(Entity<PhysicsVehicleComponent> entity, ref UpdateWishDirEvent args)
    {
        if (args.WishDir == Vector2.Zero)
            return;

        if (entity.Comp.TurnDirection != PhysicsVehicleTurnDirection.None)
            args.WishDir = Vector2.Zero;
        else // only move in direction that tank is going
            args.WishDir = Transform(entity).LocalRotation.RoundToCardinalAngle().ToVec();

        // var transformComponent = Transform(entity);
        // var perpendicular = Vector2.Dot(transformComponent.LocalRotation.ToVec(), args.WishDir) < float.Epsilon;

        // if (!perpendicular)
        //     args.WishDir = Vector2.Zero;
    }

    private void OnMoveInput(Entity<PhysicsVehicleComponent> entity, ref MoveInputEvent args)
    {
        Log.Info($"PhysVehicle MoveInput");
        // Turning

        var normalizedMovement = SharedMoverController.GetNormalizedMovement(args.Entity.Comp.HeldMoveButtons);
        if (normalizedMovement.HasFlag(MoveButtons.Left))
            TrySetTurnDirection(entity, PhysicsVehicleTurnDirection.Left);
        else if (normalizedMovement.HasFlag(MoveButtons.Right))
            TrySetTurnDirection(entity, PhysicsVehicleTurnDirection.Right);
        else
            TrySetTurnDirection(entity, PhysicsVehicleTurnDirection.None);
    }

    // Demented and unrealistic but whatever
    private static float TryGetNewAngularVelocity(float currentAngularVelocity, float turnSpeed)
    {
        if (currentAngularVelocity <= 0f)
            return 0f;

        if (currentAngularVelocity <= turnSpeed)
            return 0f;

        return currentAngularVelocity -= turnSpeed;
    }

    private void TrySetTurnDirection(Entity<PhysicsVehicleComponent> entity, PhysicsVehicleTurnDirection turnDirection)
    {
        if (turnDirection == entity.Comp.TurnDirection ||
            !_physicsQuery.TryGetComponent(entity, out var physicsComponent))
            return;

        Log.Info($"Setting turndir: {turnDirection}");

        if (turnDirection == PhysicsVehicleTurnDirection.None)
        {
            if (entity.Comp.TurnDirection == PhysicsVehicleTurnDirection.Right) // was turning right; positive angularvelocity
                _physicsSystem.SetAngularVelocity(entity.Owner, TryGetNewAngularVelocity(physicsComponent.AngularVelocity, entity.Comp.TurnSpeed), body: physicsComponent);
            else // was turning left; negative angularvelocity
                _physicsSystem.SetAngularVelocity(entity.Owner, -TryGetNewAngularVelocity(-physicsComponent.AngularVelocity, entity.Comp.TurnSpeed), body: physicsComponent);

            entity.Comp.TurnDirection = PhysicsVehicleTurnDirection.None;
            return;
        }

        _physicsSystem.SetAngularVelocity(entity.Owner, physicsComponent.AngularVelocity + (turnDirection == PhysicsVehicleTurnDirection.Right ? entity.Comp.TurnSpeed : -entity.Comp.TurnSpeed), body: physicsComponent);
        entity.Comp.TurnDirection = turnDirection;
    }
}
