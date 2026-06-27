using Content.Shared.Destructible;
using Content.Shared.EntityEffects;
using Content.Shared.Gibbing;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.EntityEffects.Effects;

/// <summary>
/// Adjust the damages on this entity by specified amounts.
/// Amounts are modified by scale.
/// </summary>
/// <inheritdoc cref="EntityEffectSystem{T,TEffect}"/>
public sealed partial class GibEntityEffectSystem : EntityEffectSystem<DestructibleComponent, Gib>
{
    [Dependency] private readonly GibbingSystem _gibbingSystem = default!;

    protected override void Effect(Entity<DestructibleComponent> entity, ref EntityEffectEvent<Gib> args)
    {
        _gibbingSystem.Gib(entity.Owner);
    }
}

/// <inheritdoc cref="EntityEffect"/>
public sealed partial class Gib : EntityEffectBase<Gib>
{
    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("entity-effect-guidebook-gib", ("chance", Probability));
}
