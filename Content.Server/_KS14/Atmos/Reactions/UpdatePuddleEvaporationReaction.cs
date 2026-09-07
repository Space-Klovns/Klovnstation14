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
///     Marks the puddle on this tile as evaporating, scaled by the moles of gas present and, optionally, by
///     how much thermal energy the mixture has past a threshold.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class UpdatePuddleEvaporationReaction : IGasReactionEffect
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private PuddleSystem _puddleSystem = default!;

    /// <summary>
    ///     Gas to count moles of. Null counts the whole mixture.
    /// </summary>
    [DataField] public Gas? Gas = null;

    /// <summary>
    ///     Baseline evaporation units per mole, before any energy scaling.
    /// </summary>
    [DataField] public FixedPoint2 UnitsPerMole = FixedPoint2.Zero;

    /// <summary>
    ///     Thermal energy, in joules, that the mixture has to hold past the threshold to add another
    ///     +1 unit per mole on top of <see cref="UnitsPerMole"/>. Null disables energy scaling entirely.
    /// </summary>
    /// <remarks>
    ///     Scaling on thermal energy rather than raw temperature keeps this roughly physical - what actually
    ///     boils a puddle is the heat the tile's gas can hand it, which a thin hot mixture has very little of.
    ///     Note that this is the heat-scaled energy (see atmos.heat_scale), matching the energy that
    ///     gasReaction's minimumEnergy gate is checked against.
    /// </remarks>
    [DataField("joulesPerScale")] public float? JoulesPerBonusUnitPerMole = null;

    /// <summary>
    ///     Temperature the scaling measures excess energy from, in kelvin.
    ///     Converted to joules with the mixture's heat capacity so it can be compared against
    ///     <see cref="ScalingMinimumEnergy"/>; the lower of the two is used as the threshold.
    ///     Mirror the parent gasReaction's minimumTemperature here when it has one.
    /// </summary>
    [DataField] public float? ScalingMinimumTemperature = null;

    /// <summary>
    ///     Thermal energy the scaling measures excess energy from, in joules.
    ///     Mirror the parent gasReaction's minimumEnergy here when it has one.
    ///     See <see cref="ScalingMinimumTemperature"/>.
    /// </summary>
    [DataField] public float? ScalingMinimumEnergy = null;

    /// <summary>
    ///     Cap on the scaled units per mole, so a fusion-hot tile doesn't flash an ocean in one atmos tick.
    /// </summary>
    [DataField("maximumScale")] public FixedPoint2 MaximumUnitsPerMole = FixedPoint2.MaxValue;

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        if (holder is not TileAtmosphere tileAtmosphere ||
            !_puddleSystem.TryGetCachedPuddle(tileAtmosphere.GridIndex, tileAtmosphere.GridIndices, out var puddleEntity))
            return ReactionResult.NoReaction;

        var moles = Gas.HasValue ? mixture.GetMoles(Gas.Value) : mixture.TotalMoles;
        var unitsPerMole = FixedPoint2.Min(UnitsPerMole + GetBonusUnitsPerMole(mixture, atmosphereSystem), MaximumUnitsPerMole);

        var evaporatingComponent = _entityManager.EnsureComponent<AtmosEvaporatingPuddleComponent>(puddleEntity);
        evaporatingComponent.EvaporationAmount = FixedPoint2.Max(evaporatingComponent.EvaporationAmount, unitsPerMole * moles);
        _entityManager.Dirty(puddleEntity, evaporatingComponent);

        _puddleSystem.UpdateEvaporation(puddleEntity, puddleEntity.Comp.Solution!.Value.Comp.Solution /* bro wtf */);
        return ReactionResult.Reacting;
    }

    /// <returns>
    ///     Units per mole to add on top of <see cref="UnitsPerMole"/>, zero when scaling is disabled or the
    ///     mixture is sitting at or below the threshold.
    /// </returns>
    private FixedPoint2 GetBonusUnitsPerMole(GasMixture mixture, AtmosphereSystem atmosphereSystem)
    {
        if (JoulesPerBonusUnitPerMole is not { } joulesPerBonusUnitPerMole || joulesPerBonusUnitPerMole <= 0f)
            return FixedPoint2.Zero;

        var heatCapacity = atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true); // matches what the gasReaction minimumEnergy check uses
        var thermalEnergy = atmosphereSystem.GetThermalEnergy(mixture, heatCapacity);

        // Both thresholds are optional; whichever ends up lower in joules is what we measure the excess from.
        var thresholdEnergy = float.PositiveInfinity;
        if (ScalingMinimumTemperature is { } minimumTemperature)
            thresholdEnergy = minimumTemperature * heatCapacity;
        if (ScalingMinimumEnergy is { } minimumEnergy)
            thresholdEnergy = MathF.Min(thresholdEnergy, minimumEnergy);
        if (float.IsPositiveInfinity(thresholdEnergy))
            thresholdEnergy = 0f;

        var excessEnergy = MathF.Max(0f, thermalEnergy - thresholdEnergy);
        return FixedPoint2.New(excessEnergy / joulesPerBonusUnitPerMole);
    }
}
