using Content.Server._KS14.Cultivation.Components;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     The legendary weapons' swings: each throws sparks of its own colour off whatever it hits.
/// </summary>
public sealed partial class LegendarySystem
{
    [SubscribeLocalEvent]
    private void OnSwingHit(Entity<LegendarySwingComponent> entity, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        // The Ruyi's clones turn on whoever their master strikes.
        if (HasComp<RuyiStaffComponent>(entity))
        {
            var clones = EntityQueryEnumerator<SageCloneComponent>();
            while (clones.MoveNext(out var cloneUid, out var clone))
            {
                if (clone.Master != args.User)
                    continue;

                foreach (var hitUid in args.HitEntities)
                {
                    if (hitUid != cloneUid && HasComp<Content.Shared.Mobs.Components.MobStateComponent>(hitUid))
                        _npcFactionSystem.AggroEntity(cloneUid, hitUid);
                }
            }
        }

        var from = _transformSystem.GetMapCoordinates(args.User);
        foreach (var hitUid in args.HitEntities)
        {
            var at = _transformSystem.GetMapCoordinates(hitUid);
            if (at.MapId != from.MapId)
                continue;

            // Thrown off the near side of the target, toward the blade.
            var direction = from.Position - at.Position;
            var offset = direction.LengthSquared() > 0.001f ? System.Numerics.Vector2.Normalize(direction) * 0.25f : System.Numerics.Vector2.Zero;
            Spawn(entity.Comp.HitEffect, at.Offset(offset));
        }
    }
}
