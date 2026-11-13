using Content.Shared._Trauma.Projectiles;
using Robust.Shared.Map;

namespace Content.Shared._KS14.Projectiles;

/// <summary>
/// Remembers where falloff projectiles were fired from, on both client and server so a predicted hit agrees.
/// </summary>
public sealed partial class KsProjectileFalloffSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [SubscribeLocalEvent]
    private void OnPlayerShotProjectile(ref PlayerShotProjectileEvent args)
    {
        if (!TryComp<KsProjectileFalloffComponent>(args.Projectile, out var falloffComponent))
            return;

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
        if (!Resolve(projectile.Owner, ref projectile.Comp, logMissing: false) ||
            projectile.Comp.OriginMap == MapId.Nullspace ||
            projectile.Comp.OriginMap != hitCoordinates.MapId)
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
