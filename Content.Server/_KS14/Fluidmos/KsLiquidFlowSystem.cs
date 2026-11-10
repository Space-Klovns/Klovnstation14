using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;

namespace Content.Server._KS14.Fluidmos;

/// <summary>
///     The orifice flow law for liquid crossing a passive device: <c>Q = conductance √ΔP</c> units per second, liquid
///         only. Without it liquid would ride along with the gas ratio, and a gate between two nets of pure liquid would
///         barely move anything, since there is almost no gas pressure difference between them.
/// </summary>
/// <remarks>
///     Pumps don't use this: they force flow, and move liquid with the gas through
///         <see cref="KsFluidmos.LiquidPseudoMolesPerUnit"/>.
/// </remarks>
public sealed partial class KsLiquidFlowSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;

    /// <summary>
    ///     Bisection steps for the most liquid that may move without reversing the pressure difference.
    /// </summary>
    private const int CapIterations = 12;

    /// <summary>
    ///     Moves liquid from <paramref name="inlet"/> to <paramref name="outlet"/> for <paramref name="deltaTime"/>
    ///         seconds, if the inlet is at the higher pressure.
    /// </summary>
    /// <param name="conductance">Units per second per √kPa.</param>
    /// <param name="minimumInletPressure">
    ///     Lowest pressure, in kPa, the inlet may be brought down to, for a device that holds its inlet at a threshold.
    /// </param>
    /// <returns>Units of liquid moved.</returns>
    public float Transfer(GasMixture inlet, GasMixture outlet, float conductance, float deltaTime, float minimumInletPressure = 0f)
    {
        if (conductance <= 0f ||
            deltaTime <= 0f ||
            inlet.Fluid is not { } inletFluid ||
            inletFluid.LiquidUnits <= KsFluidmos.MinLiquidUnits ||
            !outlet.FluidCapable ||
            outlet.Immutable)
            return 0f;

        var inletPressure = inlet.Pressure;
        var pressureDifference = inletPressure - outlet.Pressure;
        if (pressureDifference <= 0f || inletPressure <= minimumInletPressure)
            return 0f;

        var units = MathF.Min(conductance * MathF.Sqrt(pressureDifference) * deltaTime, inletFluid.LiquidUnits);
        units = CapToPressureBalance(inlet, outlet, units, minimumInletPressure);
        if (units <= KsFluidmos.MinLiquidUnits)
            return 0f;

        _atmosphereSystem.Merge(outlet, inlet.RemoveLiquidRatio(units / inletFluid.LiquidUnits));
        return units;
    }

    /// <summary>
    ///     The most of <paramref name="units"/> that leaves the inlet's pressure at or above the outlet's, and at or above
    ///         <paramref name="minimumInletPressure"/>. Found by
    ///         bisection on each side's pressure as a function of the liquid it would hold, rather than by linearising, as
    ///         near the hydraulic floor a tiny volume swings pressure by thousands of kPa and a linear estimate overshoots,
    ///         sending the liquid back and forth between the nets every update. Taking the lower bound means it never
    ///         overshoots.
    /// </summary>
    private static float CapToPressureBalance(GasMixture inlet, GasMixture outlet, float units, float minimumInletPressure)
    {
        var inletCondensed = inlet.Fluid!.CondensedUnits;
        var outletCondensed = outlet.Fluid?.CondensedUnits ?? 0f;

        if (Allowed(units))
            return units;

        var low = 0f;
        var high = units;
        for (var i = 0; i < CapIterations; i++)
        {
            var middle = (low + high) * 0.5f;
            if (Allowed(middle))
                low = middle;
            else
                high = middle;
        }

        return low;

        bool Allowed(float moved)
        {
            return PressureDifference(inlet, outlet, inletCondensed, outletCondensed, moved) >= 0f &&
                   inlet.KsPressureWithCondensedUnits(inletCondensed - moved) >= minimumInletPressure;
        }
    }

    private static float PressureDifference(GasMixture inlet, GasMixture outlet, float inletCondensed, float outletCondensed, float units)
    {
        return inlet.KsPressureWithCondensedUnits(inletCondensed - units) - outlet.KsPressureWithCondensedUnits(outletCondensed + units);
    }
}
