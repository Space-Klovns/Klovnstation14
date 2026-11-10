using Content.Shared._KS14.Fluidmos;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.TileEffects.Effects;

/// <summary>
///     Freezes the reagent out of a solution colder than its melting point, into the tile's ice sheet. Energy-limited, as
///         boiling is: the sensible-heat deficit freezes <c>(Tm - T) C / FusionHeat</c> units, whose heat of fusion warms
///         what is left back to the melting point, so a freezing puddle sits at its melting point until it has frozen.
/// </summary>
/// <remarks>
///     A reagent that sublimes at the tile's pressure (its boiling point there is below its melting point, as CO2's is
///         at one atmosphere) never forms ice. The frozen units keep their reagent data, so frozen blood keeps its DNA.
/// </remarks>
public sealed partial class FreezeTileEffect : KsTileEffect
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private KsFluidmosPropertiesSystem _fluidmosPropertiesSystem = default!;

    /// <summary>
    ///     Most units that may freeze per second.
    /// </summary>
    [DataField]
    public float Rate = 10f;

    public override bool Execute(TileRef tileRef, float scale, ref KsTileEffectContext context)
    {
        if (scale <= 0f ||
            context.Solution is not { } solution ||
            context.Reagent is not { } reagent ||
            !_prototypeManager.TryIndex<ReagentPrototype>(reagent.Prototype, out var reagentPrototype) ||
            !_fluidmosPropertiesSystem.TryGetProperties(reagentPrototype.ID, out var properties) ||
            !properties.Freezable ||
            properties.FusionHeat <= 0f)
            return false;

        var meltingPoint = properties.MeltingPoint;
        var temperature = solution.Temperature;
        if (temperature >= meltingPoint)
            return false;

        if (properties.Volatile)
        {
            var airQuery = new KsTileEffectAirQueryEvent(tileRef.GridUid, tileRef.GridIndices);
            EntityManager.EventBus.RaiseEvent(EventSource.Local, ref airQuery);
            if (airQuery.Mixture is { } air)
            {
                // Below the triple point it boils instead, so there's nothing to freeze. Immutable air is the exception once
                //     it's cooled past that: a puddle radiating under a map's air or in vacuum has no other way to go.
                var boilingPoint = KsPhaseMath.BoilingPointAt(properties.VaporizationEntropy, properties.BoilingPoint, air.Pressure);
                if (boilingPoint < meltingPoint && (!air.Immutable || temperature > boilingPoint))
                    return false;
            }
        }

        var specificHeat = reagentPrototype.SpecificHeat;
        // Whatever an earlier effect of this reagent already claimed is removed afterwards, along with its heat capacity.
        var heatCapacity = solution.GetHeatCapacity(_prototypeManager) - context.RemovedVolume * specificHeat;
        var available = (float)solution.GetReagentQuantity(reagent) - context.RemovedVolume;
        if (heatCapacity <= 0f || available <= 0f)
            return false;

        // Freezing u units at the melting point leaves (C - u c) T' + u c Tm = C T + u Lf; at T' = Tm, u = C (Tm - T) / Lf.
        var units = MathF.Min(MathF.Min(available, Rate * scale), heatCapacity * (meltingPoint - temperature) / properties.FusionHeat);
        units = (float)FixedPoint2.New(units);
        if (units <= 0f)
            return false;

        // The ice leaves at the melting point, and the rest of the solution keeps what that leaves of the heat. If
        //     nothing is left, the ice holds it all: freezing it all released no more than brings it to the melting point.
        var remainingHeatCapacity = heatCapacity - units * specificHeat;
        var iceTemperature = remainingHeatCapacity > 0f
            ? meltingPoint
            : (heatCapacity * temperature + units * properties.FusionHeat) / (units * specificHeat);

        var ev = new KsTileEffectFreezeEvent(tileRef.GridUid, tileRef.GridIndices, reagent, units, iceTemperature);
        EntityManager.EventBus.RaiseEvent(EventSource.Local, ref ev);
        if (!ev.Handled)
            return false;

        // Temperature is intensive, so removing the frozen units afterwards leaves this in place.
        if (remainingHeatCapacity > 0f)
        {
            solution.Temperature = (heatCapacity * temperature + units * properties.FusionHeat - units * specificHeat * meltingPoint)
                                   / remainingHeatCapacity;
        }
        context.RemovedVolume += units;
        return true;
    }
}
