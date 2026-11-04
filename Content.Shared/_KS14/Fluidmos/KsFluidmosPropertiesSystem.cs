using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.Atmos;
using Content.Shared._KS14.Fluidmos.Mixing;
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

    /// <summary>
    ///     Margules <c>A</c> at 298 K between each pair of mixing families, by family index, symmetric.
    /// </summary>
    private float[,] _familyInteractions = new float[0, 0];

    /// <summary>
    ///     Whether each pair of mixing families is immiscible, by family index, symmetric.
    /// </summary>
    private bool[,] _familyImmiscible = new bool[0, 0];

    /// <summary>
    ///     Margules <c>A</c> at 298 K for named pairs of reagents, under both orders.
    /// </summary>
    private Dictionary<(ProtoId<ReagentPrototype>, ProtoId<ReagentPrototype>), float> _pairInteractions = new();

    /// <summary>
    ///     Every reagent named in a pair interaction.
    /// </summary>
    private HashSet<ProtoId<ReagentPrototype>> _pairedReagents = new();

    public override void Initialize()
    {
        base.Initialize();

        Rebuild();
    }

    [SubscribeLocalEvent]
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<ReagentPrototype>() ||
            args.WasModified<GasPrototype>() ||
            args.WasModified<KsFluidMixingFamilyPrototype>() ||
            args.WasModified<KsFluidPairInteractionPrototype>())
            Rebuild();
    }

    public bool TryGetProperties(ProtoId<ReagentPrototype> reagent, [NotNullWhen(true)] out KsFluidReagentProperties? properties)
    {
        return _properties.TryGetValue(reagent, out properties);
    }

    /// <summary>
    ///     The Margules <c>A</c> at 298 K between two reagents: their named pair's, else their families', else zero.
    /// </summary>
    public float Interaction(KsFluidReagentProperties first, KsFluidReagentProperties second)
    {
        if (_pairInteractions.TryGetValue((first.Id, second.Id), out var margules))
            return margules;

        if (first.MixingFamily < 0 || second.MixingFamily < 0)
            return 0f;

        return _familyInteractions[first.MixingFamily, second.MixingFamily];
    }

    /// <summary>
    ///     Whether two reagents' liquids form separate phases.
    /// </summary>
    public bool Immiscible(KsFluidReagentProperties first, KsFluidReagentProperties second)
    {
        return first.MixingFamily >= 0 && second.MixingFamily >= 0 && _familyImmiscible[first.MixingFamily, second.MixingFamily];
    }

    private void Rebuild()
    {
        var properties = new Dictionary<ProtoId<ReagentPrototype>, KsFluidReagentProperties>();
        var byGas = new KsFluidReagentProperties?[Atmospherics.TotalNumberOfGases];
        var familyIndices = RebuildMixing();

        foreach (var reagentPrototype in ProtoMan.EnumeratePrototypes<ReagentPrototype>())
        {
            var entry = Build(reagentPrototype, familyIndices);
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

    /// <summary>
    ///     Rebuilds the family and pair tables, returning each family's index.
    /// </summary>
    private Dictionary<string, int> RebuildMixing()
    {
        var familyIndices = new Dictionary<string, int>();
        var families = new List<KsFluidMixingFamilyPrototype>();
        foreach (var family in ProtoMan.EnumeratePrototypes<KsFluidMixingFamilyPrototype>())
        {
            familyIndices[family.ID] = families.Count;
            families.Add(family);
        }

        var interactions = new float[families.Count, families.Count];
        var immiscible = new bool[families.Count, families.Count];
        for (var i = 0; i < families.Count; i++)
        {
            foreach (var (other, margules) in families[i].Interactions)
            {
                if (!familyIndices.TryGetValue(other, out var j))
                    continue;

                interactions[i, j] = margules;
                interactions[j, i] = margules;
            }

            foreach (var other in families[i].ImmiscibleWith)
            {
                if (!familyIndices.TryGetValue(other, out var j))
                    continue;

                immiscible[i, j] = true;
                immiscible[j, i] = true;
            }
        }

        var pairs = new Dictionary<(ProtoId<ReagentPrototype>, ProtoId<ReagentPrototype>), float>();
        var pairedReagents = new HashSet<ProtoId<ReagentPrototype>>();
        foreach (var pair in ProtoMan.EnumeratePrototypes<KsFluidPairInteractionPrototype>())
        {
            pairs[(pair.First, pair.Second)] = pair.Margules;
            pairs[(pair.Second, pair.First)] = pair.Margules;
            pairedReagents.Add(pair.First);
            pairedReagents.Add(pair.Second);
        }

        _familyInteractions = interactions;
        _familyImmiscible = immiscible;
        _pairInteractions = pairs;
        _pairedReagents = pairedReagents;
        return familyIndices;
    }

    private KsFluidReagentProperties Build(ReagentPrototype reagentPrototype, Dictionary<string, int> familyIndices)
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

        var mixingFamily = -1;
        if (data?.MixingFamily is { } family && !familyIndices.TryGetValue(family, out mixingFamily))
            mixingFamily = -1;

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
            FusionEntropy = data?.FusionEntropy ?? KsFluidmos.DefaultFusionEntropy,
            GasIndex = gasIndex,
            CriticalTemperature = criticalTemperature,
            InverseSqrtTwoPiMR = KsPhaseMath.InverseSqrtTwoPiMR(molarMass),
            Color = reagentPrototype.SubstanceColor,
            MixingFamily = mixingFamily,
            HasInteractions = mixingFamily >= 0 || _pairedReagents.Contains(reagentPrototype.ID),
        };
    }
}
