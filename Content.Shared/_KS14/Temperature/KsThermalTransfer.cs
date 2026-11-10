namespace Content.Shared._KS14.Temperature;

/// <summary>
///     Heat flowing between two bodies through a conductance, by conduction, thermal radiation or both, stepped exactly
///         rather than by Euler steps.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Content.Shared.Temperature.HeatContainer.HeatContainerHelpers.ConductHeat{T1, T2}"/> takes one
///             Euler step, which needs the step to be well under <c>C / g</c>. A foam blanket holds a few J/K against a
///             puddle's conductance of about 100 W/K, so <c>C / g</c> is a few hundredths of a second, far under any
///             update interval, and an Euler step there flips the temperature difference back and forth. Here the
///             difference decays exponentially, as it does between two bodies with a constant conductance, so a step
///             of any length ends between the two temperatures and never past their shared equilibrium.
///     </para>
///     <para>
///         Radiation goes through the same step as a conductance, <see cref="RadiativeConductance"/>, taken at the
///             temperatures the step starts from. Radiation weakens as a hot body cools, so a long step cools it a
///             little too fast, but never past the equilibrium.
///     </para>
/// </remarks>
public static class KsThermalTransfer
{
    /// <summary>
    ///     The Stefan-Boltzmann constant, in W/(m²·K⁴).
    /// </summary>
    public const float StefanBoltzmann = 5.670374e-8f;

    /// <summary>
    ///     The conductance, in W/K, that radiation between two surfaces at these temperatures amounts to:
    ///         <c>εσA (T_A² + T_B²)(T_A + T_B)</c>, so that times <c>T_A - T_B</c> it is the net radiated power
    ///         <c>εσA (T_A⁴ - T_B⁴)</c>.
    /// </summary>
    /// <param name="area">The radiating area, in m².</param>
    /// <param name="emissivity">From 0, radiating nothing, to 1, a black body.</param>
    public static float RadiativeConductance(float temperatureA, float temperatureB, float area, float emissivity)
    {
        if (area <= 0f || emissivity <= 0f)
            return 0f;

        return emissivity * StefanBoltzmann * area
            * (temperatureA * temperatureA + temperatureB * temperatureB)
            * (temperatureA + temperatureB);
    }

    /// <summary>
    ///     The heat, in joules, that flows into body A from body B over <paramref name="seconds"/> through
    ///         <paramref name="conductance"/>: negative when A is the hotter. Changes neither body; apply it to both,
    ///         with opposite signs.
    /// </summary>
    /// <remarks>
    ///     <c>Q = (T_eq - T_A) C_A (1 - e^(-g t (1/C_A + 1/C_B)))</c>, where <c>T_eq</c> is the temperature both would
    ///         settle at.
    /// </remarks>
    /// <param name="conductance">In W/K; conduction and radiation may be added together.</param>
    public static float ExchangeQuery(float capacityA, float temperatureA, float capacityB, float temperatureB,
        float conductance, float seconds)
    {
        if (capacityA <= 0f || capacityB <= 0f || conductance <= 0f || seconds <= 0f)
            return 0f;

        var equilibrium = (capacityA * temperatureA + capacityB * temperatureB) / (capacityA + capacityB);
        var decay = 1f - MathF.Exp(-conductance * seconds * (1f / capacityA + 1f / capacityB));
        return (equilibrium - temperatureA) * capacityA * decay;
    }

    /// <summary>
    ///     The heat, in joules, that flows into a body from a reservoir that holds its temperature however much flows,
    ///         such as foam at its boiling point, which boils off rather than warming. Negative when the body is the
    ///         hotter. Changes nothing.
    /// </summary>
    /// <remarks>
    ///     <c>Q = (T_r - T) C (1 - e^(-g t / C))</c>: <see cref="ExchangeQuery"/> with the reservoir's capacity unbounded.
    /// </remarks>
    public static float ExchangeWithReservoirQuery(float capacity, float temperature, float reservoirTemperature,
        float conductance, float seconds)
    {
        if (capacity <= 0f || conductance <= 0f || seconds <= 0f)
            return 0f;

        return (reservoirTemperature - temperature) * capacity * (1f - MathF.Exp(-conductance * seconds / capacity));
    }
}
