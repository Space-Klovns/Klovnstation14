// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MIT

using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.OverlayStains;

public sealed partial class StainCleanReaction : EntityEffect
{
    private StainSystem _stainSystem = default!;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wash-cream-pie-reaction", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        _stainSystem ??= args.EntityManager.System<StainSystem>();
        _stainSystem.CleanEntity(args.TargetEntity);
    }
}
