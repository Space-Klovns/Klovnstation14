using Content.Shared._KS14.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared.Weapons.Ranged.Systems;

// Partial: shots called off before they take their ammo, and shooting ended outright, kept apart from the upstream code.
public abstract partial class SharedGunSystem
{
    /// <summary>
    ///     Ends <paramref name="gun"/>'s shooting outright: releases its trigger, cuts its burst short, and raises
    ///         <see cref="KsGunShootingInterruptedEvent"/> for whatever would keep it firing on its own. Its fire rate
    ///         is left alone, so it can't fire again any sooner.
    /// </summary>
    public void KsInterruptShooting(Entity<GunComponent> gun)
    {
        KsReleaseTrigger(gun);

        if (gun.Comp.BurstActivated || gun.Comp.BurstShotsCount != 0)
        {
            gun.Comp.BurstActivated = false;
            gun.Comp.BurstShotsCount = 0;
            DirtyFields(gun!, null, nameof(GunComponent.BurstActivated), nameof(GunComponent.BurstShotsCount));
        }

        var ev = new KsGunShootingInterruptedEvent();
        RaiseLocalEvent(gun, ref ev);
    }

    /// <summary>
    ///     Raises <see cref="KsShooterFireAttemptEvent"/> on <paramref name="user"/> for a shot about to take its ammo.
    ///         If they call it off, the gun is left as it was, bar waiting out its fire rate as if it had fired.
    /// </summary>
    /// <returns>Whether the shot was called off.</returns>
    private bool KsShotCalledOff(EntityUid user, Entity<GunComponent> gun, TimeSpan nextFire)
    {
        // An empty gun clicks; nothing in it can blow up.
        var ammoCountEv = new GetAmmoCountEvent();
        RaiseLocalEvent(gun, ref ammoCountEv);
        if (ammoCountEv.Count <= 0)
            return false;

        var ev = new KsShooterFireAttemptEvent(gun);
        RaiseLocalEvent(user, ref ev);
        if (!ev.Cancelled)
            return false;

        gun.Comp.NextFire = nextFire;
        DirtyField(gun.AsNullable(), nameof(GunComponent.NextFire));
        return true;
    }
}
