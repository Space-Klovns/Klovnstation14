using Content.Shared._KS14.Fluidmos;

namespace Content.Server._KS14.Fluidmos;

public sealed partial class KsFluidmosPhaseSystem
{
    /// <summary>
    ///     Pairwise Margules parameters and miscibility for one step, kept between steps: refinery pipes hold enough
    ///         reagents that a square table of them is too big for the stack.
    /// </summary>
    private float[] _margulesScratch = [];

    private bool[] _miscibleScratch = [];

    /// <summary>
    ///     Fills <paramref name="activities"/> with each entry's liquid activity, <c>x γ</c>: its share of the moles of the
    ///         liquid phase it is in, times its two-suffix Margules activity coefficient. An entry with no liquid gets 1,
    ///         being its own phase.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         With no reagent that mixes non-ideally, this is Raoult's mole fraction over all the liquid, exactly as
    ///             before activity coefficients existed, and costs no lookups.
    ///     </para>
    ///     <para>
    ///         Otherwise <c>ln γ_i = Σ_j A_ij x_j - ½ Σ_j Σ_k A_jk x_j x_k</c>, over the reagents in <c>i</c>'s liquid
    ///             phase. Reagents of immiscible families are in separate phases, so oil and water each keep their own
    ///             full vapor pressure; a reagent in no family dissolves in every phase.
    ///     </para>
    /// </remarks>
    /// <param name="freezingGammas">
    ///     Each entry's activity coefficient with its own solid counted as melted, as <see cref="EffectiveMeltingPoint"/>
    ///         counts it, and at infinite dilution for one with neither.
    /// </param>
    /// <param name="phaseMoles">Moles of liquid in the phase each entry would dissolve in, its own included.</param>
    private void ComputeActivities(
        ReadOnlySpan<KsFluidAmount> amounts,
        Span<float> activities,
        Span<float> freezingGammas,
        Span<float> phaseMoles,
        float temperature)
    {
        var count = _entries.Count;
        var moles = count <= MaxStackEntries ? stackalloc float[count] : new float[count];

        var totalMoles = 0f;
        var anyInteractions = false;
        for (var i = 0; i < count; i++)
        {
            moles[i] = amounts[i].Liquid * _entries[i].Properties.MolesPerUnit;
            totalMoles += moles[i];
            anyInteractions |= moles[i] > 0f && _entries[i].Properties.HasInteractions;
        }

        if (!anyInteractions)
        {
            for (var i = 0; i < count; i++)
            {
                activities[i] = moles[i] > 0f && totalMoles > 0f ? moles[i] / totalMoles : 1f;
                freezingGammas[i] = 1f;
                phaseMoles[i] = totalMoles;
            }

            return;
        }

        // The pairwise Margules parameters and miscibility, looked up once for the step.
        if (_margulesScratch.Length < count * count)
        {
            _margulesScratch = new float[count * count];
            _miscibleScratch = new bool[count * count];
        }

        var margules = _margulesScratch.AsSpan(0, count * count);
        var miscible = _miscibleScratch.AsSpan(0, count * count);
        for (var i = 0; i < count; i++)
        {
            margules[i * count + i] = 0f;
            miscible[i * count + i] = true;

            // The table is symmetric, so each pair is looked up once.
            for (var j = i + 1; j < count; j++)
            {
                var first = _entries[i].Properties;
                var second = _entries[j].Properties;
                margules[i * count + j] = margules[j * count + i] = KsPhaseMath.MargulesAt(_propertiesSystem.Interaction(first, second), temperature);
                miscible[i * count + j] = miscible[j * count + i] = !_propertiesSystem.Immiscible(first, second);
            }
        }

        for (var i = 0; i < count; i++)
        {
            var phase = 0f;
            for (var j = 0; j < count; j++)
            {
                if (miscible[i * count + j])
                    phase += moles[j];
            }

            phaseMoles[i] = phase;
            if (phase <= 0f)
            {
                activities[i] = 1f;
                freezingGammas[i] = 1f;
                continue;
            }

            var gamma = ActivityCoefficient(i, moles, margules, miscible, phase);
            activities[i] = moles[i] > 0f ? moles[i] / phase * gamma : 1f;
            freezingGammas[i] = gamma;

            // Its solid counts as melted here, as it does for the mole fraction that goes with this. Otherwise the
            //     coefficient would rise as a reagent froze out, and freezing would raise its own freezing point.
            var solidMoles = amounts[i].Solid * _entries[i].Properties.MolesPerUnit;
            if (solidMoles > 0f)
            {
                var liquidMoles = moles[i];
                moles[i] = liquidMoles + solidMoles;
                freezingGammas[i] = ActivityCoefficient(i, moles, margules, miscible, phase + solidMoles);
                moles[i] = liquidMoles;
            }
        }
    }

    /// <summary>
    ///     <c>γ_i</c> for the moles given, within the liquid phase of <paramref name="phase"/> moles that <c>i</c> is in. A
    ///         pair across immiscible families doesn't interact, even in the eyes of a reagent that dissolves in both.
    /// </summary>
    private static float ActivityCoefficient(int i, ReadOnlySpan<float> moles, ReadOnlySpan<float> margules, ReadOnlySpan<bool> miscible, float phase)
    {
        var count = moles.Length;
        var logGamma = 0f;
        for (var j = 0; j < count; j++)
        {
            if (moles[j] <= 0f || !miscible[i * count + j])
                continue;

            var fractionJ = moles[j] / phase;
            logGamma += margules[i * count + j] * fractionJ;

            // -½ Σ_j Σ_k over the pairs, which is -Σ over each unordered pair once.
            for (var k = j + 1; k < count; k++)
            {
                if (moles[k] <= 0f || !miscible[i * count + k] || !miscible[j * count + k])
                    continue;

                logGamma -= margules[j * count + k] * fractionJ * moles[k] / phase;
            }
        }

        return MathF.Exp(Math.Clamp(logGamma, -KsPhaseMath.MaxMargules, KsPhaseMath.MaxMargules));
    }

    /// <summary>
    ///     The temperature a boiling liquid flashes down to at <paramref name="pressure"/>: its bubble point, where the
    ///         reagents' activities times their saturation pressures sum to it. Infinity while the liquid is not boiling.
    /// </summary>
    /// <remarks>
    ///     A liquid of one reagent boils at that reagent's own boiling point at the pressure, as before mixtures had a
    ///         bubble point; a minimum-boiling azeotrope or an oil-and-water mixture boils below either of its parts.
    /// </remarks>
    /// <param name="flashWeight">The sum the flash's energy is shared out by: each reagent's activity times its saturation pressure.</param>
    private float BubbleTemperature(ReadOnlySpan<KsFluidAmount> amounts, ReadOnlySpan<float> activities, float temperature, float pressure, out float flashWeight)
    {
        var count = _entries.Count;
        var liquidActivities = count <= MaxStackEntries ? stackalloc float[count] : new float[count];
        var entropies = count <= MaxStackEntries ? stackalloc float[count] : new float[count];
        var boilingPoints = count <= MaxStackEntries ? stackalloc float[count] : new float[count];

        var sum = 0f;
        var boilingCount = 0;
        var single = -1;
        for (var i = 0; i < count; i++)
        {
            var properties = _entries[i].Properties;
            if (!properties.Volatile || amounts[i].Liquid <= 0f)
                continue;

            liquidActivities[i] = activities[i];
            entropies[i] = properties.VaporizationEntropy;
            boilingPoints[i] = properties.BoilingPoint;
            sum += activities[i] * KsPhaseMath.SaturationPressure(properties.VaporizationEntropy, properties.BoilingPoint, temperature);
            boilingCount++;
            single = i;
        }

        flashWeight = sum;
        if (sum <= pressure)
            return float.PositiveInfinity;

        // With one volatile reagent, its activity times its saturation pressure is the whole of the vapor pressure.
        if (boilingCount == 1)
        {
            var properties = _entries[single].Properties;
            return KsPhaseMath.BoilingPointAt(properties.VaporizationEntropy, properties.BoilingPoint, pressure / activities[single]);
        }

        return KsPhaseMath.BubbleTemperature(liquidActivities, entropies, boilingPoints, pressure, temperature);
    }

    /// <summary>
    ///     The temperature an entry's solid and its solution are in equilibrium at: its melting point, lowered by what is
    ///         dissolved in its liquid. <c>Tm' = Tm / (1 - ln(x γ) / k_fus)</c>, the freezing-point depression (and ideal
    ///         solubility) equation.
    /// </summary>
    /// <remarks>
    ///     <c>x</c> counts the entry's own solid as if it had melted, so a little ice in a lot of something else melts
    ///         only as far as the heat and the depression allow, not all at once; and it never falls below
    ///         <see cref="MinFreezingActivity"/>.
    /// </remarks>
    private static float EffectiveMeltingPoint(KsFluidReagentProperties properties, KsFluidAmount amount, float gamma, float phaseMoles)
    {
        var molesPerUnit = properties.MolesPerUnit;
        var ownMoles = (amount.Liquid + amount.Solid) * molesPerUnit;
        var solutionMoles = phaseMoles + amount.Solid * molesPerUnit;
        if (ownMoles <= 0f || solutionMoles <= 0f || properties.FusionEntropy <= 0f)
            return properties.MeltingPoint;

        var activity = Math.Clamp(ownMoles / solutionMoles * gamma, MinFreezingActivity, 1f);
        if (activity >= 1f)
            return properties.MeltingPoint;

        return properties.MeltingPoint / (1f - MathF.Log(activity) / properties.FusionEntropy);
    }

    /// <summary>
    ///     Least activity <see cref="EffectiveMeltingPoint"/> uses, so a trace of a reagent is not depressed without
    ///         limit.
    /// </summary>
    private const float MinFreezingActivity = 0.1f;
}
