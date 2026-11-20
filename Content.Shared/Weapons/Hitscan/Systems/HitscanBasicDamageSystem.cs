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

        // KS14 start: the shooter's side may change the damage, as ProjectileHitEvent lets it for bullets
        if (args.Data.Shooter is { } ksShooterUid)
        {
            var ksModifyEv = new _KS14.Weapons.Hitscan.KsHitscanDamageModifyEvent(args.Data, dmg);
            RaiseLocalEvent(ksShooterUid, ref ksModifyEv);
            dmg = ksModifyEv.Damage;
        }
        // KS14 end

        var damaged = _damage.TryChangeDamage(args.Data.HitEntity.Value, dmg, out var damageDealt, origin: args.Data.Gun); // KS14: kept, for the impact below

        // KS14 start: TGMC's per-surface impact particles, as bullets have, whether or not the hit did damage: a bullet's
        //      plays on what it cannot hurt too, and armour soaking all of it is still a hit on the armour
        if (args.Data.HitCoordinates is { } hitCoordinates)
            _ksImpactSurfaceSystem.PlayImpactPredicted(args.Data.HitEntity.Value, hitCoordinates, args.Data.ShotDirection, dmg, damageDealt, args.Data.Shooter);

        if (!damaged)
            return;
        // KS14 end

        var damageEvent = new HitscanDamageDealtEvent
        {
            Target = args.Data.HitEntity.Value,
            DamageDealt = damageDealt,
            RawDamage = dmg, // KS14
        };

        RaiseLocalEvent(ent, ref damageEvent);

        // KS14 start: and hears that it hit
        if (args.Data.Shooter is { } ksHitShooterUid)
        {
            var ksHitEv = new _KS14.Weapons.Hitscan.KsShooterHitscanHitEvent(args.Data, damageDealt);
            RaiseLocalEvent(ksHitShooterUid, ref ksHitEv);
        }
        // KS14 end
    }
}
