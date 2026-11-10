using Content.Shared.Atmos;

namespace Content.Shared._KS14.Atmos;

/// <summary>
///     Real-gas corrections at throttles, from van der Waals constants used only through the second virial coefficient
///         <c>B(T) = b - a / (R T)</c>. Pressure stays ideal-gas everywhere; these only move temperature.
/// </summary>
public static class KsThrottling
{
    /// <summary>
    ///     Pressure drops larger than this, in kPa, are integrated in <see cref="Substeps"/> steps, since the
    ///         Joule–Thomson coefficient depends on temperature.
    /// </summary>
    public const float SubstepPressure = 500f;

    public const int Substeps = 4;

    /// <summary>
    ///     Kelvin above which no liquid forms at any pressure: <c>8a / (27 R b)</c>.
    /// </summary>
    public static float CriticalTemperature(float a, float b)
    {
        return 8f * a / (27f * Atmospherics.R * b);
    }

    /// <summary>
    ///     Kelvin above which throttling warms rather than cools: <c>2a / (R b)</c>.
    /// </summary>
    public static float InversionTemperature(float a, float b)
    {
        return 2f * a / (Atmospherics.R * b);
    }

    /// <summary>
    ///     Joule–Thomson coefficient, in K/kPa, for mixed van der Waals constants and a molar heat capacity.
    /// </summary>
    /// <remarks>1 kPa L = 1 J, so <c>μ ΔP</c> comes out in kelvin.</remarks>
    public static float JouleThomsonCoefficient(float mixedA, float mixedB, float molarHeatCapacity, float temperature)
    {
        if (molarHeatCapacity <= 0f || temperature <= 0f)
            return 0f;

        return (2f * mixedA / (Atmospherics.R * temperature) - mixedB) / molarHeatCapacity;
    }

    /// <summary>
    ///     Mixes van der Waals constants over a mole array: <c>a = (Σ x √a)²</c>, <c>b = Σ x b</c>. Gases without
    ///         constants count as ideal (zero) and only dilute.
    /// </summary>
    public static (float A, float B) MixConstants(float[] moles, float[] sqrtA, float[] b)
    {
        var totalMoles = 0f;
        var sumSqrtA = 0f;
        var sumB = 0f;

        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            var gasMoles = moles[i];
            if (gasMoles <= 0f)
                continue;

            totalMoles += gasMoles;
            sumSqrtA += gasMoles * sqrtA[i];
            sumB += gasMoles * b[i];
        }

        if (totalMoles <= 0f)
            return (0f, 0f);

        var mixedSqrtA = sumSqrtA / totalMoles;
        return (mixedSqrtA * mixedSqrtA, sumB / totalMoles);
    }

    /// <summary>
    ///     Temperature after throttling from <paramref name="pressureIn"/> to <paramref name="pressureOut"/> kPa, integrated
    ///         in substeps for large drops.
    /// </summary>
    public static float ThrottledTemperature(
        float mixedA,
        float mixedB,
        float molarHeatCapacity,
        float temperature,
        float pressureIn,
        float pressureOut)
    {
        var pressureChange = pressureOut - pressureIn;
        if (mixedA <= 0f && mixedB <= 0f)
            return temperature;

        var steps = MathF.Abs(pressureChange) > SubstepPressure ? Substeps : 1;
        var stepChange = pressureChange / steps;

        for (var i = 0; i < steps; i++)
        {
            temperature += JouleThomsonCoefficient(mixedA, mixedB, molarHeatCapacity, temperature) * stepChange;
            temperature = MathF.Max(temperature, Atmospherics.TCMB);
        }

        return temperature;
    }

    /// <summary>
    ///     Temperature of a packet after real (inefficient) adiabatic compression from <paramref name="pressureIn"/> to
    ///         <paramref name="pressureOut"/> kPa: <c>T1 + T1 ((P2/P1)^((γ-1)/γ) - 1) / η</c>.
    /// </summary>
    public static float CompressedTemperature(float heatCapacityRatio, float efficiency, float temperature, float pressureIn, float pressureOut)
    {
        if (pressureOut <= pressureIn || heatCapacityRatio <= 1f || efficiency <= 0f)
            return temperature;

        var ratio = pressureOut / MathF.Max(pressureIn, MinimumCompressionInletPressure);
        var idealRise = temperature * (MathF.Pow(ratio, (heatCapacityRatio - 1f) / heatCapacityRatio) - 1f);
        return temperature + idealRise / efficiency;
    }

    /// <summary>
    ///     Inlet pressure floor, in kPa, so pumping from near-vacuum does not produce a huge compression ratio.
    /// </summary>
    public const float MinimumCompressionInletPressure = 1f;
}
