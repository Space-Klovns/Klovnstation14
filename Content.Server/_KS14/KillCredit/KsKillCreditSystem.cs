using Content.Server.Explosion.EntitySystems;
using Content.Shared._KS14.Construction;
using Content.Shared._Trauma.Projectiles;
using Content.Shared.Construction.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Projectiles;

namespace Content.Server._KS14.KillCredit;

/// <summary>
///     Works out who to credit for damage: the hand behind a bullet, a turret's owner, whoever set off an explosion
///         or pressed the signaller that did.
///         Credit has to be settled while the thing it hangs on still exists: a turret's bullets are stamped with its
///         owner as they leave it, and an explosion is given its user when it is queued.
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
    [Dependency] private EntityQuery<DeviceLinkSourceComponent> _deviceLinkSourceQuery = default!;

    /// <summary>
    ///     Who deserves the credit for damage dealt by <paramref name="origin"/>. A projectile is credited to
    ///         whoever fired it, anything with a <see cref="KsCreditOwnerComponent"/> to its owner, a device whose
    ///         signal set something off to whoever sent the signal by using it
    ///         (<see cref="DeviceLinkSourceComponent.KsSignalUser"/>), and no origin at all, inside an explosion, to
    ///         whoever set it off. Null if nobody.
    /// </summary>
    public EntityUid? ResolveCredit(EntityUid? origin)
    {
        var current = origin ?? _explosionSystem.KsProcessingUser;

        for (var hop = 0; hop < MaxChainLength && current is { } uid; hop++)
        {
            // Before the projectile's shooter: a bullet carries its owner itself, as the turret that shot it may be
            //     gone (a disposable one blows up on its last shot) before the bullet lands.
            if (_creditOwnerQuery.TryComp(uid, out var creditOwnerComponent) &&
                creditOwnerComponent.OwnerUid is { } ownerUid &&
                ownerUid != uid)
            {
                current = ownerUid;
            }
            else if (_projectileQuery.TryComp(uid, out var projectileComponent) &&
                     projectileComponent.Shooter is { } shooterUid &&
                     shooterUid != uid)
            {
                current = shooterUid;
            }
            else if (_deviceLinkSourceQuery.TryComp(uid, out var deviceLinkSourceComponent) &&
                     deviceLinkSourceComponent.KsSignalUser is { } signalUserUid &&
                     signalUserUid != uid &&
                     !TerminatingOrDeleted(signalUserUid))
            {
                current = signalUserUid;
            }
            else
            {
                break;
            }
        }

        return current;
    }

    /// <summary>
    ///     A bullet from something with an owner is stamped with it, which outlives the turret.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPlayerShotProjectile(ref PlayerShotProjectileEvent args)
    {
        if (!_creditOwnerQuery.HasComp(args.User) ||
            ResolveCredit(args.User) is not { } ownerUid ||
            ownerUid == args.User)
            return;

        var creditOwnerComponent = EnsureComp<KsCreditOwnerComponent>(args.Projectile);
        creditOwnerComponent.OwnerUid = ownerUid;
    }

    [SubscribeLocalEvent]
    private void OnFlatpackUnpacked(Entity<FlatpackComponent> entity, ref KsFlatpackUnpackedEvent args)
    {
        var creditOwnerComponent = EnsureComp<KsCreditOwnerComponent>(args.Unpacked);
        creditOwnerComponent.OwnerUid = args.User;
    }
}
