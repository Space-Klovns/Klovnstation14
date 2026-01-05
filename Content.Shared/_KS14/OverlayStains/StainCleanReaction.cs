// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MIT

using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.OverlayStains;

/// <inheritdoc cref="EntityEffectSystem{T,TEffect}"/>
public sealed partial class StainCleanEntityEffectSystem : EntityEffectSystem<StainedComponent, StainClean>
{
    [Dependency] private readonly StainSystem _stainSystem = default!;

    protected override void Effect(Entity<StainedComponent> entity, ref EntityEffectEvent<StainClean> args)
    {
        _stainSystem.CleanEntity(entity!);
    }
}

/// <inheritdoc cref="EntityEffect"/>
public sealed partial class StainClean : EntityEffectBase<StainClean>
{
    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString("entity-effect-guidebook-stain-clean");
    }
}
