// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 nabegator220
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Content.Shared.Chemistry.EntitySystems;

namespace Content.Shared._KS14.EntityEffects.EffectConditions;

public sealed partial class BloodReagentThreshold : EntityEffectCondition
{
    [DataField]
    public FixedPoint2 Min = FixedPoint2.Zero;

    [DataField]
    public FixedPoint2 Max = FixedPoint2.MaxValue;

    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    [Dependency] private SharedSolutionContainerSystem? _solutionContainerSystem = null;

    public override bool Condition(EntityEffectBaseArgs args)
    {
        // yeah wtf is this #2
        _solutionContainerSystem ??= args.EntityManager.System<SharedSolutionContainerSystem>();

        if (!args.EntityManager.TryGetComponent<BloodstreamComponent>(args.TargetEntity, out var blood) ||
            !_solutionContainerSystem.ResolveSolution(args.TargetEntity, blood.ChemicalSolutionName, ref blood.ChemicalSolution, out var chemSolution))
            throw new NotImplementedException();

        var reagentID = new ReagentId(Reagent, null);
        if (chemSolution.TryGetReagentQuantity(reagentID, out var quant))
            return quant > Min && quant < Max;

        return true;
    }

    public override string GuidebookExplanation(IPrototypeManager prototype)
    {
        prototype.TryIndex(Reagent, out var reagentProto);

        return Loc.GetString("reagent-effect-condition-guidebook-blood-reagent-threshold",
            ("reagent", reagentProto?.LocalizedName ?? Loc.GetString("reagent-effect-condition-guidebook-this-reagent")),
            ("max", Max == FixedPoint2.MaxValue ? float.MaxValue : Max.Float()),
            ("min", Min.Float()));
    }
}
