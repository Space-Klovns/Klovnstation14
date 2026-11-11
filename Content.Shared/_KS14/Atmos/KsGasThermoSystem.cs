using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Atmos;

/// <summary>
///     Per-gas constants for the thermodynamics of transfers, in arrays indexed by gas as atmos keeps its specific heats:
///         van der Waals <c>a</c>, <c>√a</c> and <c>b</c> for Joule–Thomson, and each gas's molar <c>Cv</c> for the heat
///         capacity ratio. Rebuilt whenever gases are reloaded.
/// </summary>
public sealed partial class KsGasThermoSystem : EntitySystem
{
    [Dependency] private KsFluidmosPropertiesSystem _fluidmosPropertiesSystem = default!;

    /// <summary>
    ///     The monatomic limit. No gas compresses hotter than this ratio says.
    /// </summary>
    public const float MaxHeatCapacityRatio = 5f / 3f;

    /// <summary>
    ///     Van der Waals <c>a</c>, kPa L²/mol², or 0 for an ideal gas.
    /// </summary>
    public float[] VanDerWaalsA { get; private set; } = new float[Atmospherics.TotalNumberOfGases];

    public float[] SqrtVanDerWaalsA { get; private set; } = new float[Atmospherics.TotalNumberOfGases];

    /// <summary>
    ///     Van der Waals <c>b</c>, L/mol, or 0 for an ideal gas.
    /// </summary>
    public float[] VanDerWaalsB { get; private set; } = new float[Atmospherics.TotalNumberOfGases];

    /// <summary>
    ///     Molar heat capacity, J/(mol K), that sets each gas's heat capacity ratio: its unscaled atmos molar heat capacity,
    ///         or <c>R / (γ - 1)</c> where the gas overrides γ.
    /// </summary>
    public float[] RatioHeatCapacities { get; private set; } = new float[Atmospherics.TotalNumberOfGases];

    /// <summary>
    ///     Kelvin above which each gas can't be liquid at any pressure, <c>8a / (27 R b)</c>, or infinity for an ideal gas.
    /// </summary>
    public float[] CriticalTemperatures { get; private set; } = CreateInfinite();

    /// <summary>
    ///     kPa at each gas's critical point, <c>a / (27 b²)</c>, or infinity for an ideal gas.
    /// </summary>
    public float[] CriticalPressures { get; private set; } = CreateInfinite();

    public override void Initialize()
    {
        base.Initialize();

        Rebuild();
    }

    [SubscribeLocalEvent]
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<GasPrototype>())
            Rebuild();
    }

    /// <summary>
    ///     A gas's effective heat capacity ratio, <c>1 + R / Cv</c>, clamped to <see cref="MaxHeatCapacityRatio"/>.
    /// </summary>
    public static float HeatCapacityRatio(float molarHeatCapacity)
    {
        if (molarHeatCapacity <= 0f)
            return MaxHeatCapacityRatio;

        return MathF.Min(1f + Atmospherics.R / molarHeatCapacity, MaxHeatCapacityRatio);
    }

    /// <summary>
    ///     The heat capacity ratio of a mixture's gas: <c>Cv</c> mole-weighted over <see cref="RatioHeatCapacities"/>,
    ///         then <see cref="HeatCapacityRatio(float)"/>. Zero if it holds no gas.
    /// </summary>
    public float MixtureHeatCapacityRatio(GasMixture mixture)
    {
        var moles = mixture.Moles;
        var totalMoles = 0f;
        var heatCapacity = 0f;
        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            var gasMoles = moles[i];
            if (gasMoles <= 0f)
                continue;

            totalMoles += gasMoles;
            heatCapacity += gasMoles * RatioHeatCapacities[i];
        }

        // Unlinked vapor has no gas to take a ratio from, so its molar heat capacity stands in, as for a gas with no override.
        if (mixture.Fluid is { } fluid)
        {
            totalMoles += fluid.VaporMoles;
            heatCapacity += fluid.GetVaporHeatCapacity(_fluidmosPropertiesSystem);
        }

        return totalMoles > 0f ? HeatCapacityRatio(heatCapacity / totalMoles) : 0f;
    }

    /// <summary>
    ///     A mixture's van der Waals constants, mixed by <see cref="KsThrottling.MixConstants"/>.
    /// </summary>
    public (float A, float B) MixtureVanDerWaals(GasMixture mixture)
    {
        var (a, b) = KsThrottling.MixConstants(mixture.Moles, SqrtVanDerWaalsA, VanDerWaalsB);

        // Unlinked vapor has no constants of its own, so it only dilutes, as a gas with none does.
        if (mixture.Fluid is { VaporMoles: > 0f } fluid)
        {
            var gasFraction = mixture.TotalMoles / (mixture.TotalMoles + fluid.VaporMoles);
            a *= gasFraction * gasFraction;
            b *= gasFraction;
        }

        return (a, b);
    }

    /// <summary>
    ///     How dense a mixture's supercritical gas is, as a solvent: the sum of <see cref="KsThrottling.ReducedDensity"/>
    ///         over the gases above their critical temperature, each at its own partial density. 1 is about each gas's
    ///         real critical density. A gas below its critical temperature condenses rather than becoming dense, and an
    ///         ideal gas has no constants, so neither counts.
    /// </summary>
    /// <param name="inverseVolume">
    ///     One over the litres the gas occupies: the free volume's, or for a packet flowing at a line pressure, that
    ///         pressure over <c>R T</c> times the packet's vapor moles.
    /// </param>
    public float ReducedSolventDensity(GasMixture mixture, float temperature, float inverseVolume)
    {
        if (inverseVolume <= 0f || !float.IsFinite(inverseVolume))
            return 0f;

        var moles = mixture.Moles;
        var density = 0f;
        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            var gasMoles = moles[i];
            if (gasMoles <= 0f || temperature <= CriticalTemperatures[i])
                continue;

            density += KsThrottling.ReducedDensity(VanDerWaalsA[i], VanDerWaalsB[i], gasMoles * inverseVolume, temperature);
        }

        return density;
    }

    /// <summary>
    ///     <see cref="ReducedSolventDensity(GasMixture, float, float)"/> of a mixture's own gas, in its free volume.
    /// </summary>
    public float ReducedSolventDensity(GasMixture mixture)
    {
        var freeVolume = mixture.FreeVolume;
        return freeVolume > 0f ? ReducedSolventDensity(mixture, mixture.Temperature, 1f / freeVolume) : 0f;
    }

    /// <summary>
    ///     How many times over a supercritical solvent in <paramref name="mixture"/> raises a reagent's vapor pressure, at
    ///         an affinity of 1: 1 when it holds none. For readouts; a polar reagent such as water gets far less.
    /// </summary>
    public float SolventEnhancement(GasMixture mixture)
    {
        var density = ReducedSolventDensity(mixture);
        return density > 1f ? MathF.Exp(KsPhaseMath.SupercriticalLogEnhancement(density, affinity: 1f)) : 1f;
    }

    private static float[] CreateInfinite()
    {
        var values = new float[Atmospherics.TotalNumberOfGases];
        Array.Fill(values, float.PositiveInfinity);
        return values;
    }

    private void Rebuild()
    {
        var criticalTemperatures = CreateInfinite();
        var criticalPressures = CreateInfinite();
        var a = new float[Atmospherics.TotalNumberOfGases];
        var sqrtA = new float[Atmospherics.TotalNumberOfGases];
        var b = new float[Atmospherics.TotalNumberOfGases];
        var ratioHeatCapacities = new float[Atmospherics.TotalNumberOfGases];

        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            if (!ProtoMan.TryIndex<GasPrototype>(((Gas)i).ToString(), out var gasPrototype))
                continue;

            a[i] = MathF.Max(gasPrototype.KsVanDerWaalsA ?? 0f, 0f);
            sqrtA[i] = MathF.Sqrt(a[i]);
            b[i] = MathF.Max(gasPrototype.KsVanDerWaalsB ?? 0f, 0f);
            if (a[i] > 0f && b[i] > 0f)
            {
                criticalTemperatures[i] = KsThrottling.CriticalTemperature(a[i], b[i]);
                criticalPressures[i] = KsThrottling.CriticalPressure(a[i], b[i]);
            }

            if (gasPrototype.KsHeatCapacityRatio is { } ratio)
            {
                if (ratio > 1f)
                    ratioHeatCapacities[i] = Atmospherics.R / (ratio - 1f);
                else
                    Log.Error($"Gas {gasPrototype.ID} has a heat capacity ratio of {ratio}, which must be above 1; ignoring it.");
            }

            if (ratioHeatCapacities[i] <= 0f)
                ratioHeatCapacities[i] = gasPrototype.MolarHeatCapacity;
        }

        VanDerWaalsA = a;
        SqrtVanDerWaalsA = sqrtA;
        VanDerWaalsB = b;
        RatioHeatCapacities = ratioHeatCapacities;
        CriticalTemperatures = criticalTemperatures;
        CriticalPressures = criticalPressures;
    }
}
