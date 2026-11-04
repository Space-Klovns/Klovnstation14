using Content.Server._KS14.Fluidmos.Reactions;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Fluidmos;

/// <summary>
///     Moves reagents between solid, liquid and vapor inside fluid-capable atmos mixtures, once per <c>React</c>.
/// </summary>
/// <remarks>
///     <para>
///         One explicit step per call, never iterating or recursing, so the cost per tick is bounded:
///     </para>
///     <list type="bullet">
///         <item>
///             Below the boiling point at the mixture's pressure, evaporation and condensation follow one signed
///                 Hertz–Knudsen term against Raoult's-law equilibrium, capped so it never overshoots.
///         </item>
///         <item>
///             Above it, liquid flashes, limited only by energy, down to that boiling point (and to equilibrium).
///         </item>
///         <item>
///             Freezing and melting are energy-limited plateaus at the melting point; a solid with no liquid of its
///                 own sublimes.
///         </item>
///         <item>
///             Linked gases never condense above their critical temperature.
///         </item>
///     </list>
///     <para>
///         The temperature is recomputed from energy conservation, since a phase change also changes the heat
///             capacity: a unit of liquid is its specific heat, while its vapor is its moles times its gas's molar heat.
///     </para>
/// </remarks>
public sealed partial class KsFluidmosPhaseSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private KsFluidmosPropertiesSystem _propertiesSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsFluidReactionSystem _reactionSystem = default!;

    /// <summary>
    ///     The longest time step a single phase step takes, however long the mixture went unprocessed.
    /// </summary>
    private const float MaxStepSeconds = 2f;

    /// <summary>
    ///     Time step for a mixture's first phase step, which has no previous one to measure from.
    /// </summary>
    private const float FirstStepSeconds = 0.5f;

    /// <summary>
    ///     A step that moved the temperature less than this, and less than <see cref="SettledUnits"/> of any reagent,
    ///         leaves the mixture settled.
    /// </summary>
    private const float SettledTemperature = 0.05f;

    private const float SettledUnits = 0.001f;

    /// <summary>
    ///     Relative pressure change that wakes a settled mixture, such as gas merged in from outside.
    /// </summary>
    private const float SettledPressureFraction = 0.001f;

    /// <summary>
    ///     Most entries whose amounts a step keeps on the stack; more than this goes to the heap.
    /// </summary>
    private const int MaxStackEntries = 32;

    private readonly List<(KsFluidReagentKey Key, KsFluidReagentProperties Properties)> _entries = new();

    /// <summary>
    ///     One phase step for <paramref name="mixture"/>. Does nothing to mixtures that cannot hold fluid.
    /// </summary>
    public void UpdatePhases(GasMixture mixture, IGasMixtureHolder? holder)
    {
        if (mixture.Immutable || !mixture.FluidCapable)
            return;

        var fluid = mixture.Fluid;

        // Reactions first, so whatever they make settles into its phase on this same step.
        if (fluid != null)
            _reactionSystem.React(mixture, fluid, holder);

        var temperature = mixture.Temperature;
        var pressure = mixture.Pressure;

        if (fluid == null)
        {
            // A dry pipe only wakes for a linked gas past saturation, so pumped-in steam can condense.
            if (!AnyLinkedGasCondenses(mixture, temperature))
                return;

            fluid = mixture.Fluid = new KsFluidMixture();
        }
        else if (fluid.PhaseSettled &&
                 !fluid.CompositionChanged &&
                 MathF.Abs(temperature - fluid.LastTemperature) < SettledTemperature &&
                 MathF.Abs(pressure - fluid.LastPressure) <= SettledPressureFraction * MathF.Max(pressure, 1f))
        {
            return;
        }

        var now = _gameTiming.CurTime;
        var deltaTime = fluid.LastPhaseTime is { } lastTime
            ? MathF.Min((float)(now - lastTime).TotalSeconds, MaxStepSeconds)
            : FirstStepSeconds;
        fluid.LastPhaseTime = now;

        GatherEntries(mixture, fluid);

        var heatCapacity = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: false);
        var freeVolume = mixture.FreeVolume;
        var interfaceArea = mixture.Volume * KsFluidmos.PipeInterfaceAreaPerLiter;

        // Each entry's amounts, read once. A mixture holds a handful of reagents, so this lives on the stack.
        var entryCount = _entries.Count;
        var amounts = entryCount <= MaxStackEntries
            ? stackalloc KsFluidAmount[entryCount]
            : new KsFluidAmount[entryCount];

        for (var i = 0; i < entryCount; i++)
        {
            fluid.TryGetAmount(_entries[i].Key, out amounts[i]);
        }

        // Each reagent's activity in its liquid (Raoult's mole fraction, times its activity coefficient when it mixes
        //     non-ideally), and the bubble point the liquid flashes down to if it is boiling.
        var activities = entryCount <= MaxStackEntries ? stackalloc float[entryCount] : new float[entryCount];
        var freezingGammas = entryCount <= MaxStackEntries ? stackalloc float[entryCount] : new float[entryCount];
        var phaseMoles = entryCount <= MaxStackEntries ? stackalloc float[entryCount] : new float[entryCount];
        ComputeActivities(amounts, activities, freezingGammas, phaseMoles, temperature);
        var bubbleTemperature = BubbleTemperature(amounts, activities, temperature, pressure, out var flashWeight);

        var unitsMoved = 0f;
        var step = new PhaseStep(heatCapacity, temperature, pressure, freeVolume, interfaceArea, deltaTime, bubbleTemperature, flashWeight);

        for (var i = 0; i < entryCount; i++)
        {
            var (key, properties) = _entries[i];
            ref var amount = ref amounts[i];
            var before = amount;

            // What is dissolved in it lowers where it freezes; worked out from before this step moved anything.
            var meltingPoint = properties.Freezable
                ? EffectiveMeltingPoint(properties, before, freezingGammas[i], phaseMoles[i])
                : properties.MeltingPoint;

            var belowTriplePoint = IsBelowTriplePoint(properties, step.Pressure, amount.Liquid > 0f ? activities[i] : 1f, meltingPoint);

            if (properties.Volatile)
                Vaporise(mixture, ref step, properties, ref amount, activities[i], belowTriplePoint);

            if (properties.Freezable)
                Freeze(ref step, properties, ref amount, meltingPoint, belowTriplePoint);

            var moved = MathF.Abs(amount.Liquid - before.Liquid) + MathF.Abs(amount.Solid - before.Solid) +
                MathF.Abs(amount.Vapor - before.Vapor) / properties.MolesPerUnit;
            if (moved <= 0f)
                continue;

            unitsMoved += moved;
            fluid.SetAmount(key, amount);
        }

        mixture.Temperature = step.Temperature;

        if (fluid.IsEmpty)
        {
            mixture.Fluid = null;
            return;
        }

        fluid.LastTemperature = mixture.Temperature;
        fluid.LastPressure = mixture.Pressure;
        fluid.MarkPhaseStepped();
        fluid.PhaseSettled = MathF.Abs(mixture.Temperature - temperature) < SettledTemperature && unitsMoved < SettledUnits;
    }

    /// <summary>
    ///     The volatile reagents to step: every entry, plus each linked gas present with no entry of its own.
    /// </summary>
    private void GatherEntries(GasMixture mixture, KsFluidMixture fluid)
    {
        _entries.Clear();

        for (var i = 0; i < fluid.Reagents.Count; i++)
        {
            var key = fluid.Reagents.GetKey(i);
            if (_propertiesSystem.TryGetProperties(key.Prototype, out var properties))
                _entries.Add((key, properties));
        }

        var byGas = _propertiesSystem.ByGas;
        for (var gas = 0; gas < byGas.Length; gas++)
        {
            if (byGas[gas] is not { } properties || mixture.GetMoles(gas) <= KsFluidmos.MinVaporMoles)
                continue;

            var key = new KsFluidReagentKey(properties.Id, null);
            if (!fluid.Reagents.ContainsKey(key))
                _entries.Add((key, properties));
        }
    }

    /// <summary>
    ///     Whether any linked gas in a dry mixture is past saturation, below its critical temperature.
    /// </summary>
    /// <param name="linePressure">
    ///     The pressure a packet flows at, which sets its partial pressures by mole fraction. Without it, they come from
    ///         the mixture's own volume.
    /// </param>
    private bool AnyLinkedGasCondenses(GasMixture mixture, float temperature, float? linePressure = null)
    {
        var byGas = _propertiesSystem.ByGas;
        for (var gas = 0; gas < byGas.Length; gas++)
        {
            if (byGas[gas] is not { } properties)
                continue;

            var moles = mixture.GetMoles(gas);
            if (moles <= KsFluidmos.MinVaporMoles || temperature >= properties.CriticalTemperature || !properties.Volatile)
                continue;

            if (temperature < properties.MinActiveTemperature)
                return true;

            var partialPressure = linePressure is { } pressure
                ? pressure * moles / mixture.TotalMoles
                : moles * Atmospherics.R * temperature / mixture.Volume;
            if (partialPressure > KsPhaseMath.SaturationPressure(properties.VaporizationEntropy, properties.BoilingPoint, temperature))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether <paramref name="pressure"/> is below the reagent's triple point, where it boils below its melting point
    ///         and so has no stable liquid: carbon dioxide under about 5 atm. It goes straight between solid and gas there.
    ///         Dissolved, it boils higher and melts lower, so a solution holds as a liquid where the pure reagent can't.
    /// </summary>
    /// <param name="activity">Its activity in its liquid, which raises where it boils.</param>
    /// <param name="meltingPoint">Where it melts, lowered by what is dissolved in it.</param>
    private static bool IsBelowTriplePoint(KsFluidReagentProperties properties, float pressure, float activity, float meltingPoint)
    {
        return properties is { Freezable: true, Volatile: true } &&
               KsPhaseMath.BoilingPointAt(properties.VaporizationEntropy, properties.BoilingPoint, pressure / MathF.Min(activity, 1f)) < meltingPoint;
    }

    /// <summary>
    ///     Evaporates or condenses one reagent, or sublimes its solid when it has no liquid.
    /// </summary>
    /// <param name="belowTriplePoint">
    ///     Whether it has no stable liquid here (<see cref="IsBelowTriplePoint"/>): its solid sublimes whatever the
    ///         temperature, and its gas deposits straight back as solid, both at the Hertz–Knudsen rate.
    /// </param>
    /// <param name="activity">
    ///     Its activity in the liquid: Raoult's mole fraction, times its activity coefficient. Unused for a solid.
    /// </param>
    private void Vaporise(GasMixture mixture, ref PhaseStep step, KsFluidReagentProperties properties, ref KsFluidAmount amount,
        float activity, bool belowTriplePoint)
    {
        var molesPerUnit = properties.MolesPerUnit;
        var vaporMoles = properties.Linked ? mixture.GetMoles(properties.GasIndex) : amount.Vapor;
        var temperature = step.Temperature;

        // A solid with no liquid of its own sublimes, from its own phase. Below the triple point that is the only
        //     condensed phase there is, so with no liquid the gas deposits as solid too, even with none there yet.
        var fromSolid = amount.Liquid <= 0f &&
                        (belowTriplePoint ||
                         amount.Solid > 0f && (!properties.Freezable || temperature < properties.MeltingPoint));
        var sourceUnits = fromSolid ? amount.Solid : amount.Liquid;

        if (sourceUnits <= 0f && vaporMoles <= 0f)
            return;

        var canCondense = temperature < properties.CriticalTemperature;
        var latentHeat = fromSolid ? properties.LatentHeat + properties.FusionHeat : properties.LatentHeat;
        var heatCapacityChange = properties.VaporHeatCapacityPerUnit - properties.SpecificHeat;
        var maxLatentMoles = latentHeat > 0f
            ? step.HeatCapacity * KsFluidmos.MaxPhaseDeltaT / latentHeat * molesPerUnit
            : float.PositiveInfinity;
        float moles;

        if (temperature < properties.MinActiveTemperature)
        {
            // Its vapor pressure is negligible here: whatever vapor there is condenses.
            if (vaporMoles <= 0f || !canCondense)
                return;

            moles = -MathF.Min(vaporMoles, maxLatentMoles);
        }
        else
        {
            var saturationPressure = KsPhaseMath.SaturationPressure(properties.VaporizationEntropy, properties.BoilingPoint, temperature);

            // Its own phase when it has no liquid in the mixture yet: dissolving into another liquid is Henry's law,
            //     which this does not model.
            var moleFraction = !fromSolid && amount.Liquid > 0f ? activity : 1f;

            var equilibriumMoles = moleFraction * saturationPressure * step.FreeVolume / (Atmospherics.R * temperature);
            var gap = equilibriumMoles - vaporMoles;
            if (gap < 0f && !canCondense)
                return;

            // The liquid boils as a whole, at its bubble point: a single reagent's own boiling point at this pressure.
            var flashPoint = step.BubbleTemperature;
            if (!fromSolid && gap > 0f && temperature > flashPoint && amount.Liquid > 0f)
            {
                // Flash: limited by energy, down to the bubble point at this pressure, and by equilibrium. The energy
                //     is shared by what each reagent adds to the vapor pressure, so no reagent gets it for being first.
                var weight = moleFraction * KsPhaseMath.SaturationPressure(properties.VaporizationEntropy, properties.BoilingPoint, step.StartTemperature);
                var flashShare = step.FlashWeight > 0f ? MathF.Min(weight / step.FlashWeight, 1f) : 1f;
                step.FlashWeight -= weight;

                var units = KsPhaseMath.UnitsToReach(step.HeatCapacity, temperature, flashPoint, latentHeat, heatCapacityChange);
                moles = MathF.Min(MathF.Max(units, 0f) * flashShare * molesPerUnit, gap);
            }
            else
            {
                var partialPressure = vaporMoles * Atmospherics.R * temperature / step.FreeVolume;
                var flux = KsPhaseMath.HertzKnudsenFlux(
                    KsFluidmos.HertzKnudsenAlpha,
                    moleFraction * saturationPressure - partialPressure,
                    properties.InverseSqrtTwoPiMR,
                    temperature);
                moles = flux * step.InterfaceArea * step.DeltaTime;

                // Never overshoot equilibrium, nor move more latent heat than the step allows.
                moles = gap >= 0f ? MathF.Min(moles, gap) : MathF.Max(moles, gap);
                moles = Math.Clamp(moles, -maxLatentMoles, maxLatentMoles);
            }
        }

        moles = Math.Clamp(moles, -vaporMoles, sourceUnits * molesPerUnit);
        if (moles == 0f || !float.IsFinite(moles))
            return;

        var changedUnits = moles / molesPerUnit;
        step.Temperature = KsPhaseMath.TemperatureAfter(step.HeatCapacity, temperature, changedUnits, latentHeat, heatCapacityChange);
        step.HeatCapacity += changedUnits * heatCapacityChange;

        if (fromSolid)
            amount.Solid -= changedUnits;
        else
            amount.Liquid -= changedUnits;

        if (properties.Linked)
            mixture.AdjustMoles(properties.GasIndex, moles);
        else
            amount.Vapor += moles;
    }

    /// <summary>
    ///     Freezes liquid below the melting point, or melts solid above it, as far as the sensible heat pays for: the
    ///         mixture sits at the melting point as a slurry until it has.
    /// </summary>
    /// <param name="belowTriplePoint">
    ///     Whether it has no stable liquid here (<see cref="IsBelowTriplePoint"/>). Its solid then never melts, and any
    ///         liquid it has, merged in or condensed at a wall, freezes whatever the temperature, a step's worth of heat
    ///         at a time. Melting it would only have flashed it to gas below the melting point and frozen it again.
    /// </param>
    /// <param name="meltingPoint">
    ///     Where it freezes in this mixture: its melting point, depressed by what is dissolved in it.
    /// </param>
    private static void Freeze(ref PhaseStep step, KsFluidReagentProperties properties, ref KsFluidAmount amount, float meltingPoint,
        bool belowTriplePoint)
    {
        var fusionHeat = properties.FusionHeat;
        if (fusionHeat <= 0f)
            return;

        var temperature = step.Temperature;
        float frozenUnits;

        if (belowTriplePoint)
        {
            if (amount.Liquid <= 0f)
                return;

            frozenUnits = MathF.Min(amount.Liquid, step.HeatCapacity * KsFluidmos.MaxPhaseDeltaT / fusionHeat);
        }
        else if (temperature < meltingPoint && amount.Liquid > 0f)
            frozenUnits = MathF.Min(amount.Liquid, step.HeatCapacity * (meltingPoint - temperature) / fusionHeat);
        else if (temperature > meltingPoint && amount.Solid > 0f)
            frozenUnits = -MathF.Min(amount.Solid, step.HeatCapacity * (temperature - meltingPoint) / fusionHeat);
        else
            return;

        // Freezing releases the heat of fusion; melting takes it. Solid and liquid share a specific heat.
        step.Temperature = KsPhaseMath.TemperatureAfter(step.HeatCapacity, temperature, frozenUnits, -fusionHeat, heatCapacityChangePerUnit: 0f);
        amount.Liquid -= frozenUnits;
        amount.Solid += frozenUnits;
    }

    /// <summary>
    ///     The state one phase step carries from reagent to reagent.
    /// </summary>
    private struct PhaseStep(
        float heatCapacity,
        float temperature,
        float pressure,
        float freeVolume,
        float interfaceArea,
        float deltaTime,
        float bubbleTemperature,
        float flashWeight)
    {
        /// <summary>
        ///     Unscaled J/K, updated as each phase change alters it.
        /// </summary>
        public float HeatCapacity = heatCapacity;

        public float Temperature = temperature;
        public readonly float StartTemperature = temperature;
        public readonly float Pressure = pressure;
        public readonly float FreeVolume = freeVolume;
        public readonly float InterfaceArea = interfaceArea;
        public readonly float DeltaTime = deltaTime;
        /// <summary>
        ///     Kelvin the liquid flashes down to while boiling; infinity while it is not.
        /// </summary>
        public readonly float BubbleTemperature = bubbleTemperature;

        /// <summary>
        ///     What's left of the sum the flash's energy is shared out by, as reagents take their turn at it.
        /// </summary>
        public float FlashWeight = flashWeight;
    }
}
