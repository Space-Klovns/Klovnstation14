using Content.Shared.Atmos;

namespace Content.Shared._KS14.Fluidmos;

/// <summary>
///     The phase-change maths shared by pipes (Fluidmos) and puddles (<c>BoilTileEffect</c>), so the two cannot drift.
/// </summary>
/// <remarks>
///     One vapor-pressure curve everywhere: Clausius–Clapeyron anchored at the reagent's boiling point at one
///         atmosphere, <c>Psat = P0 exp(k (1 - Tb / T))</c>, where <c>k = ΔH_vap / (R Tb)</c> is the reagent's
///         vaporization entropy <c>ΔS_vap / R</c>: a real per-mole value, separate from the game-scale latent heat
///         that sets how much energy a phase change moves.
/// </remarks>
public static class KsPhaseMath
{
    /// <summary>
    ///     Pressure, in kPa, that the boiling point is anchored at.
    /// </summary>
    public const float ReferencePressure = Atmospherics.OneAtmosphere;

    /// <summary>
    ///     Lowest pressure, in kPa, <see cref="BoilingPointAt"/> evaluates, so vacuum gives a finite boiling point.
    /// </summary>
    public const float MinimumBoilingPressure = 0.1f;

    /// <summary>
    ///     Saturation vapor pressure, in kPa, at <paramref name="temperature"/>.
    /// </summary>
    public static float SaturationPressure(float vaporizationEntropy, float boilingPoint, float temperature)
    {
        if (temperature <= 0f)
            return 0f;

        var exponent = Math.Clamp(vaporizationEntropy * (1f - boilingPoint / temperature), -50f, 20f);
        return ReferencePressure * MathF.Exp(exponent);
    }

    /// <summary>
    ///     Boiling point, in kelvin, at <paramref name="pressure"/> kPa: the inverse of <see cref="SaturationPressure"/>.
    /// </summary>
    public static float BoilingPointAt(float vaporizationEntropy, float boilingPoint, float pressure)
    {
        pressure = MathF.Max(pressure, MinimumBoilingPressure);
        var inverse = 1f / boilingPoint - MathF.Log(pressure / ReferencePressure) / (vaporizationEntropy * boilingPoint);
        // Only reachable at absurd pressures; past it the curve has no boiling point left.
        return inverse > 0f ? 1f / inverse : float.PositiveInfinity;
    }

    /// <summary>
    ///     Temperature at which the saturation pressure falls to <see cref="KsFluidmos.MinActivePressure"/>, below which
    ///         a reagent is treated as not evaporating.
    /// </summary>
    public static float MinActiveTemperature(float vaporizationEntropy, float boilingPoint)
    {
        var logRatio = MathF.Log(KsFluidmos.MinActivePressure / ReferencePressure);
        return boilingPoint / (1f - logRatio / vaporizationEntropy);
    }

    /// <summary>
    ///     Default latent heat of vaporisation, in joules per unit, on the game's specific-heat scale.
    /// </summary>
    public static float DefaultLatentHeat(float boilingPoint, float specificHeat)
    {
        return KsFluidmos.LatentHeatRatio * boilingPoint * specificHeat;
    }

    /// <summary>
    ///     Default heat of fusion, in joules per unit, on the game's specific-heat scale.
    /// </summary>
    public static float DefaultFusionHeat(float meltingPoint, float specificHeat)
    {
        return KsFluidmos.FusionHeatRatio * meltingPoint * specificHeat;
    }

    /// <summary>
    ///     Moles of vapor a unit becomes, treating a unit as <see cref="KsFluidmos.GramsPerUnit"/> grams.
    /// </summary>
    public static float MolesPerUnit(float molarMass)
    {
        return KsFluidmos.GramsPerUnit / molarMass;
    }

    /// <summary>
    ///     <c>1 / sqrt(2π M R)</c>, with <c>M</c> in kg/mol, for <see cref="HertzKnudsenFlux"/>. Cached per reagent so a
    ///         step needs only <c>sqrt(T)</c>.
    /// </summary>
    public static float InverseSqrtTwoPiMR(float molarMass)
    {
        return 1f / MathF.Sqrt(2f * MathF.PI * molarMass * 0.001f * Atmospherics.R);
    }

    /// <summary>
    ///     Net Hertz–Knudsen flux, in mol/(m² s): positive evaporates, negative condenses.
    /// </summary>
    /// <param name="alpha">The evaporation coefficient.</param>
    /// <param name="excessPressure">Equilibrium partial pressure minus the actual one, in kPa.</param>
    /// <param name="inverseSqrtTwoPiMR">From <see cref="InverseSqrtTwoPiMR"/>.</param>
    /// <param name="temperature">Kelvin.</param>
    public static float HertzKnudsenFlux(float alpha, float excessPressure, float inverseSqrtTwoPiMR, float temperature)
    {
        if (temperature <= 0f)
            return 0f;

        return alpha * excessPressure * 1000f * inverseSqrtTwoPiMR / MathF.Sqrt(temperature);
    }

    /// <summary>
    ///     Units that must change phase to bring a mixture from <paramref name="temperature"/> to
    ///         <paramref name="targetTemperature"/>, with energy conserved.
    /// </summary>
    /// <param name="heatCapacity">The mixture's heat capacity before, in J/K.</param>
    /// <param name="latentHeat">Energy each unit absorbs as it changes phase, in J (negative releases).</param>
    /// <param name="heatCapacityChangePerUnit">How much the mixture's heat capacity changes per unit converted, in J/K.</param>
    /// <remarks>
    ///     Solves <c>C T = (C + u ΔC) T' + u L</c> for <c>u</c> at <c>T' = target</c>.
    /// </remarks>
    public static float UnitsToReach(
        float heatCapacity,
        float temperature,
        float targetTemperature,
        float latentHeat,
        float heatCapacityChangePerUnit)
    {
        var denominator = latentHeat + targetTemperature * heatCapacityChangePerUnit;
        if (MathF.Abs(denominator) < 1e-6f)
            return 0f;

        return heatCapacity * (temperature - targetTemperature) / denominator;
    }

    /// <summary>
    ///     Temperature after <paramref name="units"/> change phase, with energy conserved: the inverse of
    ///         <see cref="UnitsToReach"/>.
    /// </summary>
    public static float TemperatureAfter(
        float heatCapacity,
        float temperature,
        float units,
        float latentHeat,
        float heatCapacityChangePerUnit)
    {
        var heatCapacityAfter = heatCapacity + units * heatCapacityChangePerUnit;
        if (heatCapacityAfter <= 0f)
            return temperature;

        return (heatCapacity * temperature - units * latentHeat) / heatCapacityAfter;
    }
}
