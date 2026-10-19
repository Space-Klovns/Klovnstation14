using System.Numerics;
using Content.Server.Movement.Systems;
using Content.Server.Physics.Controllers;
using Content.Shared.Friction;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Physics.Events;

namespace Content.Server._KS14.Physics;

/// <summary>
///     Drives <see cref="KsConstantVelocityComponent"/>. Runs as a controller after everything else that writes
///         velocity or damping before the solve (mob and shuttle movement, tile friction, pulling, conveyors), so
///         whatever they did this tick is overwritten.
/// </summary>
public sealed partial class KsConstantVelocitySystem : VirtualController
{
    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(MoverController));
        UpdatesAfter.Add(typeof(TileFrictionController));
        UpdatesAfter.Add(typeof(PullController));
        UpdatesAfter.Add(typeof(ConveyorController));

        base.Initialize();
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsConstantVelocityComponent> entity, ref ComponentStartup args)
    {
        if (!TryComp<PhysicsComponent>(entity, out var physicsComponent))
            return;

        entity.Comp.OriginalLinearDamping = physicsComponent.LinearDamping;
        entity.Comp.OriginalAngularDamping = physicsComponent.AngularDamping;
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsConstantVelocityComponent> entity, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(entity) ||
            !TryComp<PhysicsComponent>(entity, out var physicsComponent))
            return;

        RestoreBodyType((entity.Owner, entity.Comp, physicsComponent));
        PhysicsSystem.SetLinearDamping(entity, physicsComponent, entity.Comp.OriginalLinearDamping);
        PhysicsSystem.SetAngularDamping(entity, physicsComponent, entity.Comp.OriginalAngularDamping);

        // Contacts that were filtered out while collision was off have to be found again.
        if (!entity.Comp.AppliedCanCollide)
            PhysicsSystem.RegenerateContacts((entity.Owner, physicsComponent));
    }

    [SubscribeLocalEvent]
    private void OnPreventCollide(Entity<KsConstantVelocityComponent> entity, ref PreventCollideEvent args)
    {
        if (!entity.Comp.CanCollide)
            args.Cancelled = true;
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        var query = EntityQueryEnumerator<KsConstantVelocityComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var constantVelocityComponent, out var physicsComponent))
        {
            // Existing contacts were made under the old setting, so a change has to tear them down to take effect.
            if (constantVelocityComponent.AppliedCanCollide != constantVelocityComponent.CanCollide)
            {
                constantVelocityComponent.AppliedCanCollide = constantVelocityComponent.CanCollide;
                PhysicsSystem.RegenerateContacts((uid, physicsComponent));
            }

            if (physicsComponent.BodyType == BodyType.Static)
                continue;

            // Tile friction rewrites damping every tick for every awake body, so this has to be reasserted each tick too.
            PhysicsSystem.SetLinearDamping(uid, physicsComponent, 0f);
            PhysicsSystem.SetAngularDamping(uid, physicsComponent, 0f);

            if (constantVelocityComponent.LockPosition)
            {
                // Reasserted every tick rather than once, since other systems set body types too: ShuttleSystem puts
                //      a grid back to Dynamic when it FTLs, for one.
                if (physicsComponent.BodyType != BodyType.Kinematic)
                {
                    constantVelocityComponent.OriginalBodyType = physicsComponent.BodyType;
                    PhysicsSystem.SetBodyType(uid, BodyType.Kinematic, body: physicsComponent);
                }

                PhysicsSystem.SetLinearVelocity(uid, Vector2.Zero, body: physicsComponent);
            }
            else
            {
                RestoreBodyType((uid, constantVelocityComponent, physicsComponent));

                if (constantVelocityComponent.EnforceLinearVelocity)
                    PhysicsSystem.SetLinearVelocity(uid, constantVelocityComponent.LinearVelocity, body: physicsComponent);
            }

            if (constantVelocityComponent.EnforceAngularVelocity)
                PhysicsSystem.SetAngularVelocity(uid, constantVelocityComponent.AngularVelocity, body: physicsComponent);
        }
    }

    /// <summary>
    ///     Undoes <see cref="KsConstantVelocityComponent.LockPosition"/>'s body type change, unless something else has
    ///         changed the body type since, in which case that change is left alone.
    /// </summary>
    private void RestoreBodyType(Entity<KsConstantVelocityComponent, PhysicsComponent> entity)
    {
        if (entity.Comp1.OriginalBodyType is not { } originalBodyType)
            return;

        entity.Comp1.OriginalBodyType = null;

        if (entity.Comp2.BodyType == BodyType.Kinematic)
            PhysicsSystem.SetBodyType(entity, originalBodyType, body: entity.Comp2);
    }
}
