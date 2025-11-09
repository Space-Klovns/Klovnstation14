// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 nabegator220
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Content.Shared.Chemistry.Components;

namespace Content.Shared._KS14.EntityEffects.Effects;

public sealed partial class AddReagentToBlood : EntityEffect
{
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    [DataField]
    public FixedPoint2 Amount = default;

    [DataField]
    public List<ReagentData>? Data = null;

    [Dependency] private SharedBloodstreamSystem? _bloodstreamSystem = null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent<BloodstreamComponent>(args.TargetEntity, out var blood))
            return;

        // yeah wtf is this
        _bloodstreamSystem ??= args.EntityManager.System<SharedBloodstreamSystem>();
        _bloodstreamSystem.TryAddToChemicals((args.TargetEntity, blood), new Solution(Reagent, Amount, Data));
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        if (prototype.TryIndex(Reagent, out var reagentProto))
        {
            return Loc.GetString("reagent-effect-guidebook-add-to-chemicals",
                ("chance", Probability),
                ("deltasign", MathF.Sign(Amount.Float())),
                ("reagent", reagentProto.LocalizedName),
                ("amount", MathF.Abs(Amount.Float())));
        }

        throw new NotImplementedException();
    }
}
