using Content.Shared._Trauma.Projectiles;
using Content.Shared.NPC.Components;
using Robust.Shared.Physics.Events;

namespace Content.Shared._KS14.FactionCollision;

public sealed class KsFactionCollisionSystem : EntitySystem
{
    [Dependency] private readonly EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;
    [Dependency] private readonly EntityQuery<KsFactionCollisionShooterComponent> _factionCollisionShooterQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsFactionCollisionComponent, PreventCollideEvent>(OnPreventCollide,
            after: [typeof(Projectiles.SharedProjectileSystem)] /* PreventCollideEvent for projectiles is really cheap, rather do that first */);
        SubscribeLocalEvent<PlayerShotProjectileEvent>(OnPlayerShotProjectile);
    }

    // its almost like linq except not actually under System.Linq

    private void OnPreventCollide(Entity<KsFactionCollisionComponent> entity, ref PreventCollideEvent args)
    {
        if (args.Cancelled ||
            !_factionMemberQuery.TryGetComponent(args.OtherEntity, out var otherFactionComponent) ||
            !entity.Comp.Factions.Overlaps(otherFactionComponent.Factions))
            return;

        args.Cancelled = true;
    }

    private void OnPlayerShotProjectile(ref PlayerShotProjectileEvent args)
    {
        if (!_factionCollisionShooterQuery.HasComponent(args.User) ||
            !_factionMemberQuery.TryGetComponent(args.User, out var factionMemberComponent) ||
            factionMemberComponent.Factions.Count == 0)
            return;

        var collisionComponent = EnsureComp<KsFactionCollisionComponent>(args.Projectile);
        collisionComponent.Factions.UnionWith(factionMemberComponent.Factions);

        Dirty(args.Projectile, collisionComponent);
    }
}
