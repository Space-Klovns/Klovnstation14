using Content.Shared._KS14.CCVar;
using Content.Shared._Trauma.Projectiles;
using Content.Shared.Projectiles;
using Robust.Shared.Configuration;
using Robust.Shared.Map;

namespace Content.Shared._KS14.Projectiles;

/// <summary>
/// Remembers where falloff projectiles were fired from, on both client and server so a predicted hit agrees.
/// </summary>
public sealed partial class KsProjectileFalloffSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;

    private bool _falloffEnabled = true;
    private bool _falloffEverywhere;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_configurationManager, KsCCVars.ProjectileDamageFalloff, value => _falloffEnabled = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.ProjectileDamageFalloffEverywhere, value => _falloffEverywhere = value, invokeImmediately: true);
    }

    [SubscribeLocalEvent]
    private void OnPlayerShotProjectile(ref PlayerShotProjectileEvent args)
    {
        // A gun with the component applies falloff to what it fires, even to a bullet that has none of its own.
        KsGunProjectileFalloffComponent? gunFalloff = null;
        if (TryComp<ProjectileComponent>(args.Projectile, out var projectileComponent) &&
            projectileComponent.Weapon is { } weaponUid)
            TryComp(weaponUid, out gunFalloff);

        if (!TryComp<KsProjectileFalloffComponent>(args.Projectile, out var falloffComponent))
        {
            if (gunFalloff == null)
                return;

            falloffComponent = EnsureComp<KsProjectileFalloffComponent>(args.Projectile);
        }

        // The gun's tuning, written onto the projectile once so the hit code only reads the projectile.
        if (gunFalloff != null)
        {
            falloffComponent.GunOptedIn = true;
            falloffComponent.FullDamageRange = (gunFalloff.FullDamageRange ?? falloffComponent.FullDamageRange) * gunFalloff.RangeScale;
            falloffComponent.MinDamageRange = (gunFalloff.MinDamageRange ?? falloffComponent.MinDamageRange) * gunFalloff.RangeScale;
            falloffComponent.MinMultiplier = gunFalloff.MinMultiplier ?? falloffComponent.MinMultiplier;
        }

        var origin = _transformSystem.GetMapCoordinates(args.Projectile);
        falloffComponent.OriginMap = origin.MapId;
        falloffComponent.OriginPosition = origin.Position;
        Dirty(args.Projectile, falloffComponent);
    }

    /// <summary>
    /// The damage multiplier for a projectile that has now reached <paramref name="hitCoordinates"/>.
    /// 1 when the origin is unknown or on another map.
    /// </summary>
    public float GetMultiplier(Entity<KsProjectileFalloffComponent?> projectile, MapCoordinates hitCoordinates)
    {
        if (!_falloffEnabled ||
            !Resolve(projectile.Owner, ref projectile.Comp, logMissing: false) ||
            projectile.Comp.OriginMap == MapId.Nullspace ||
            projectile.Comp.OriginMap != hitCoordinates.MapId ||
            !(_falloffEverywhere || projectile.Comp.GunOptedIn))
            return 1f;

        var distance = (hitCoordinates.Position - projectile.Comp.OriginPosition).Length();
        return KsProjectileFalloff.GetMultiplier(distance, projectile.Comp);
    }
}

public static class KsProjectileFalloff
{
    /// <summary>Damage multiplier for a projectile that has flown <paramref name="distance"/> tiles.</summary>
    public static float GetMultiplier(float distance, KsProjectileFalloffComponent component)
    {
        if (!(distance > component.FullDamageRange))
            return 1f;

        if (distance >= component.MinDamageRange || component.MinDamageRange <= component.FullDamageRange)
            return component.MinMultiplier;

        var t = (distance - component.FullDamageRange) / (component.MinDamageRange - component.FullDamageRange);
        return 1f + (component.MinMultiplier - 1f) * t;
    }
}
