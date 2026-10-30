using Content.Shared._KS14.PredictedSpawning;
using Content.Shared.Damage;
using Content.Shared.Projectiles;

namespace Content.Server._KS14.PredictedSpawning;

/// <inheritdoc/>
public sealed partial class KsPredictedSpawnSystem : KsSharedPredictedSpawnSystem
{
    [Dependency] private EntityQuery<KsPredictedSpawnComponent> _predictedSpawnQuery = default!;

    protected override void KeepForUser(EntityUid uid, EntityUid user)
    {
        SetPredictingUser((uid, EnsureComp<KsPredictedSpawnComponent>(uid)), user);
    }

    public override void OnProjectileHit(Entity<ProjectileComponent> projectile, EntityUid target, DamageSpecifier? damage)
    {
        if (!_predictedSpawnQuery.TryComp(projectile.Owner, out var predictedSpawnComponent) ||
            predictedSpawnComponent.PredictingUser is not { } user)
            return;

        // Not recorded for replays: a replay shows the server's copy hitting, as it does for everyone but the user.
        RaiseNetworkEvent(new KsPredictedSpawnImpactEvent
        {
            Projectile = GetNetEntity(projectile.Owner),
            Target = GetNetEntity(target),
            Coordinates = GetNetCoordinates(Transform(projectile.Owner).Coordinates),
            ImpactEffect = projectile.Comp.ImpactEffect,
            SoundHit = projectile.Comp.SoundHit,
            ForceSound = projectile.Comp.ForceSound,
            Damage = damage,
        }, user);
    }
}
