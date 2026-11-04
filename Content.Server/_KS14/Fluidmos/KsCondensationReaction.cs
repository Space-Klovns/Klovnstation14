using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using Content.Shared.FixedPoint;
using JetBrains.Annotations;

namespace Content.Server._KS14.Fluidmos;

/// <summary>
///     Condenses a linked gas out of a tile's air into a puddle of its reagent, once its partial pressure passes
///         saturation by <see cref="KsFluidmos.SupersaturationMargin"/>: steam vented into a cold room fogs and leaves
///         water on the floor, and cryo leaks pool as liquid oxygen and nitrogen.
/// </summary>
/// <remarks>
///     <para>
///         The rate is Hertz–Knudsen over the tile's floor, one square metre, at the puddle coefficient, capped at the
///             excess over saturation. The margin keeps the puddle and the tile from trading the same vapor back and
///             forth against the puddle's own evaporation.
///     </para>
///     <para>
///         The energy is <c>BoilTileEffect</c>'s accounting in reverse: the condensed units take their sensible heat
///             into the puddle and release their latent heat into the air, which warms. The puddle is spilled on the
///             next update, outside atmos processing. Only tiles: pipes condense through the phase step.
///     </para>
///     <para>
///         Each prototype's maximum temperature is its gas's critical temperature, so nothing condenses above it.
///     </para>
/// </remarks>
[UsedImplicitly]
[DataDefinition]
public sealed partial class KsCondensationReaction : IGasReactionEffect
{
    [Dependency] private KsFluidmosPropertiesSystem _propertiesSystem = default!;
    [Dependency] private KsFluidmosTileSystem _tileSystem = default!;

    /// <summary>
    ///     The linked gas that condenses.
    /// </summary>
    [DataField(required: true)]
    public Gas Gas;

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        if (holder is not TileAtmosphere tileAtmosphere ||
            mixture.Immutable ||
            mixture.Volume <= 0f ||
            _propertiesSystem.ByGas[(int)Gas] is not { Volatile: true } properties)
            return ReactionResult.NoReaction;

        var temperature = mixture.Temperature;
        var condensedMoles = GetCondensingMoles(properties, mixture, Gas, atmosphereSystem.AtmosTime);
        if (condensedMoles <= 0f)
            return ReactionResult.NoReaction;

        var units = (float)FixedPoint2.New(condensedMoles / properties.MolesPerUnit);
        if (units <= 0f)
            return ReactionResult.NoReaction;

        condensedMoles = units * properties.MolesPerUnit;

        // The reverse of boiling: the liquid leaves with its sensible heat, and its latent heat stays in the air.
        var heatCapacity = atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);
        var energy = heatCapacity * temperature - units * properties.SpecificHeat * temperature + units * properties.LatentHeat;

        mixture.AdjustMoles(Gas, -condensedMoles);

        var heatCapacityAfter = atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);
        if (heatCapacityAfter > Atmospherics.MinimumHeatCapacity)
            mixture.Temperature = MathF.Max(energy / heatCapacityAfter, Atmospherics.TCMB);

        _tileSystem.QueueCondensate(tileAtmosphere.GridIndex, tileAtmosphere.GridIndices, properties.Id, units, temperature);
        return ReactionResult.Reacting;
    }

    /// <summary>
    ///     Moles of a linked gas that condense out of <paramref name="mixture"/> over <paramref name="seconds"/>: none at
    ///         or above the critical temperature or within the supersaturation margin, otherwise the Hertz–Knudsen flux
    ///         over one square metre, capped at the excess over saturation.
    /// </summary>
    public static float GetCondensingMoles(KsFluidReagentProperties properties, GasMixture mixture, Gas gas, float seconds)
    {
        var temperature = mixture.Temperature;
        if (mixture.Volume <= 0f || temperature <= 0f || temperature >= properties.CriticalTemperature)
            return 0f;

        var moles = mixture.GetMoles(gas);
        var partialPressure = moles * Atmospherics.R * temperature / mixture.Volume;
        var saturationPressure = KsPhaseMath.SaturationPressure(properties.VaporizationEntropy, properties.BoilingPoint, temperature);
        if (partialPressure <= saturationPressure * (1f + KsFluidmos.SupersaturationMargin))
            return 0f;

        // Condensing is the Hertz-Knudsen flux with its sign reversed, over the tile's floor.
        var flux = KsPhaseMath.HertzKnudsenFlux(KsFluidmos.PuddleHertzKnudsenAlpha, partialPressure - saturationPressure,
            properties.InverseSqrtTwoPiMR, temperature);
        var excessMoles = moles - saturationPressure * mixture.Volume / (Atmospherics.R * temperature);
        return MathF.Max(0f, MathF.Min(flux * seconds, excessMoles));
    }
}
