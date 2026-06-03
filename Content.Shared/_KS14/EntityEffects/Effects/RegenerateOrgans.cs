using Content.Shared.Body.Components;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using Content.Shared._KS14.Klovnmed;

namespace Content.Shared._KS14.EntityEffects.Effects;

/// <inheritdoc cref="EntityEffectSystem{T,TEffect}"/>
public sealed partial class RegenerateOrgansEntityEffectSystem : EntityEffectSystem<BloodstreamComponent, RegenerateOrgans>
{
    [Dependency] private readonly OrganRegenerationSystem _organRegenerationSystem = default!;

    protected override void Effect(Entity<BloodstreamComponent> entity, ref EntityEffectEvent<RegenerateOrgans> args)
    {
        _organRegenerationSystem.RegenerateForBody(entity.Owner, maxCount: args.Effect.MaxRegenCount);
    }
}

/// <inheritdoc cref="EntityEffect"/>
public sealed partial class RegenerateOrgans : EntityEffectBase<RegenerateOrgans>
{
    /// <summary>
    ///     Maximum number of organs to regenerate at once.
    ///         Null for no maximum.
    /// </summary>
    [DataField]
    public int? MaxRegenCount = null;

    public override string? EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        if (MaxRegenCount == null)
            return Loc.GetString("entity-effect-guidebook-regenerateorgans-nomax");

        return Loc.GetString("entity-effect-guidebook-regenerateorgans-withmax", ("count", MaxRegenCount.Value));
    }
}
