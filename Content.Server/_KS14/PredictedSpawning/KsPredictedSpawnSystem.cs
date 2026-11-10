using Content.Shared._KS14.PredictedSpawning;
using Content.Shared.Damage;
using Content.Shared.Projectiles;
using Robust.Shared.Player;

namespace Content.Server._KS14.PredictedSpawning;

/// <inheritdoc/>
public sealed partial class KsPredictedSpawnSystem : KsSharedPredictedSpawnSystem
{
    [Dependency] private EntityQuery<KsPredictedSpawnComponent> _predictedSpawnQuery = default!;
    [Dependency] private EntityQuery<KsUserPredictedProjectileComponent> _userPredictedQuery = default!;

    protected override void KeepForUser(EntityUid uid, EntityUid user)
    {
        SetPredictingUser((uid, EnsureComp<KsPredictedSpawnComponent>(uid)), user);
    }

    protected override void OnUserPredictedShot(EntityUid projectile, EntityUid user)
    {
        EnsureComp<KsUserPredictedProjectileComponent>(projectile).User = user;
    }

    public override void OnProjectileHit(Entity<ProjectileComponent> projectile, EntityUid target, DamageSpecifier? damage)
    {
        // A new instance per send: a queued network event is not serialised at once.
        KsPredictedSpawnImpactEvent MakeEvent(bool toPredictingUser) => new()
        {
            Projectile = GetNetEntity(projectile.Owner),
            Target = GetNetEntity(target),
            Coordinates = GetNetCoordinates(Transform(projectile.Owner).Coordinates),
            ImpactEffect = projectile.Comp.ImpactEffect,
            SoundHit = projectile.Comp.SoundHit,
            ForceSound = projectile.Comp.ForceSound,
            Damage = damage,
            ToPredictingUser = toPredictingUser,
        };

        // The one client that predicted this shot, if any: it played the hit itself, or reconciles it with its own.
        EntityUid? predictingUser = null;
        if (_predictedSpawnQuery.TryComp(projectile.Owner, out var predictedSpawnComponent) &&
            predictedSpawnComponent.PredictingUser is { } keptByUser)
        {
            predictingUser = keptByUser;

            // Not recorded for replays: a replay shows the server's copy hitting, as everyone else sees it.
            RaiseNetworkEvent(MakeEvent(toPredictingUser: true), keptByUser);
        }
        else if (_userPredictedQuery.TryComp(projectile.Owner, out var userPredictedComponent) &&
                 userPredictedComponent.User == projectile.Comp.Shooter)
        {
            predictingUser = userPredictedComponent.User;
        }

        // Everyone else interpolates the server's copy rather than predicting it, so nothing plays its hit for them.
        var filter = Filter.Pvs(projectile.Owner, entityManager: EntityManager);
        if (predictingUser is { } excludedUser)
            filter.RemoveWhereAttachedEntity(attachedUid => attachedUid == excludedUser);

        RaiseNetworkEvent(MakeEvent(toPredictingUser: false), filter);
    }
}
