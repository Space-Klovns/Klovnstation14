using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

namespace Content.Server._KS14.Atmos.Reactions;

/// <summary>
///     2 H2 + O2 -> 2 H2O. Tritium's fire, without the part that lets a little oxygen burn a lot of fuel: every mole of
///         hydrogen burned takes half a mole of oxygen and leaves one of steam.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class KsHydrogenFireReaction : IGasReactionEffect
{
    /// <summary>
    ///     The fraction of whatever can burn that burns each reaction tick.
    /// </summary>
    private const float BurnFraction = 0.25f;

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        mixture.ReactionResults[(byte)GasReaction.Fire] = 0f;

        var burnedHydrogen = MathF.Min(mixture.GetMoles(Gas.Hydrogen), mixture.GetMoles(Gas.Oxygen) * 2f) * BurnFraction;
        if (burnedHydrogen <= 0f)
            return ReactionResult.NoReaction;

        var temperature = mixture.Temperature;
        var oldHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);

        mixture.AdjustMoles(Gas.Hydrogen, -burnedHydrogen);
        mixture.AdjustMoles(Gas.Oxygen, -burnedHydrogen * 0.5f);
        mixture.AdjustMoles(Gas.WaterVapor, burnedHydrogen);
        mixture.ReactionResults[(byte)GasReaction.Fire] = burnedHydrogen;

        // Adjusted so that speedup doesn't cause a mega temperature rise.
        var energyReleased = Atmospherics.FireHydrogenEnergyReleased * burnedHydrogen / heatScale;
        var newHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);
        if (newHeatCapacity > Atmospherics.MinimumHeatCapacity)
            mixture.Temperature = (temperature * oldHeatCapacity + energyReleased) / newHeatCapacity;

        if (holder is TileAtmosphere location && mixture.Temperature > Atmospherics.FireMinimumTemperatureToExist)
            atmosphereSystem.HotspotExpose(location, mixture.Temperature, mixture.Volume);

        return ReactionResult.Reacting;
    }
}
