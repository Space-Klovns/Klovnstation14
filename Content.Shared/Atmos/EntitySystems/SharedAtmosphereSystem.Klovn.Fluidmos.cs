using Content.Shared._KS14.Fluidmos;
using Robust.Shared.Utility;

namespace Content.Shared.Atmos.EntitySystems;

public abstract partial class SharedAtmosphereSystem
{
    [Dependency] private KsFluidmosPropertiesSystem _ksFluidmosPropertiesSystem = default!;

    /// <summary>
    ///     A mixture's fluid heat capacity in the same scaled units as its gas's, or 0 without fluid.
    /// </summary>
    private float KsGetFluidHeatCapacity(GasMixture mixture)
    {
        if (mixture.Fluid is not { } fluid)
            return 0f;

        return fluid.GetHeatCapacity(_ksFluidmosPropertiesSystem) / HeatScale;
    }

    /// <summary>
    ///     A mixture's heat capacity from its gas alone, leaving out any fluid it carries.
    /// </summary>
    public float KsGetGasHeatCapacity(GasMixture mixture, bool applyScaling)
    {
        var scale = GetHeatCapacityCalculation(mixture.Moles, mixture.Immutable);
        return applyScaling ? scale : scale * HeatScale;
    }

    /// <summary>
    ///     The fluid half of <see cref="Merge"/>. A receiver that cannot hold fluid (a tile, say) must never be handed any:
    ///         every route from a device into a tile detaches it first. Reaching here means one did not, so this asserts
    ///         in Debug, logs, and drops the fluid.
    /// </summary>
    private void KsMergeFluid(GasMixture receiver, GasMixture giver)
    {
        if (giver.Fluid is not { } fluid || fluid.IsEmpty)
            return;

        if (!receiver.FluidCapable)
        {
            DebugTools.Assert(false, "Fluid merged into a mixture that cannot hold it; release it through KsFluidmosTileSystem first.");
            Log.Error($"Dropped {fluid.LiquidUnits} u of liquid and {fluid.VaporMoles} mol of vapor merged into a mixture that cannot hold fluid.");
            return;
        }

        receiver.Fluid ??= new KsFluidMixture(fluid.Count);
        receiver.Fluid.Add(fluid);
    }
}
