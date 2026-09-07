using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Fluids.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using Content.Shared.FixedPoint;
using JetBrains.Annotations;

namespace Content.Server._KS14.Atmos.Reactions;

/// <summary>
///     Synthesis of Evaporin from Tritium and Carbon Dioxide at high temperatures.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class UpdatePuddleEvaporationReaction : IGasReactionEffect
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private PuddleSystem _puddleSystem = default!;

    [DataField(required: true)] public Gas Gas;
    [DataField] public FixedPoint2 UnitsPerMole = FixedPoint2.New(1);

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        if (holder is not TileAtmosphere tileAtmosphere ||
            !_puddleSystem.TryGetCachedPuddle(tileAtmosphere.GridIndex, tileAtmosphere.GridIndices, out var puddleEntity))
            return ReactionResult.NoReaction;

        var evaporatingComponent = _entityManager.EnsureComponent<AtmosEvaporatingPuddleComponent>(puddleEntity);
        evaporatingComponent.EvaporationAmount = FixedPoint2.Max(evaporatingComponent.EvaporationAmount, UnitsPerMole * mixture.GetMoles(Gas));
        _entityManager.Dirty(puddleEntity, evaporatingComponent);

        _puddleSystem.UpdateEvaporation(puddleEntity, puddleEntity.Comp.Solution!.Value.Comp.Solution /* bro wtf */);
        return ReactionResult.Reacting;
    }
}
