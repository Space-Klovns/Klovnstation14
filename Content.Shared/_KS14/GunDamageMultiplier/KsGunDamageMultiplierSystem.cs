using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._KS14.GunDamageMultiplier;

public sealed partial class KsGunDamageMultiplierSystem : EntitySystem
{
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;

    [SubscribeLocalEvent]
    private void OnGunShot(Entity<KsGunDamageMultiplierComponent> entity, ref GunShotEvent args)
    {
        foreach (var (ammoUid, _) in args.Ammo)
        {
            if (ammoUid is not { } projectileUid ||
                !_projectileQuery.TryComp(projectileUid, out var projectileComponent))
                continue;

            projectileComponent.Damage *= entity.Comp.Multiplier;
        }
    }
}
