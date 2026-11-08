using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;

namespace Content.Shared.Weapons.Ranged.Systems;

// Partial: firing a gun whose user can't act, kept apart from the upstream shooting code.
public abstract partial class SharedGunSystem
{
    /// <summary>
    ///     As <see cref="AttemptShoot(EntityUid, Entity{GunComponent}, EntityCoordinates, EntityUid?)"/>, but for a shot
    ///         <paramref name="user"/> isn't choosing to take, so it goes ahead even if they can't attack.
    /// </summary>
    public bool KsAttemptInvoluntaryShoot(EntityUid user, Entity<GunComponent> gun, EntityCoordinates toCoordinates)
    {
        gun.Comp.ShootCoordinates = toCoordinates;
        gun.Comp.Target = null;
        var result = AttemptShoot(user, gun, ksInvoluntary: true);
        gun.Comp.ShotCounter = 0;
        DirtyField(gun.AsNullable(), nameof(GunComponent.ShotCounter));
        return result;
    }

    /// <summary>
    ///     Puts <paramref name="gun"/> back as if nothing was pulling its trigger, so its next real shot starts clean.
    /// </summary>
    public void KsReleaseTrigger(Entity<GunComponent> gun)
    {
        gun.Comp.ShotCounter = 0;
        gun.Comp.ShootCoordinates = null;
        gun.Comp.Target = null;
        gun.Comp.LastShotWasEmpty = false;
        DirtyFields(gun!, null, nameof(GunComponent.ShotCounter), nameof(GunComponent.LastShotWasEmpty));
    }
}
