using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityConditions;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.EntityEffects.EffectConditions;

/// <summary>
///     Returns true if the entity has taken at least a certain amount of damage, of all kinds together.
/// </summary>
/// <inheritdoc cref="EntityConditionSystem{T, TCondition}"/>
public sealed partial class TotalDamageEntityConditionSystem : EntityConditionSystem<DamageableComponent, TotalDamageCondition>
{
    [Dependency] private DamageableSystem _damageableSystem = default!;

    protected override void Condition(Entity<DamageableComponent> entity, ref EntityConditionEvent<TotalDamageCondition> args)
    {
        args.Result = _damageableSystem.GetTotalDamage((entity.Owner, entity.Comp)) >= args.Condition.Min;
    }
}

/// <inheritdoc cref="EntityCondition"/>
public sealed partial class TotalDamageCondition : EntityConditionBase<TotalDamageCondition>
{
    [DataField(required: true)]
    public FixedPoint2 Min;

    public override string EntityConditionGuidebookText(IPrototypeManager prototype) =>
        Loc.GetString("reagent-effect-condition-guidebook-total-damage", ("min", Min.Float()));
}
