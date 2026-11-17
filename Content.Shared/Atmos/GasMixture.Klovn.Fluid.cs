using Content.Shared._KS14.Fluidmos;

namespace Content.Shared.Atmos;

public sealed partial class GasMixture
{
    /// <summary>
    ///     Reagents this mixture carries as solid, liquid and unlinked vapor. Null unless any are present, which is the
    ///         case for every tile and every dry pipe, so they pay one null check. Only ever non-null when
    ///         <see cref="FluidCapable"/>; immutable mixtures always keep it null.
    /// </summary>
    [DataField]
    public KsFluidMixture? Fluid;

    /// <summary>
    ///     Whether this mixture may hold <see cref="Fluid"/>: set by its owner (pipe nets, canisters, tanks), and inherited
    ///         by packets split off it. Opt-in, because tile mixtures are reassigned in too many places for an opt-out to
    ///         be safe. Not saved; the owner sets it again.
    /// </summary>
    [ViewVariables]
    public bool FluidCapable;

    /// <summary>
    ///     Volume left to the gas, in litres, after liquid and solid displace their share. Never below
    ///         <see cref="KsFluidmos.MinFreeVolumeFraction"/> of the volume.
    /// </summary>
    [ViewVariables]
    public float FreeVolume
    {
        get
        {
            if (Fluid == null)
                return Volume;

            return MathF.Max(Volume - Fluid.CondensedUnits * KsFluidmos.LitersPerUnit,
                Volume * KsFluidmos.MinFreeVolumeFraction);
        }
    }

    /// <summary>
    ///     What a transfer divides by: gas moles, plus unlinked vapor, plus liquid weighted by
    ///         <see cref="KsFluidmos.LiquidPseudoMolesPerUnit"/>. <see cref="TotalMoles"/> stays gas-only, so gas
    ///         fractions keep their meaning.
    /// </summary>
    [ViewVariables]
    public float TransferMoles
    {
        get
        {
            if (Fluid == null)
                return TotalMoles;

            return TotalMoles + Fluid.VaporMoles + Fluid.LiquidUnits * KsFluidmos.LiquidPseudoMolesPerUnit;
        }
    }

    /// <summary>
    ///     Pressure with fluid present: gas and vapor over the free volume, plus a hydraulic term once liquid and solid
    ///         overfill past the free-volume floor.
    /// </summary>
    private float KsFluidPressure(KsFluidMixture fluid)
    {
        return KsPressureWithCondensedUnits(fluid.CondensedUnits);
    }

    /// <summary>
    ///     What <see cref="Pressure"/> would be if this mixture held <paramref name="condensedUnits"/> of liquid and solid,
    ///         with its gas, vapor and temperature unchanged. For flows that must not overshoot.
    /// </summary>
    public float KsPressureWithCondensedUnits(float condensedUnits)
    {
        if (Volume <= 0)
            return 0f;

        var condensedVolume = condensedUnits * KsFluidmos.LitersPerUnit;
        var freeVolume = MathF.Max(Volume - condensedVolume, Volume * KsFluidmos.MinFreeVolumeFraction);
        var pressure = (TotalMoles + (Fluid?.VaporMoles ?? 0f)) * Atmospherics.R * Temperature / freeVolume;

        var fullVolume = Volume * (1f - KsFluidmos.MinFreeVolumeFraction);
        if (condensedVolume > fullVolume)
            pressure += MathF.Min(KsFluidmos.HydraulicStiffness * (condensedVolume - fullVolume) / Volume * 100f,
                KsFluidmos.MaxHydraulicPressure);

        return pressure;
    }

    /// <summary>
    ///     Splits off <paramref name="ratio"/> of the liquid only, with no gas, at this mixture's temperature.
    /// </summary>
    public GasMixture RemoveLiquidRatio(float ratio)
    {
        var removed = new GasMixture(Volume) { Temperature = Temperature, FluidCapable = FluidCapable };
        if (Fluid == null || Immutable)
            return removed;

        removed.Fluid = Fluid.RemoveLiquidRatio(ratio);
        if (removed.Fluid.IsEmpty)
            removed.Fluid = null;

        return removed;
    }

    /// <summary>
    ///     Gives this mixture's fluid back to <paramref name="origin"/>, for draws that must take gas only (internals, gas
    ///         grenades), and leaves this mixture unable to hold fluid.
    /// </summary>
    public void ReturnFluidTo(GasMixture origin)
    {
        if (Fluid is { } fluid && !fluid.IsEmpty)
        {
            origin.Fluid ??= new KsFluidMixture(fluid.Count);
            origin.Fluid.Add(fluid);
        }

        Fluid = null;
        FluidCapable = false;
    }

    /// <summary>
    ///     The fluid half of <see cref="RemoveRatio"/>.
    /// </summary>
    private void KsSplitFluid(GasMixture removed, float ratio, bool includeLiquid)
    {
        removed.FluidCapable = FluidCapable;
        if (Fluid == null || Immutable || ratio <= 0f)
            return;

        var removedFluid = Fluid.RemoveRatio(ratio, includeLiquid);
        if (!removedFluid.IsEmpty)
            removed.Fluid = removedFluid;
    }
}
