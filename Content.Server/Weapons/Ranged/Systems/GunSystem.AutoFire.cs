using Content.Shared.Damage.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        /*
         * On server because client doesn't want to predict other's guns.
         */

        // Automatic firing without stopping if the AutoShootGunComponent component is exist and enabled
        var query = EntityQueryEnumerator<GunComponent>();

        while (query.MoveNext(out var uid, out var gun))
        {
            if (gun.NextFire > Timing.CurTime)
                continue;

            if (TryComp(uid, out AutoShootGunComponent? autoShoot))
            {
                if (!autoShoot.Enabled)
                    continue;

                AttemptShoot((uid, gun));
            }
            else if (gun.BurstActivated)
            {
                var parent = TransformSystem.GetParentUid(uid);
                if (HasComp<DamageableComponent>(parent))
                {
                    // KS14 start: a player's burst is their own prediction whichever side fires the next shot of it. Their
                    //      client asks for each, and when that request is late this fires it first: outside the scope its
                    //      round was not hidden from them, and they saw it beside their own.
                    using (_ksPredictedSpawnSystem.BeginUserPrediction(HasComp<Robust.Shared.Player.ActorComponent>(parent) ? (EntityUid?)parent : null))
                        AttemptShoot(parent, (uid, gun), gun.ShootCoordinates ?? new EntityCoordinates(uid, gun.DefaultDirection), gun.Target /* KS14: added arg, the rest of a burst flew over someone down whom its first shot was aimed at */);
                    // KS14 end
                }
                else
                    AttemptShoot((uid, gun));
            }
        }
    }
}
