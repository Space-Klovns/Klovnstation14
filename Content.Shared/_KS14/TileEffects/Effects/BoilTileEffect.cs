using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.TileEffects.Effects;

/// <summary>
///     Boils the reagent off a solution hotter than the reagent's boiling point, into the tile's air as gas.
///     Boiling takes <see cref="LatentHeat"/> out of the solution for every unit, which is how water cools a hot puddle
///         far more than its heat capacity alone would: it is the puddle-side counterpart of the latent heat
///         <c>ExtinguishTileReaction</c> takes out of the air.
/// </summary>
/// <remarks>
///     <para>
///         The energy is accounted the same way <c>ExtinguishTileReaction</c> accounts it: the boiled reagent's
///             sensible heat goes into the air along with its gas, and the latent heat leaves with the vapor. Never
///             cools the solution below the boiling point, and boils nothing when the tile has no air to take the gas.
///     </para>
///     <para>
///         With <see cref="Sublimate"/>, a solution at or below the boiling point still boils, taking the latent heat
///             from the air instead, as dry ice does: so the reagent always ends up as gas, unless the air is colder
///             than it is.
///     </para>
/// </remarks>
public sealed partial class BoilTileEffect : KsTileEffect
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedAtmosphereSystem _atmosphereSystem = default!;

    /// <summary>
    ///     Energy, in joules, that boiling one unit takes out of the solution.
    ///     The default is water's latent heat in proportion to its specific heat (2260 J/g over 4.18 J/(g K)), scaled
    ///         to a specific heat of 1 J/(u K): boiling a unit cools the solution as much as warming 540 units by 1 K.
    /// </summary>
    [DataField]
    public float LatentHeat = 540f;

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
    ///     Moles of gas each unit becomes. Null is one mole per molar mass in units, treating a unit as a gram.
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

    public override bool Execute(TileRef tileRef, float scale, ref KsTileEffectReagentData reagentData)
    {
        if (reagentData.Solution is not { } solution ||
            reagentData.Reagent is not { } reagent ||
            !_prototypeManager.TryIndex<ReagentPrototype>(reagent.Prototype, out var reagentPrototype) ||
            reagentPrototype.BoilingPoint is not { } boilingPointCelsius ||
            LatentHeat <= 0f)
            return false;

        if ((Gas ?? GetBoiledGas(reagentPrototype)) is not { } gas)
            return false;

        var boilingPoint = boilingPointCelsius + Atmospherics.T0C;
        var temperature = solution.Temperature;
        var specificHeat = reagentPrototype.SpecificHeat;
        // Whatever an earlier effect of this reagent already claimed is removed afterwards, along with its heat capacity.
        var heatCapacity = solution.GetHeatCapacity(_prototypeManager) - reagentData.RemovedVolume * specificHeat;
        var available = (float)solution.GetReagentQuantity(reagent) - reagentData.RemovedVolume;
        if (heatCapacity <= 0f || available <= 0f)
            return false;

        var molesPerUnit = MolesPerUnit ?? 1f / _atmosphereSystem.GasMolarMasses[(int)gas];
        var maxUnits = MathF.Min(available, Rate * scale);
        var boiled = 0f;

        // On its own heat first, as far as that goes; the air only pays for sublimating, so this may boil into colder air.
        if (temperature > boilingPoint)
        {
            // Boiling b units leaves (C - b c)(T - T') = b L, so this much brings the solution down to exactly the boiling point.
            var excessTemperature = temperature - boilingPoint;
            var hotUnits = MathF.Min(maxUnits, excessTemperature * heatCapacity / (LatentHeat + excessTemperature * specificHeat));
            boiled = RaiseBoil(tileRef, gas, hotUnits, molesPerUnit, specificHeat * temperature, latentEnergyPerUnitFromAir: 0f, Atmospherics.TCMB);

            // Temperature is intensive, so removing the boiled units afterwards leaves this in place.
            if (boiled > 0f)
                solution.Temperature = temperature - boiled * LatentHeat / (heatCapacity - boiled * specificHeat);
        }

        // Then whatever its own heat could not boil, with the latent heat from the air, which keeps the solution's
        //     temperature. A solution only just above its boiling point would otherwise stall there.
        if (Sublimate && boiled < maxUnits)
        {
            boiled += RaiseBoil(tileRef, gas, maxUnits - boiled, molesPerUnit, specificHeat * solution.Temperature,
                latentEnergyPerUnitFromAir: LatentHeat, boilingPoint);
        }

        if (boiled <= 0f)
            return false;

        reagentData.RemovedVolume += boiled;
        return true;
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
