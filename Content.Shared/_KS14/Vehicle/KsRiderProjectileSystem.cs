using Content.Shared._Trauma.Projectiles;
using Content.Shared.Vehicle.Systems;
using Robust.Shared.Physics.Events;

namespace Content.Shared._KS14.Vehicle;

/// <summary>
///     Keeps a vehicle operator's shots from hitting the vehicle they fire from. A projectile spawns at its shooter,
///         and so inside whatever they ride.
/// </summary>
public sealed partial class KsRiderProjectileSystem : EntitySystem
{
    [Dependency] private VehicleSystem _vehicleSystem = default!;

    [SubscribeLocalEvent]
    private void OnPlayerShotProjectile(ref PlayerShotProjectileEvent args)
    {
        if (_vehicleSystem.GetOperatedVehicleOrNull(args.User) is not { } vehicleUid)
            return;

        var riderProjectileComponent = EnsureComp<KsRiderProjectileComponent>(args.Projectile);
        riderProjectileComponent.Vehicle = vehicleUid;
        Dirty(args.Projectile, riderProjectileComponent);
    }

    [SubscribeLocalEvent]
    private void OnPreventCollide(Entity<KsRiderProjectileComponent> entity, ref PreventCollideEvent args)
    {
        if (args.OtherEntity == entity.Comp.Vehicle)
            args.Cancelled = true;
    }
}
