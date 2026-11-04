using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.Atmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Fluidmos;

/// <summary>
///     Resolves every reagent's Fluidmos properties once, into a table the phase step and the mixture caches read without
///         touching the prototype manager. Rebuilt whenever reagents or gases are reloaded.
/// </summary>
public sealed partial class KsFluidmosPropertiesSystem : EntitySystem
{
    private Dictionary<ProtoId<ReagentPrototype>, KsFluidReagentProperties> _properties = new();

    /// <summary>
    ///     The reagent linked to each gas, indexed by gas, or null.
    /// </summary>
    public KsFluidReagentProperties?[] ByGas { get; private set; } = new KsFluidReagentProperties?[Atmospherics.TotalNumberOfGases];

    public override void Initialize()
    {
        base.Initialize();

        Rebuild();
    }

    [SubscribeLocalEvent]
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<ReagentPrototype>() || args.WasModified<GasPrototype>())
            Rebuild();
    }

    public bool TryGetProperties(ProtoId<ReagentPrototype> reagent, [NotNullWhen(true)] out KsFluidReagentProperties? properties)
    {
        return _properties.TryGetValue(reagent, out properties);
    }

    private void Rebuild()
    {
        var properties = new Dictionary<ProtoId<ReagentPrototype>, KsFluidReagentProperties>();
        var byGas = new KsFluidReagentProperties?[Atmospherics.TotalNumberOfGases];

        foreach (var reagentPrototype in ProtoMan.EnumeratePrototypes<ReagentPrototype>())
        {
            var entry = Build(reagentPrototype);
            properties[reagentPrototype.ID] = entry;

            if (!entry.Linked)
                continue;

            if (byGas[entry.GasIndex] is { } existing)
            {
                Log.Error($"Gas {(Gas)entry.GasIndex} is linked from both {existing.Id} and {entry.Id}; keeping {existing.Id}.");
                continue;
            }

            byGas[entry.GasIndex] = entry;
        }

        _properties = properties;
        ByGas = byGas;
    }

    private KsFluidReagentProperties Build(ReagentPrototype reagentPrototype)
    {
        var data = reagentPrototype.Fluidmos;
        var specificHeat = reagentPrototype.SpecificHeat;
        var gasIndex = -1;
        GasPrototype? gasPrototype = null;

        if (data?.Gas is { } gas)
        {
            if (ProtoMan.TryIndex<GasPrototype>(gas.ToString(), out gasPrototype))
                gasIndex = (int)gas;
            else
                Log.Error($"Reagent {reagentPrototype.ID} links to gas {gas}, which has no prototype.");
        }

        if (gasPrototype != null && data?.MolarMass != null)
            Log.Error($"Reagent {reagentPrototype.ID} is linked to {gasPrototype.ID} and sets its own molar mass; the gas's is used.");

        var molarMass = gasPrototype?.MolarMass ?? data?.MolarMass ?? KsFluidmos.DefaultMolarMass;
        var molesPerUnit = data?.MolesPerUnit ?? KsPhaseMath.MolesPerUnit(molarMass);
        var vaporizationEntropy = data?.VaporizationEntropy ?? KsFluidmos.DefaultVaporizationEntropy;

        var boilingPoint = reagentPrototype.BoilingPoint ?? 0f;
        var isVolatile = reagentPrototype.BoilingPoint != null && boilingPoint < KsFluidmos.NonVolatileBoilingPoint;
        if (isVolatile && boilingPoint <= 0f)
        {
            Log.Error($"Reagent {reagentPrototype.ID} has a non-positive boiling point {boilingPoint} K; treating it as non-volatile.");
            isVolatile = false;
        }

        var meltingPoint = reagentPrototype.MeltingPoint ?? 0f;
        var freezable = reagentPrototype.MeltingPoint != null && meltingPoint > 0f;

        var criticalTemperature = float.PositiveInfinity;
        if (gasPrototype is { KsVanDerWaalsA: { } a, KsVanDerWaalsB: { } b } && b > 0f)
            criticalTemperature = KsThrottling.CriticalTemperature(a, b);

        return new KsFluidReagentProperties
        {
            Id = reagentPrototype.ID,
            SpecificHeat = specificHeat,
            MolarMass = molarMass,
            MolesPerUnit = molesPerUnit,
            VaporHeatCapacityPerUnit = gasPrototype != null ? molesPerUnit * gasPrototype.MolarHeatCapacity : specificHeat,
            Volatile = isVolatile,
            BoilingPoint = boilingPoint,
            VaporizationEntropy = vaporizationEntropy,
            LatentHeat = data?.LatentHeat ?? KsPhaseMath.DefaultLatentHeat(boilingPoint, specificHeat),
            MinActiveTemperature = isVolatile ? KsPhaseMath.MinActiveTemperature(vaporizationEntropy, boilingPoint) : float.PositiveInfinity,
            Freezable = freezable,
            MeltingPoint = meltingPoint,
            FusionHeat = data?.FusionHeat ?? KsPhaseMath.DefaultFusionHeat(meltingPoint, specificHeat),
            GasIndex = gasIndex,
            CriticalTemperature = criticalTemperature,
            InverseSqrtTwoPiMR = KsPhaseMath.InverseSqrtTwoPiMR(molarMass),
            Color = reagentPrototype.SubstanceColor,
        };
    }
}
