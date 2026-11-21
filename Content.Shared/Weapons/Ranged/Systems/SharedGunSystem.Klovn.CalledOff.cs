using Content.Shared._KS14.Weapons.Ranged;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared.Weapons.Ranged.Systems;

// Partial: shots called off after their ammo was taken, and shooting ended outright, kept apart from the upstream code.
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
    ///     Uses up the ammo of a shot its shooter called off (<see cref="Events.SelfBeforeGunShotEvent"/>, a clumsy
    ///         backfire) after the gun had already given it up, as firing it would have: a cartridge is spent, then
    ///         ejected or deleted as a fired one is, and a round that is its own projectile or ray is deleted. Left as
    ///         it was, a cartridge lay on the floor unspent, and a battery's bolt sat motionless where it was spawned.
    /// </summary>
    /// <param name="throwItems">What a pneumatic cannon throws is an item rather than a round, so it's left where it fell.</param>
    private void KsUseUpCalledOffShot(
        Entity<GunComponent> gun,
        List<(EntityUid? Entity, IShootable Shootable)> ammo,
        EntityUid? user,
        bool throwItems)
    {
        if (throwItems)
            return;

        foreach (var (ammoUid, shootable) in ammo)
        {
            if (ammoUid is not { } uid)
                continue;

            switch (shootable)
            {
                case CartridgeAmmoComponent cartridge:
                    if (!cartridge.Spent)
                    {
                        SetCartridgeSpent(uid, cartridge, true);

                        if (cartridge.DeleteOnSpawn)
                        {
                            PredictedQueueDel(uid);
                            break;
                        }
                    }

                    // One left in its chamber or cylinder stays there, as a fired one does.
                    if (!Containers.IsEntityInContainer(uid))
                        EjectCartridge(GetRandom(gun), uid, user: user);
                    break;
                case AmmoComponent:
                case HitscanAmmoComponent:
                    PredictedQueueDel(uid);
                    break;
            }
        }
    }
}
