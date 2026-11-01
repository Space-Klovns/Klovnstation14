using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.TileEffects.Effects;

/// <summary>
///     Boils the reagent off a solution hotter than the reagent's boiling point at the tile's pressure, into the tile's
///         air as gas, or as smoke when it has no gas. Boiling takes <see cref="LatentHeat"/> out of the solution for every
///         unit, which is how water cools a hot puddle far more than its heat capacity alone would: it is the puddle-side
///         counterpart of the latent heat <c>ExtinguishTileReaction</c> takes out of the air.
/// </summary>
/// <remarks>
///     <para>
///         The energy is accounted the same way <c>ExtinguishTileReaction</c> accounts it: the boiled reagent's
///             sensible heat goes into the air along with its gas, and the latent heat leaves with the vapor. Never
///             cools the solution below the boiling point, and boils nothing when the tile has no air to take the gas.
///     </para>
///     <para>
///         The boiling point follows the tile's pressure along the reagent's Fluidmos vapor-pressure curve
///             (<see cref="KsPhaseMath.BoilingPointAt"/>): water in a spaced room boils away, cooling as it goes, and a
///             pressurised room raises the boiling point. At one atmosphere it is the reagent's own boiling point.
///     </para>
///     <para>
///         With <see cref="Sublimate"/>, a solution at or below the boiling point still boils, taking the latent heat
///             from the air instead, as dry ice does: so the reagent always ends up as gas, unless the air is colder
///             than it is.
///     </para>
///     <para>
///         With <see cref="Evaporate"/>, a solution below the boiling point evaporates into its tile as humidity, at the
///             Hertz–Knudsen rate towards saturation over the puddle's coverage of the tile, taking the latent heat from
///             the solution. A dry room dries a puddle; a humid or sealed one barely does.
///     </para>
/// </remarks>
public sealed partial class BoilTileEffect : KsTileEffect
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedAtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private KsFluidmosPropertiesSystem _fluidmosPropertiesSystem = default!;
    [Dependency] private Content.Shared._KS14.Atmos.FoamBlanket.SharedKsFoamBlanketSystem _foamBlanketSystem = default!;

    /// <summary>
    ///     Energy, in joules, that boiling one unit takes out of the solution. Null takes the reagent's Fluidmos latent
    ///         heat (<see cref="KsPhaseMath.DefaultLatentHeat"/>), so pipes and puddles agree: for water that is its latent
    ///         heat in proportion to its specific heat (2260 J/g over 4.18 J/(g K)), scaled to a specific heat of
    ///         1 J/(u K), 540 J/u.
    /// </summary>
    [DataField]
    public float? LatentHeat;

    /// <summary>
    ///     Most units that may boil off per second; boiling is otherwise limited only by the heat available.
    /// </summary>
    [DataField]
    public float Rate = 10f;

    /// <summary>
    ///     The gas the reagent boils into. Null uses the gas whose reagent is this one, and boils nothing without one.
    /// </summary>
    [DataField]
    public Gas? Gas;

    /// <summary>
    ///     Moles of gas each unit becomes. Null is the reagent's Fluidmos moles per unit when it is linked to this gas,
    ///         and otherwise one mole per molar mass in units, treating a unit as a gram.
    /// </summary>
    /// <remarks>
    ///     A reagent given more than that should have its specific heat matched to it (moles per unit times the gas's
    ///         molar heat capacity), or its gas does not come out at the temperature it boiled at.
    /// </remarks>
    [DataField]
    public float? MolesPerUnit;

    /// <summary>
    ///     Whether the gas pushes the tile's other gases out into its neighbours, rather than raising the tile's pressure.
    /// </summary>
    [DataField]
    public bool Displace;

    /// <summary>
    ///     Whether the reagent still boils, at up to <see cref="Rate"/>, when the solution is not above its boiling
    ///         point, taking the latent heat from the tile's air. The air is never cooled below the boiling point.
    /// </summary>
    [DataField]
    public bool Sublimate;

    /// <summary>
    ///     Whether a solution below the boiling point evaporates into the tile's gas (see the remarks). For reagents with
    ///         a gas; the latent heat comes from the solution.
    /// </summary>
    [DataField]
    public bool Evaporate;

    /// <summary>
    ///     Whether the reagent boils into smoke over the tile instead of into a gas, for volatile reagents with none: a hot
    ///         puddle of fuel boils into a cloud.
    /// </summary>
    [DataField]
    public bool Smoke;

    public override bool Execute(TileRef tileRef, float scale, ref KsTileEffectContext context)
    {
        if (context.Solution is not { } solution ||
            context.Reagent is not { } reagent ||
            !_prototypeManager.TryIndex<ReagentPrototype>(reagent.Prototype, out var reagentPrototype) ||
            reagentPrototype.BoilingPoint is not { } boilingPoint)
            return false;

        Gas? gas = null;
        if (!Smoke && (gas = GetGas(reagentPrototype)) == null)
            return false;

        var latentHeat = GetLatentHeat(reagentPrototype);
        if (latentHeat <= 0f)
            return false;

        var temperature = solution.Temperature;
        var specificHeat = reagentPrototype.SpecificHeat;
        // Whatever an earlier effect of this reagent already claimed is removed afterwards, along with its heat capacity.
        var heatCapacity = solution.GetHeatCapacity(_prototypeManager) - context.RemovedVolume * specificHeat;
        var available = (float)solution.GetReagentQuantity(reagent) - context.RemovedVolume;
        if (heatCapacity <= 0f || available <= 0f)
            return false;

        _fluidmosPropertiesSystem.TryGetProperties(reagentPrototype.ID, out var properties);
        var vaporizationEntropy = properties?.VaporizationEntropy ?? KsFluidmos.DefaultVaporizationEntropy;

        var airQuery = new KsTileEffectAirQueryEvent(tileRef.GridUid, tileRef.GridIndices);
        EntityManager.EventBus.RaiseEvent(EventSource.Local, ref airQuery);
        var air = airQuery.Mixture;
        var localBoilingPoint = air != null
            ? KsPhaseMath.BoilingPointAt(vaporizationEntropy, boilingPoint, air.Pressure)
            : boilingPoint;

        var molesPerUnit = gas is { } boiledGas ? GetMolesPerUnit(reagentPrototype, boiledGas) : 0f;
        var maxUnits = MathF.Min(available, Rate * scale);
        var boiled = 0f;

        // On its own heat first, as far as that goes; the air only pays for sublimating, so this may boil into colder air.
        if (temperature > localBoilingPoint)
        {
            // Boiling b units leaves (C - b c)(T - T') = b L, so this much brings the solution down to exactly the boiling point.
            var excessTemperature = temperature - localBoilingPoint;
            var hotUnits = MathF.Min(maxUnits, excessTemperature * heatCapacity / (latentHeat + excessTemperature * specificHeat));
            boiled = gas is { } hotGas
                ? RaiseBoil(tileRef, hotGas, hotUnits, molesPerUnit, specificHeat * temperature, latentEnergyPerUnitFromAir: 0f, Atmospherics.TCMB)
                : RaiseSmoke(tileRef, reagent, hotUnits, localBoilingPoint);

            // Temperature is intensive, so removing the boiled units afterwards leaves this in place.
            if (boiled > 0f)
                solution.Temperature = temperature - boiled * latentHeat / (heatCapacity - boiled * specificHeat);
        }
        else if (Evaporate && gas is { } evaporatingGas && air != null && properties != null && scale > 0f &&
                 // A foam blanket over the tile seals the puddle in.
                 !_foamBlanketSystem.TryGetBlanket(tileRef.GridUid, tileRef.GridIndices, out _))
        {
            boiled = EvaporateIntoAir(tileRef, solution, air, evaporatingGas, properties, available, molesPerUnit, scale,
                heatCapacity, latentHeat, specificHeat);

            if (boiled > 0f)
                solution.Temperature = temperature - boiled * latentHeat / (heatCapacity - boiled * specificHeat);
        }

        // Then whatever its own heat could not boil, with the latent heat from the air, which keeps the solution's
        //     temperature. A solution only just above its boiling point would otherwise stall there.
        if (Sublimate && gas is { } sublimatingGas && boiled < maxUnits)
        {
            boiled += RaiseBoil(tileRef, sublimatingGas, maxUnits - boiled, molesPerUnit, specificHeat * solution.Temperature,
                latentEnergyPerUnitFromAir: latentHeat, localBoilingPoint);
        }

        if (boiled <= 0f)
            return false;

        context.RemovedVolume += boiled;
        return true;
    }

    /// <summary>
    ///     Evaporates below the boiling point at the Hertz–Knudsen rate, towards saturation of the tile's air, over the
    ///         puddle's coverage of the tile.
    /// </summary>
    /// <returns>The units that evaporated.</returns>
    private float EvaporateIntoAir(
        TileRef tileRef,
        Solution solution,
        GasMixture air,
        Gas gas,
        KsFluidReagentProperties properties,
        float available,
        float molesPerUnit,
        float seconds,
        float heatCapacity,
        float latentHeat,
        float specificHeat)
    {
        if (air.Volume <= 0f || air.Temperature <= 0f || molesPerUnit <= 0f)
            return 0f;

        var saturationPressure = KsPhaseMath.SaturationPressure(properties.VaporizationEntropy, properties.BoilingPoint, solution.Temperature);
        var partialPressure = air.GetMoles(gas) * Atmospherics.R * air.Temperature / air.Volume;
        if (saturationPressure <= partialPressure)
            return 0f;

        var coverage = MathF.Min(1f, (float)solution.Volume / KsFluidmos.PuddleReferenceVolume);
        var flux = KsPhaseMath.HertzKnudsenFlux(KsFluidmos.PuddleHertzKnudsenAlpha, saturationPressure - partialPressure,
            properties.InverseSqrtTwoPiMR, solution.Temperature);

        // Never past saturation of the tile's air.
        var gapMoles = (saturationPressure - partialPressure) * air.Volume / (Atmospherics.R * air.Temperature);
        var moles = MathF.Min(flux * coverage * seconds, gapMoles);
        var units = MathF.Min(moles / molesPerUnit, available);

        // The latent heat comes out of the solution, so no more goes than cools it to the air's dew point, where it would
        //     stop. Unbounded, a small puddle that is mostly this reagent could lose nearly all of it in one long step,
        //     leaving the divisor of its new temperature near zero and that temperature far below absolute zero.
        var dewPoint = KsPhaseMath.BoilingPointAt(properties.VaporizationEntropy, properties.BoilingPoint, partialPressure);
        var excessTemperature = solution.Temperature - dewPoint;
        if (excessTemperature <= 0f)
            return 0f;

        units = MathF.Min(units, excessTemperature * heatCapacity / (latentHeat + excessTemperature * specificHeat));

        return RaiseBoil(tileRef, gas, units, molesPerUnit, properties.SpecificHeat * solution.Temperature,
            latentEnergyPerUnitFromAir: 0f, Atmospherics.TCMB);
    }

    /// <summary>
    ///     Asks the server to put up to <paramref name="units"/> into smoke over the tile.
    /// </summary>
    /// <returns>The units that went.</returns>
    private float RaiseSmoke(TileRef tileRef, ReagentId reagent, float units, float temperature)
    {
        units = (float)FixedPoint2.New(units);
        if (units <= 0f)
            return 0f;

        var ev = new KsTileEffectSmokeEvent(tileRef.GridUid, tileRef.GridIndices, reagent, units, temperature);
        EntityManager.EventBus.RaiseEvent(EventSource.Local, ref ev);
        return ev.Handled ? units : 0f;
    }

    /// <summary>
    ///     The gas this effect boils into: its own, the reagent's Fluidmos-linked gas, or the gas whose reagent this is.
    /// </summary>
    public Gas? GetGas(ReagentPrototype reagentPrototype)
    {
        if (Gas is { } gas)
            return gas;

        if (_fluidmosPropertiesSystem.TryGetProperties(reagentPrototype.ID, out var properties) && properties.Linked)
            return (Gas)properties.GasIndex;

        return GetBoiledGas(reagentPrototype);
    }

    /// <summary>
    ///     Asks the server to put up to <paramref name="maxUnits"/> into the tile's air as gas.
    /// </summary>
    /// <returns>The units that boiled.</returns>
    private float RaiseBoil(
        TileRef tileRef,
        Gas gas,
        float maxUnits,
        float molesPerUnit,
        float sensibleEnergyPerUnit,
        float latentEnergyPerUnitFromAir,
        float minimumAirTemperature)
    {
        if (maxUnits <= 0f)
            return 0f;

        var ev = new KsTileEffectBoilEvent(
            tileRef.GridUid,
            tileRef.GridIndices,
            gas,
            maxUnits,
            molesPerUnit,
            sensibleEnergyPerUnit,
            latentEnergyPerUnitFromAir,
            minimumAirTemperature,
            Displace);
        EntityManager.EventBus.RaiseEvent(EventSource.Local, ref ev);
        return ev.BoiledUnits;
    }

    /// <summary>
    ///     The latent heat per unit this effect boils with: its own, or the reagent's Fluidmos one.
    /// </summary>
    public float GetLatentHeat(ReagentPrototype reagentPrototype)
    {
        if (LatentHeat is { } latentHeat)
            return latentHeat;

        return _fluidmosPropertiesSystem.TryGetProperties(reagentPrototype.ID, out var properties)
            ? properties.LatentHeat
            : KsPhaseMath.DefaultLatentHeat(reagentPrototype.BoilingPoint ?? 0f, reagentPrototype.SpecificHeat);
    }

    /// <summary>
    ///     Moles of <paramref name="gas"/> each boiled unit becomes: its own, the reagent's Fluidmos value when the reagent
    ///         is linked to that gas, or a gram per unit.
    /// </summary>
    public float GetMolesPerUnit(ReagentPrototype reagentPrototype, Gas gas)
    {
        if (MolesPerUnit is { } molesPerUnit)
            return molesPerUnit;

        if (_fluidmosPropertiesSystem.TryGetProperties(reagentPrototype.ID, out var properties) && properties.GasIndex == (int)gas)
            return properties.MolesPerUnit;

        return KsPhaseMath.MolesPerUnit(_atmosphereSystem.GasMolarMasses[(int)gas]);
    }

    private Gas? GetBoiledGas(ReagentPrototype reagentPrototype)
    {
        var gasReagents = _atmosphereSystem.GasReagents;

        for (var i = 0; i < gasReagents.Length; i++)
        {
            if (gasReagents[i] == reagentPrototype.ID)
                return (Gas)i;
        }

        return null;
    }
}
