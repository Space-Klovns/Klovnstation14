using Content.Shared.Damage.Components;
using Robust.Shared.Physics.Components;

namespace Content.Shared.Damage.Systems;

public sealed partial class DamageContactsSystem
{
    /// <summary>
    ///     How fast, in metres a second, an entity has to be going to count as moving for
    ///         <see cref="DamageContactsComponent.OnlyWhileMoving"/>. Well below a slowed walk, well above the creep of a
    ///         body coming to rest.
    /// </summary>
    public const float KsMovingSpeed = 0.1f;

    [Dependency] private EntityQuery<PhysicsComponent> _ksPhysicsQuery = default!;

    /// <summary>
    ///     Whether contact damage that is due lands now. A source that only hurts the moving holds it back while the
    ///         entity stands still, without moving its next tick on, so the first step after standing still hurts at once
    ///         and the usual cadence runs from there.
    /// </summary>
    private bool KsCanTakeContactDamage(Entity<DamagedByContactComponent> entity)
    {
        if (!entity.Comp.OnlyWhileMoving)
            return true;

        // The body's own velocity, relative to what it stands on: what a player's input or an NPC's steering sets, and
        //     what a push or a pull gives it. Predicted for the local player's mob, from the server's state otherwise.
        return _ksPhysicsQuery.TryComp(entity, out var physicsComponent)
               && physicsComponent.LinearVelocity.LengthSquared() > KsMovingSpeed * KsMovingSpeed;
    }
}
