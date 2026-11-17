using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Whitelist;

namespace Content.Shared._KS14.Bane;

public sealed partial class KsBaneSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;

    [SubscribeLocalEvent]
    private void OnMeleeHit(Entity<KsBaneComponent> entity, ref MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        // Per target rather than through BonusDamage, which every target of a wide swing would get.
        foreach (var hitUid in args.HitEntities)
        {
            if (_whitelistSystem.IsWhitelistPass(entity.Comp.Whitelist, hitUid))
                _damageableSystem.TryChangeDamage(hitUid, entity.Comp.Damage, origin: args.User);
        }
    }

    [SubscribeLocalEvent]
    private void OnThrowHit(Entity<KsBaneComponent> entity, ref ThrowDoHitEvent args)
    {
        if (_whitelistSystem.IsWhitelistPass(entity.Comp.Whitelist, args.Target))
            _damageableSystem.TryChangeDamage(args.Target, entity.Comp.Damage, origin: args.Component.Thrower);
    }

    [SubscribeLocalEvent]
    private void OnExamined(Entity<KsBaneComponent> entity, ref ExaminedEvent args)
    {
        if (entity.Comp.ExamineText is { } examineText)
            args.PushMarkup(Loc.GetString(examineText));
    }
}
