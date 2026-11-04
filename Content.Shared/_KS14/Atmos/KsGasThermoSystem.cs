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

        return totalMoles > 0f ? HeatCapacityRatio(heatCapacity / totalMoles) : 0f;
    }

    /// <summary>
    ///     A mixture's van der Waals constants, mixed by <see cref="KsThrottling.MixConstants"/>.
    /// </summary>
    public (float A, float B) MixtureVanDerWaals(GasMixture mixture)
    {
        return KsThrottling.MixConstants(mixture.Moles, SqrtVanDerWaalsA, VanDerWaalsB);
    }

    private void Rebuild()
    {
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
    }
}
