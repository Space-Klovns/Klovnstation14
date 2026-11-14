using Content.Shared.Damage.Systems;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;

namespace Content.Shared.Weapons.Hitscan.Systems;

public sealed partial class HitscanBasicDamageSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private _KS14.Impacts.KsImpactSurfaceSystem _ksImpactSurfaceSystem = default!; // KS14

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HitscanBasicDamageComponent, HitscanRaycastFiredEvent>(OnHitscanHit);
    }

    private void OnHitscanHit(Entity<HitscanBasicDamageComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        if (args.Data.HitEntity == null)
            return;

        var dmg = ent.Comp.Damage * _damage.UniversalHitscanDamageModifier;

        if (!_damage.TryChangeDamage(args.Data.HitEntity.Value, dmg, out var damageDealt, origin: args.Data.Gun))
            return;

        // KS14 start: TGMC's per-surface impact particles, as bullets have
        if (args.Data.HitCoordinates is { } hitCoordinates)
            _ksImpactSurfaceSystem.PlayImpactPredicted(args.Data.HitEntity.Value, hitCoordinates, args.Data.ShotDirection, dmg, damageDealt, args.Data.Shooter);
        // KS14 end

        var damageEvent = new HitscanDamageDealtEvent
        {
            Target = args.Data.HitEntity.Value,
            DamageDealt = damageDealt,
        };

        RaiseLocalEvent(ent, ref damageEvent);
    }
}
