using Content.Shared._Trauma.Projectiles;
using Content.Shared.NPC.Components;
using Robust.Shared.Physics.Events;

namespace Content.Shared._KS14.FactionCollision;

public sealed partial class KsFactionCollisionSystem : EntitySystem
{
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;
    [Dependency] private EntityQuery<KsFactionCollisionShooterComponent> _factionCollisionShooterQuery = default!;

    // its almost like linq except not actually under System.Linq

    [SubscribeLocalEvent(after: [typeof(Projectiles.SharedProjectileSystem)])]
    private void OnPreventCollide(Entity<KsFactionCollisionComponent> entity, ref PreventCollideEvent args)
    {
        if (args.Cancelled ||
            !_factionMemberQuery.TryGetComponent(args.OtherEntity, out var otherFactionComponent) ||
            !entity.Comp.Factions.Overlaps(otherFactionComponent.Factions))
            return;

        args.Cancelled = true;
    }

    [SubscribeLocalEvent]
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
