using Content.Server.Explosion.EntitySystems;
using Content.Shared._KS14.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.Projectiles;

namespace Content.Server._KS14.KillCredit;

/// <summary>
///     Works out who to credit for damage: the hand behind a bullet, a turret's owner, whoever set off an explosion.
/// </summary>
public sealed partial class KsKillCreditSystem : EntitySystem
{
    /// <summary>
    ///     How many hops (bullet, turret, owner...) to follow before giving up, so a loop can't hang the server.
    /// </summary>
    private const int MaxChainLength = 8;

    [Dependency] private ExplosionSystem _explosionSystem = default!;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;
    [Dependency] private EntityQuery<KsCreditOwnerComponent> _creditOwnerQuery = default!;

    /// <summary>
    ///     Who deserves the credit for damage dealt by <paramref name="origin"/>. A projectile is credited to
    ///         whoever fired it, anything with a <see cref="KsCreditOwnerComponent"/> to its owner, and no origin
    ///         at all, inside an explosion, to whoever set it off. Null if nobody.
    /// </summary>
    public EntityUid? ResolveCredit(EntityUid? origin)
    {
        var current = origin ?? _explosionSystem.KsProcessingUser;

        for (var hop = 0; hop < MaxChainLength && current is { } uid; hop++)
        {
            if (_projectileQuery.TryComp(uid, out var projectileComponent) &&
                projectileComponent.Shooter is { } shooterUid &&
                shooterUid != uid)
            {
                current = shooterUid;
            }
            else if (_creditOwnerQuery.TryComp(uid, out var creditOwnerComponent) &&
                     creditOwnerComponent.OwnerUid is { } ownerUid &&
                     ownerUid != uid)
            {
                current = ownerUid;
            }
            else
            {
                break;
            }
        }

        return current;
    }

    [SubscribeLocalEvent]
    private void OnFlatpackUnpacked(Entity<FlatpackComponent> entity, ref KsFlatpackUnpackedEvent args)
    {
        var creditOwnerComponent = EnsureComp<KsCreditOwnerComponent>(args.Unpacked);
        creditOwnerComponent.OwnerUid = args.User;
    }
}
