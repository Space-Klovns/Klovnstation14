using Content.Shared._KS14.Weapons.Ranged;
using Content.Shared.Projectiles;

namespace Content.Shared._KS14.GunDamageMultiplier;

public sealed partial class KsGunDamageMultiplierSystem : EntitySystem
{
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;

    // Not GunShotEvent: its ammo is the cartridges, and the bullets they spawn are separate entities.
    [SubscribeLocalEvent]
    private void OnFiredProjectiles(Entity<KsGunDamageMultiplierComponent> entity, ref KsGunFiredProjectilesEvent args)
    {
        foreach (var projectileUid in args.ProjectileUids)
        {
            if (!_projectileQuery.TryComp(projectileUid, out var projectileComponent))
                continue;

            projectileComponent.Damage *= entity.Comp.Multiplier;
        }
    }
}
