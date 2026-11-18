using Content.Server._KS14.Cultivation.Components;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     The legendary weapons' swings: each leaves a slash of its own colour on whatever it hits, turned along the blow.
/// </summary>
public sealed partial class LegendarySystem
{
    [SubscribeLocalEvent]
    private void OnSwingHit(Entity<LegendarySwingComponent> entity, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        var from = _transformSystem.GetMapCoordinates(args.User);
        foreach (var hitUid in args.HitEntities)
        {
            var at = _transformSystem.GetMapCoordinates(hitUid);
            if (at.MapId != from.MapId)
                continue;

            var effectUid = Spawn(entity.Comp.HitEffect, at);
            var direction = at.Position - from.Position;
            if (direction.LengthSquared() > 0.001f)
                _transformSystem.SetWorldRotation(effectUid, direction.ToWorldAngle());
        }
    }
}
