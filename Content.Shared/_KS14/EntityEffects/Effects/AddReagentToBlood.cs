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

/// <inheritdoc cref="EntityEffectSystem{T,TEffect}"/>
public sealed partial class AddReagentToBloodEntityEffectSystem : EntityEffectSystem<BloodstreamComponent, AddReagentToBlood>
{
    [Dependency] private readonly SharedBloodstreamSystem _bloodstreamSystem = default!;

    protected override void Effect(Entity<BloodstreamComponent> entity, ref EntityEffectEvent<AddReagentToBlood> args)
    {
        _bloodstreamSystem.TryAddToBloodstream(entity!, new Solution(args.Effect.Reagent, args.Effect.Amount, args.Effect.Data));
    }
}

/// <inheritdoc cref="EntityEffect"/>
public sealed partial class AddReagentToBlood : EntityEffectBase<AddReagentToBlood>
{
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    [DataField]
    public FixedPoint2 Amount = default;

    [DataField]
    public List<ReagentData>? Data = null;

    public override string? EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        if (!prototype.TryIndex(Reagent, out var reagentProto))
            throw new NotImplementedException();

        return Loc.GetString("reagent-effect-guidebook-add-to-chemicals",
            ("chance", Probability),
            ("deltasign", MathF.Sign(Amount.Float())),
            ("reagent", reagentProto.LocalizedName),
            ("amount", MathF.Abs(Amount.Float())));
    }
}
