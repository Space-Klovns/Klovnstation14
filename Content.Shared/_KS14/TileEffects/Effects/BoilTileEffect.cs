using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Chemistry.Reagent;
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
///     The energy is accounted the same way <c>ExtinguishTileReaction</c> accounts it: the boiled reagent's sensible
///         heat goes into the air along with its gas, and the latent heat leaves with the vapor. Never cools the
///         solution below the boiling point, and boils nothing when the tile has no air to take the gas.
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

    public override bool Execute(TileRef tileRef, float scale, ref KsTileEffectReagentData reagentData)
    {
        if (reagentData.Solution is not { } solution ||
            reagentData.Reagent is not { } reagent ||
            !_prototypeManager.TryIndex<ReagentPrototype>(reagent.Prototype, out var reagentPrototype) ||
            reagentPrototype.BoilingPoint is not { } boilingPointCelsius)
            return false;

        var boilingPoint = boilingPointCelsius + Atmospherics.T0C;
        var temperature = solution.Temperature;
        if (temperature <= boilingPoint || LatentHeat <= 0f)
            return false;

        if ((Gas ?? GetBoiledGas(reagentPrototype)) is not { } gas)
            return false;

        var specificHeat = reagentPrototype.SpecificHeat;
        // Whatever an earlier effect of this reagent already claimed is removed afterwards, along with its heat capacity.
        var heatCapacity = solution.GetHeatCapacity(_prototypeManager) - reagentData.RemovedVolume * specificHeat;
        var available = (float)solution.GetReagentQuantity(reagent) - reagentData.RemovedVolume;
        if (heatCapacity <= 0f || available <= 0f)
            return false;

        // Boiling b units leaves (C - b c)(T - T') = b L, so this much brings the solution down to exactly the boiling point.
        var excessTemperature = temperature - boilingPoint;
        var boiledByEnergy = excessTemperature * heatCapacity / (LatentHeat + excessTemperature * specificHeat);

        // Floored to the 0.01u the solution actually stores, so the energy taken matches the volume removed.
        var boiled = MathF.Floor(MathF.Min(MathF.Min(available, Rate * scale), boiledByEnergy) * 100f) / 100f;
        if (boiled <= 0f)
            return false;

        var ev = new KsTileEffectBoilEvent(tileRef.GridUid, tileRef.GridIndices, gas, boiled, boiled * specificHeat * temperature);
        EntityManager.EventBus.RaiseEvent(EventSource.Local, ref ev);
        if (!ev.Handled)
            return false;

        // Temperature is intensive, so removing the boiled units afterwards leaves this in place.
        solution.Temperature = temperature - boiled * LatentHeat / (heatCapacity - boiled * specificHeat);
        reagentData.RemovedVolume += boiled;
        return true;
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
