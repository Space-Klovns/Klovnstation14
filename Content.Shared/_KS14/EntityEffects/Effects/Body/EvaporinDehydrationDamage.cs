using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.EntityEffects.Effects.Body;

/// <summary>
/// Deals damage if the entity is at the Dead thirst threshold.
/// </summary>
public sealed partial class EvaporinDehydrationDamage : EntityEffectBase<EvaporinDehydrationDamage>
{
    [DataField(required: true)]
    public DamageSpecifier Damage = default!;
}

public sealed partial class EvaporinDehydrationDamageSystem : EntityEffectSystem<ThirstComponent, EvaporinDehydrationDamage>
{
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;

    protected override void Effect(Entity<ThirstComponent> entity, ref EntityEffectEvent<EvaporinDehydrationDamage> args)
    {
        if (entity.Comp.CurrentThirstThreshold != ThirstThreshold.Dead)
            return;

        _damageableSystem.TryChangeDamage(entity.Owner, args.Effect.Damage * args.Scale, true, false);
    }
}
