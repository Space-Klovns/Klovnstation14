using System.Collections.Frozen;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Fluidmos;
using Content.Shared._KS14.Fluidmos.Reactions;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Fluidmos.Reactions;

/// <summary>
///     Runs <see cref="KsFluidReactionPrototype"/>s inside fluid-capable mixtures, called from the phase step just before
///         it moves reagents between phases, so whatever a reaction makes settles into its phase on the same tick.
/// </summary>
/// <remarks>
///     <para>
///         Cheap on a net with nothing to do: the candidate reactions are cached on the mixture against the reagents it
///             holds, a mixture colder than every candidate's minimum returns after one comparison, and the rest step at
///             most once per <see cref="StepInterval"/>.
///     </para>
///     <para>
///         Energy is conserved as the phase step keeps it: every reagent's energy is its heat capacity times the
///             temperature, plus the heat of fusion while liquid, plus the latent heat as well while vapor. So a
///             reaction that turns liquid into vapor pays to boil it, and its <see cref="KsFluidReactionPrototype.Enthalpy"/>
///             is only the chemistry.
///     </para>
/// </remarks>
public sealed partial class KsFluidReactionSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private KsFluidmosPropertiesSystem _propertiesSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;

    /// <summary>
    ///     Least time between two reaction steps of one mixture.
    /// </summary>
    public static readonly TimeSpan StepInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>
    ///     Longest time step a single reaction step takes, however long the mixture went unprocessed.
    /// </summary>
    private const float MaxStepSeconds = 2f;

    /// <summary>
    ///     Time step for a mixture's first reaction step, which has no previous one to measure from.
    /// </summary>
    private const float FirstStepSeconds = 0.5f;

    /// <summary>
    ///     The most, in kelvin, one reaction's step may change a mixture's temperature by, so a runaway heats over several
    ///         ticks rather than in one jump.
    /// </summary>
    public const float MaxReactionDeltaT = 20f;

    /// <summary>
    ///     Reaction units below which a step is not worth making.
    /// </summary>
    private const float MinExtent = 0.0001f;

    private FrozenDictionary<ProtoId<ReagentPrototype>, KsFluidReactionPrototype[]> _byReactant =
        FrozenDictionary<ProtoId<ReagentPrototype>, KsFluidReactionPrototype[]>.Empty;

    private FrozenDictionary<ProtoId<KsFluidCatalystPrototype>, KsFluidReactionPrototype[]> _byCatalyst =
        FrozenDictionary<ProtoId<KsFluidCatalystPrototype>, KsFluidReactionPrototype[]>.Empty;

    private readonly HashSet<KsFluidReactionPrototype> _candidateScratch = new();

    /// <summary>
    ///     Which build of the index a mixture's cached candidates came from, as those hold prototype instances that a
    ///         reload replaces.
    /// </summary>
    private int _indexVersion;

    public override void Initialize()
    {
        base.Initialize();

        RebuildIndex();
    }

    [SubscribeLocalEvent]
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<KsFluidReactionPrototype>())
            RebuildIndex();
    }

    private void RebuildIndex()
    {
        _indexVersion++;
        var byReactant = new Dictionary<ProtoId<ReagentPrototype>, List<KsFluidReactionPrototype>>();
        var byCatalyst = new Dictionary<ProtoId<KsFluidCatalystPrototype>, List<KsFluidReactionPrototype>>();
        foreach (var reaction in ProtoMan.EnumeratePrototypes<KsFluidReactionPrototype>())
        {
            // Catalysed reactions run only from their reactor, never on their own.
            if (reaction.Catalyst is { } catalyst)
            {
                if (!byCatalyst.TryGetValue(catalyst, out var catalysed))
                    byCatalyst[catalyst] = catalysed = new List<KsFluidReactionPrototype>();

                catalysed.Add(reaction);
                continue;
            }

            foreach (var reactant in reaction.Reactants.Keys)
            {
                if (!byReactant.TryGetValue(reactant, out var list))
                    byReactant[reactant] = list = new List<KsFluidReactionPrototype>();

                list.Add(reaction);
            }
        }

        _byReactant = byReactant.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        _byCatalyst = byCatalyst.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    /// <summary>
    ///     Runs every reaction <paramref name="catalyst"/> catalyses on <paramref name="mixture"/>, for a catalytic
    ///         reactor in its net.
    /// </summary>
    /// <param name="deltaTime">
    ///     Seconds of reaction, already scaled by how much of the net the bed fills and its activity.
    /// </param>
    /// <returns>Whether any of them got anywhere.</returns>
    public bool ReactCatalysed(GasMixture mixture, ProtoId<KsFluidCatalystPrototype> catalyst, float deltaTime)
    {
        if (deltaTime <= 0f || mixture.Fluid is not { IsEmpty: false } fluid || !_byCatalyst.TryGetValue(catalyst, out var reactions))
            return false;

        var reacted = false;
        foreach (var reaction in reactions)
        {
            reacted |= Step(mixture, fluid, reaction, deltaTime);
        }

        return reacted;
    }

    /// <summary>
    ///     Takes <paramref name="units"/> of a reagent from every phase in proportion, its linked gas included.
    /// </summary>
    public void RemoveUnits(GasMixture mixture, KsFluidMixture fluid, ProtoId<ReagentPrototype> reagent, float units)
    {
        if (units > 0f && _propertiesSystem.TryGetProperties(reagent, out var properties))
            Consume(mixture, fluid, properties, KsFluidReactionPhase.Any, units);
    }

    /// <summary>
    ///     One reaction step for <paramref name="mixture"/>, whose fluid is <paramref name="fluid"/>.
    /// </summary>
    public void React(GasMixture mixture, KsFluidMixture fluid, IGasMixtureHolder? holder)
    {
        if (fluid.ReactionCandidatesStale(_indexVersion))
            RefreshCandidates(fluid);

        if (fluid.ReactionCandidates.Length == 0 || mixture.Temperature < fluid.ReactionFloorTemperature)
        {
            fluid.LastReactionTime = null;
            return;
        }

        var now = _gameTiming.CurTime;
        float deltaTime;
        if (fluid.LastReactionTime is { } lastTime)
        {
            if (now - lastTime < StepInterval)
                return;

            deltaTime = MathF.Min((float)(now - lastTime).TotalSeconds, MaxStepSeconds);
        }
        else
        {
            deltaTime = FirstStepSeconds;
        }

        fluid.LastReactionTime = now;

        foreach (var reaction in fluid.ReactionCandidates)
        {
            Step(mixture, fluid, reaction, deltaTime);
        }
    }

    private void RefreshCandidates(KsFluidMixture fluid)
    {
        _candidateScratch.Clear();
        for (var i = 0; i < fluid.Reagents.Count; i++)
        {
            if (!_byReactant.TryGetValue(fluid.Reagents.GetKey(i).Prototype, out var reactions))
                continue;

            foreach (var reaction in reactions)
            {
                _candidateScratch.Add(reaction);
            }
        }

        if (_candidateScratch.Count == 0)
        {
            fluid.SetReactionCandidates([], float.PositiveInfinity, _indexVersion);
            return;
        }

        var candidates = new KsFluidReactionPrototype[_candidateScratch.Count];
        _candidateScratch.CopyTo(candidates);
        // A stable order, so a mixture with several reactions steps them the same way every time.
        Array.Sort(candidates, (a, b) => string.CompareOrdinal(a.ID, b.ID));

        var floor = float.PositiveInfinity;
        foreach (var reaction in candidates)
        {
            floor = MathF.Min(floor, reaction.MinTemp);
        }

        fluid.SetReactionCandidates(candidates, floor, _indexVersion);
        _candidateScratch.Clear();
    }

    private bool Step(GasMixture mixture, KsFluidMixture fluid, KsFluidReactionPrototype reaction, float deltaTime)
    {
        var temperature = mixture.Temperature;
        if (temperature < reaction.MinTemp || temperature > reaction.MaxTemp)
            return false;

        if (reaction.CatalystReagent is { } catalystReagent && TotalUnits(mixture, fluid, catalystReagent) < reaction.CatalystReagentUnits)
            return false;

        foreach (var (reagent, minimumPressure) in reaction.MinPartialPressure)
        {
            if (PartialPressure(mixture, fluid, reagent) < minimumPressure)
                return false;
        }

        // How many units of reaction the limiting reactant allows, and the energy one unit of reaction takes, phase
        //     changes included.
        var limit = float.PositiveInfinity;
        var energyPerExtent = reaction.Enthalpy;
        var heatCapacityPerExtent = 0f;
        var leastExtent = MinExtent;

        foreach (var (reagent, reactant) in reaction.Reactants)
        {
            if (!_propertiesSystem.TryGetProperties(reagent, out var properties))
                return false;

            var available = Available(mixture, fluid, properties, reactant.Phase);
            if (available.Total <= 0f)
                return false;

            limit = MathF.Min(limit, available.Total / reactant.Amount);
            energyPerExtent -= reactant.Amount * available.MeanEnergyOffset(properties);
            heatCapacityPerExtent -= reactant.Amount * available.MeanHeatCapacity(properties);
        }

        var pressure = mixture.Pressure;
        foreach (var (reagent, product) in reaction.Products)
        {
            if (!_propertiesSystem.TryGetProperties(reagent, out var properties))
                return false;

            var phase = ProductPhase(properties, product.Phase, temperature, pressure);
            energyPerExtent += product.Amount * EnergyOffset(properties, phase);
            heatCapacityPerExtent += product.Amount * HeatCapacity(properties, phase);
            leastExtent = MathF.Max(leastExtent, LeastExtentToKeep(fluid, properties, phase) / product.Amount);
        }

        var rateConstant = reaction.Rate;
        if (reaction.ActivationTemperature != 0f)
        {
            var exponent = reaction.ActivationTemperature * (1f / reaction.ReferenceTemperature - 1f / temperature);
            rateConstant *= MathF.Exp(Math.Clamp(exponent, -50f, 20f));
        }

        var stepExtent = limit * (1f - MathF.Exp(-rateConstant * deltaTime));
        var extent = MathF.Min(stepExtent + fluid.DeferredExtent(reaction), limit);

        // The temperature moves by both the reaction's heat and what it does to the mixture's heat capacity.
        var temperatureDrive = MathF.Abs(energyPerExtent + heatCapacityPerExtent * temperature) + MaxReactionDeltaT * MathF.Abs(heatCapacityPerExtent);
        var heatCapacity = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: false);
        if (temperatureDrive > 0f && heatCapacity > 0f)
            extent = MathF.Min(extent, heatCapacity * MaxReactionDeltaT / temperatureDrive);

        if (float.IsNaN(extent))
            return false;

        // Too little to leave a product that a table would keep: wait until there's enough, rather than lose it.
        if (extent < leastExtent)
        {
            fluid.SetDeferredExtent(reaction, extent);
            return stepExtent > MinExtent;
        }

        fluid.SetDeferredExtent(reaction, 0f);

        var energy = heatCapacity * temperature - energyPerExtent * extent;

        foreach (var (reagent, reactant) in reaction.Reactants)
        {
            _propertiesSystem.TryGetProperties(reagent, out var properties);
            Consume(mixture, fluid, properties!, reactant.Phase, reactant.Amount * extent);
        }

        foreach (var (reagent, product) in reaction.Products)
        {
            _propertiesSystem.TryGetProperties(reagent, out var properties);
            Produce(mixture, fluid, properties!, ProductPhase(properties!, product.Phase, temperature, pressure), product.Amount * extent);
        }

        var heatCapacityAfter = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: false);
        if (heatCapacityAfter > 0f)
            mixture.Temperature = MathF.Max(energy / heatCapacityAfter, Atmospherics.TCMB);

        return true;
    }

    /// <summary>
    ///     Units of a reagent a reaction must make for the fluid to keep them: enough to clear the table's minimum for that
    ///         phase, or nothing if it's already there. A linked gas has no minimum.
    /// </summary>
    private static float LeastExtentToKeep(KsFluidMixture fluid, KsFluidReagentProperties properties, KsFluidReactionPhase phase)
    {
        if (phase == KsFluidReactionPhase.Vapor && properties.Linked)
            return 0f;

        fluid.TryGetAmount(new KsFluidReagentKey(properties.Id, null), out var amount);
        return phase switch
        {
            KsFluidReactionPhase.Solid => amount.Solid > 0f ? 0f : KsFluidmos.MinLiquidUnits,
            KsFluidReactionPhase.Vapor => amount.Vapor > 0f ? 0f : KsFluidmos.MinVaporMoles / properties.MolesPerUnit,
            _ => amount.Liquid > 0f ? 0f : KsFluidmos.MinLiquidUnits,
        };
    }

    /// <summary>
    ///     A reagent's heat capacity, per unit, in a phase.
    /// </summary>
    private static float HeatCapacity(KsFluidReagentProperties properties, KsFluidReactionPhase phase)
    {
        return phase == KsFluidReactionPhase.Vapor ? properties.VaporHeatCapacityPerUnit : properties.SpecificHeat;
    }

    /// <summary>
    ///     A reagent's energy, per unit, in a phase on top of its heat capacity times the temperature: nothing as solid,
    ///         its heat of fusion as liquid, and its latent heat as well as vapor. The offsets the phase step moves.
    /// </summary>
    private static float EnergyOffset(KsFluidReagentProperties properties, KsFluidReactionPhase phase)
    {
        return phase switch
        {
            KsFluidReactionPhase.Liquid => properties.FusionHeat,
            KsFluidReactionPhase.Vapor => properties.FusionHeat + properties.LatentHeat,
            _ => 0f,
        };
    }

    private static KsFluidReactionPhase ProductPhase(KsFluidReagentProperties properties, KsFluidReactionPhase phase, float temperature, float pressure)
    {
        if (phase != KsFluidReactionPhase.Any)
            return phase;

        if (properties.Volatile &&
            temperature > KsPhaseMath.BoilingPointAt(properties.VaporizationEntropy, properties.BoilingPoint, pressure))
            return KsFluidReactionPhase.Vapor;

        return KsFluidReactionPhase.Liquid;
    }

    /// <summary>
    ///     Units of a reagent a reactant can take from each phase. Only its entry without extra data takes part.
    /// </summary>
    private static Availability Available(GasMixture mixture, KsFluidMixture fluid, KsFluidReagentProperties properties, KsFluidReactionPhase phase)
    {
        fluid.TryGetAmount(new KsFluidReagentKey(properties.Id, null), out var amount);
        var vaporUnits = (properties.Linked ? mixture.GetMoles(properties.GasIndex) : amount.Vapor) / properties.MolesPerUnit;

        return phase switch
        {
            KsFluidReactionPhase.Solid => new Availability(amount.Solid, 0f, 0f),
            KsFluidReactionPhase.Liquid => new Availability(0f, amount.Liquid, 0f),
            KsFluidReactionPhase.Vapor => new Availability(0f, 0f, vaporUnits),
            _ => new Availability(amount.Solid, amount.Liquid, vaporUnits),
        };
    }

    /// <summary>
    ///     Takes <paramref name="units"/> of a reactant, from each phase it reacts from in proportion to what is there.
    /// </summary>
    private static void Consume(GasMixture mixture, KsFluidMixture fluid, KsFluidReagentProperties properties, KsFluidReactionPhase phase, float units)
    {
        var available = Available(mixture, fluid, properties, phase);
        if (available.Total <= 0f)
            return;

        var fraction = MathF.Min(units / available.Total, 1f);
        var key = new KsFluidReagentKey(properties.Id, null);
        fluid.TryGetAmount(key, out var amount);

        amount.Solid -= available.Solid * fraction;
        amount.Liquid -= available.Liquid * fraction;

        var vaporMoles = available.Vapor * fraction * properties.MolesPerUnit;
        if (properties.Linked)
            mixture.AdjustMoles(properties.GasIndex, -MathF.Min(vaporMoles, mixture.GetMoles(properties.GasIndex)));
        else
            amount.Vapor = MathF.Max(0f, amount.Vapor - vaporMoles);

        amount.Solid = MathF.Max(0f, amount.Solid);
        amount.Liquid = MathF.Max(0f, amount.Liquid);
        fluid.SetAmount(key, amount);
    }

    private static void Produce(GasMixture mixture, KsFluidMixture fluid, KsFluidReagentProperties properties, KsFluidReactionPhase phase, float units)
    {
        if (phase == KsFluidReactionPhase.Vapor && properties.Linked)
        {
            mixture.AdjustMoles(properties.GasIndex, units * properties.MolesPerUnit);
            return;
        }

        var key = new KsFluidReagentKey(properties.Id, null);
        fluid.TryGetAmount(key, out var amount);

        switch (phase)
        {
            case KsFluidReactionPhase.Solid:
                amount.Solid += units;
                break;
            case KsFluidReactionPhase.Vapor:
                amount.Vapor += units * properties.MolesPerUnit;
                break;
            default:
                amount.Liquid += units;
                break;
        }

        fluid.SetAmount(key, amount);
    }

    /// <summary>
    ///     Units of a reagent in every phase together, its linked gas included.
    /// </summary>
    public float TotalUnits(GasMixture mixture, KsFluidMixture fluid, ProtoId<ReagentPrototype> reagent)
    {
        if (!_propertiesSystem.TryGetProperties(reagent, out var properties))
            return 0f;

        return Available(mixture, fluid, properties, KsFluidReactionPhase.Any).Total;
    }

    /// <summary>
    ///     Partial pressure, in kPa, of a reagent's vapor in the mixture's free volume.
    /// </summary>
    public float PartialPressure(GasMixture mixture, KsFluidMixture fluid, ProtoId<ReagentPrototype> reagent)
    {
        if (!_propertiesSystem.TryGetProperties(reagent, out var properties))
            return 0f;

        var vaporMoles = Available(mixture, fluid, properties, KsFluidReactionPhase.Vapor).Vapor * properties.MolesPerUnit;
        return vaporMoles * Atmospherics.R * mixture.Temperature / mixture.FreeVolume;
    }

    private readonly record struct Availability(float Solid, float Liquid, float Vapor)
    {
        public float Total => Solid + Liquid + Vapor;

        /// <summary>
        ///     The energy offset of a unit taken in proportion from every phase here.
        /// </summary>
        public float MeanEnergyOffset(KsFluidReagentProperties properties)
        {
            var total = Total;
            if (total <= 0f)
                return 0f;

            return (Liquid * EnergyOffset(properties, KsFluidReactionPhase.Liquid) +
                    Vapor * EnergyOffset(properties, KsFluidReactionPhase.Vapor)) / total;
        }

        /// <summary>
        ///     The heat capacity of a unit taken in proportion from every phase here.
        /// </summary>
        public float MeanHeatCapacity(KsFluidReagentProperties properties)
        {
            var total = Total;
            if (total <= 0f)
                return 0f;

            return ((Solid + Liquid) * HeatCapacity(properties, KsFluidReactionPhase.Liquid) +
                    Vapor * HeatCapacity(properties, KsFluidReactionPhase.Vapor)) / total;
        }
    }
}
