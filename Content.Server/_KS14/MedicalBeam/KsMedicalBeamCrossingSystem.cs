using Content.Server.Administration.Logs;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._KS14.MedicalBeam;
using Content.Shared.Database;

namespace Content.Server._KS14.MedicalBeam;

/// <summary>
///     Two medical beams crossed (<see cref="KsMedicalBeamsCrossedEvent"/>): sets off the gun's
///         <see cref="KsMedicalBeamGunComponent.CrossExplosion"/> where they cross, set off by the holder whose beam found the
///         crossing, and logs both holders (tg's <c>mid_los_check</c>).
/// </summary>
public sealed partial class KsMedicalBeamCrossingSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogManager = default!;
    [Dependency] private ExplosionSystem _explosionSystem = default!;

    [SubscribeLocalEvent]
    private void OnCrossed(Entity<KsMedicalBeamGunComponent> gun, ref KsMedicalBeamsCrossedEvent args)
    {
        if (gun.Comp.CrossExplosion is not { } explosion)
            return;

        var totalIntensity = _explosionSystem.RadiusToIntensity(gun.Comp.CrossExplosionRadius,
            gun.Comp.CrossExplosionSlope,
            gun.Comp.CrossExplosionMaxIntensity);

        _adminLogManager.Add(LogType.Explosion,
            LogImpact.High,
            $"{ToPrettyString(args.UserUid):user} and {ToPrettyString(args.OtherUserUid):otherUser} crossed medical beams ({ToPrettyString(gun.Owner):gun}, {ToPrettyString(args.OtherGunUid):otherGun}), exploding ({explosion}) at {args.Crossing} with intensity {totalIntensity} slope {gun.Comp.CrossExplosionSlope}");

        _explosionSystem.QueueExplosion(args.Crossing,
            explosion,
            totalIntensity,
            gun.Comp.CrossExplosionSlope,
            gun.Comp.CrossExplosionMaxIntensity,
            cause: gun.Owner,
            addLog: false,
            ksUser: args.UserUid);
    }
}
