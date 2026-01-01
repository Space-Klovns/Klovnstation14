// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 nabegator220
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.EntityConditions;

namespace Content.Shared._KS14.EntityEffects.EffectConditions;

/// <summary>
///     Returns true if the entity's bloodstream contains a specified amount
///         of a certain reagent.
/// </summary>
/// <inheritdoc cref="EntityConditionSystem{T, TCondition}"/>
public sealed partial class BloodReagentEntityConditionSystem : EntityConditionSystem<BloodstreamComponent, BloodReagentCondition>
{
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;

    protected override void Condition(Entity<BloodstreamComponent> entity, ref EntityConditionEvent<BloodReagentCondition> args)
    {
        if (!_solutionContainerSystem.ResolveSolution(entity.Owner, entity.Comp.BloodSolutionName, ref entity.Comp.BloodSolution, out var chemSolution))
        {
            Log.Error($"Couldn't find solution of name {entity.Comp.BloodSolutionName} in entity {ToPrettyString(entity.Owner)}");

            args.Result = false;
            return;
        }

        var quantity = chemSolution.GetTotalPrototypeQuantity(args.Condition.Reagent);
        args.Result = quantity > args.Condition.Min && quantity < args.Condition.Max;
    }
}

/// <inheritdoc cref="EntityCondition"/>
public sealed partial class BloodReagentCondition : EntityConditionBase<BloodReagentCondition>
{
    [DataField]
    public FixedPoint2 Min = FixedPoint2.Zero;

    [DataField]
    public FixedPoint2 Max = FixedPoint2.MaxValue;

    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    public override string EntityConditionGuidebookText(IPrototypeManager prototype)
    {
        if (!prototype.Resolve(Reagent, out var reagentProto))
            return string.Empty;

        return Loc.GetString("reagent-effect-condition-guidebook-blood-reagent-threshold",
            ("reagent", reagentProto?.LocalizedName ?? Loc.GetString("reagent-effect-condition-guidebook-this-reagent")),
            ("max", Max == FixedPoint2.MaxValue ? float.MaxValue : Max.Float()),
            ("min", Min.Float()));
    }
}
