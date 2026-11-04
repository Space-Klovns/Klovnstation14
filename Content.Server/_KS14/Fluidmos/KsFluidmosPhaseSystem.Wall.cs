using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;

namespace Content.Server._KS14.Fluidmos;

/// <summary>
///     How a wall exchanges heat with its surroundings, in unscaled watts. Bounds the latent heat it can carry off.
/// </summary>
/// <param name="Conductance">W/K to <paramref name="ConvectionTemperature"/>; zero with nothing around the wall.</param>
/// <param name="RadiativeCoefficient">W/K⁴ to <paramref name="RadiationTemperature"/>.</param>
public readonly record struct KsWallHeatTransfer(
    float Conductance,
    float ConvectionTemperature,
    float RadiativeCoefficient,
    float RadiationTemperature)
{
    /// <summary>
    ///     Watts out of a wall held at <paramref name="wallTemperature"/>; negative flows into it.
    /// </summary>
    public float Power(float wallTemperature)
    {
        var radiationDelta = wallTemperature - RadiationTemperature;
        return Conductance * (wallTemperature - ConvectionTemperature) +
               RadiativeCoefficient * radiationDelta * MathF.Abs(radiationDelta * radiationDelta * radiationDelta);
    }
}

public sealed partial class KsFluidmosPhaseSystem
{
    /// <summary>
    ///     Condenses or boils a packet passing a wall, such as a radiator's, at its line's
    ///         <paramref name="linePressure"/> and its own temperature: the wall carries the latent heat to or from its
    ///         surroundings, so a condenser's vapor condenses inside it rather than heating the next pipe.
    /// </summary>
    /// <remarks>
    ///     The packet is held at a constant pressure, as gas flowing along a pipe is, so its partial pressures come from
    ///         <paramref name="linePressure"/> and its mole fractions rather than from its own volume, which is the
    ///         whole net's. Each reagent changes phase towards equilibrium, as far as <paramref name="deltaTime"/> of the
    ///         wall's heat transfer at that reagent's plateau temperature pays for. Liquid only: freezing and sublimation
    ///         are left to the phase step.
    /// </remarks>
    /// <returns>Unscaled joules given to the surroundings: positive from condensing, negative into boiling.</returns>
    public float ExchangeAtWall(GasMixture packet, float linePressure, KsWallHeatTransfer wall, float deltaTime)
    {
        if (packet.Immutable || !packet.FluidCapable || linePressure <= 0f || deltaTime <= 0f)
            return 0f;

        var temperature = packet.Temperature;
        var fluid = packet.Fluid;
        if (fluid == null)
        {
            if (!AnyLinkedGasCondenses(packet, temperature, linePressure))
                return 0f;

            fluid = packet.Fluid = new KsFluidMixture();
        }

        GatherEntries(packet, fluid);

        var liquidMoles = 0f;
        for (var i = 0; i < _entries.Count; i++)
        {
            fluid.TryGetAmount(_entries[i].Key, out var amount);
            liquidMoles += amount.Liquid * _entries[i].Properties.MolesPerUnit;
        }

        var vaporTotal = packet.TotalMoles + fluid.VaporMoles;
        var heatOut = 0f;

        for (var i = 0; i < _entries.Count; i++)
        {
            var (key, properties) = _entries[i];
            if (!properties.Volatile)
                continue;

            fluid.TryGetAmount(key, out var amount);
            var molesPerUnit = properties.MolesPerUnit;
            var vaporMoles = properties.Linked ? packet.GetMoles(properties.GasIndex) : amount.Vapor;
            if (amount.Liquid <= 0f && vaporMoles <= 0f)
                continue;

            var moleFraction = amount.Liquid > 0f && liquidMoles > 0f ? amount.Liquid * molesPerUnit / liquidMoles : 1f;
            var saturationPressure = moleFraction * KsPhaseMath.SaturationPressure(properties.VaporizationEntropy, properties.BoilingPoint, temperature);

            // At constant pressure: the vapor whose share of the packet's vapor gives the saturation pressure. Past the
            //      line pressure, it all boils.
            var otherVapor = vaporTotal - vaporMoles;
            var equilibriumMoles = saturationPressure < linePressure
                ? saturationPressure * otherVapor / (linePressure - saturationPressure)
                : float.PositiveInfinity;
            var gap = equilibriumMoles - vaporMoles;
            if (gap < 0f && temperature >= properties.CriticalTemperature)
                continue;

            // The wall sits at the plateau the phase change holds it at: the dew point of the packet's vapor.
            var partialPressure = vaporTotal > 0f ? linePressure * vaporMoles / vaporTotal : 0f;
            var plateau = KsPhaseMath.BoilingPointAt(properties.VaporizationEntropy, properties.BoilingPoint, partialPressure / moleFraction);
            var power = wall.Power(plateau);

            // Condensing needs the wall to shed heat, and boiling needs it to take some in.
            if (gap < 0f ? power <= 0f : power >= 0f)
                continue;

            var heatPerMole = (properties.LatentHeat + temperature * (properties.VaporHeatCapacityPerUnit - properties.SpecificHeat)) / molesPerUnit;
            if (heatPerMole <= 0f)
                continue;

            var budgetMoles = MathF.Abs(power) * deltaTime / heatPerMole;
            var moles = gap < 0f
                ? -MathF.Min(MathF.Min(-gap, vaporMoles), budgetMoles)
                : MathF.Min(MathF.Min(gap, amount.Liquid * molesPerUnit), budgetMoles);
            if (moles == 0f || !float.IsFinite(moles))
                continue;

            amount.Liquid -= moles / molesPerUnit;
            if (properties.Linked)
                packet.AdjustMoles(properties.GasIndex, moles);
            else
                amount.Vapor += moles;

            fluid.SetAmount(key, amount);
            vaporTotal += moles;
            heatOut -= moles * heatPerMole;
        }

        // The wall took or gave the latent heat, so the packet keeps its temperature.
        packet.Temperature = temperature;
        if (fluid.IsEmpty)
            packet.Fluid = null;

        return heatOut;
    }
}
