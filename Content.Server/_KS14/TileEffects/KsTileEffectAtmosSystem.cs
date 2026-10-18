using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.TileEffects;
using Content.Shared.Atmos;

namespace Content.Server._KS14.TileEffects;

/// <summary>
///     The atmos half of the tile effects: shared effects cannot reach a tile's air, so they ask through events.
/// </summary>
public sealed partial class KsTileEffectAtmosSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;

    [SubscribeLocalEvent]
    private void OnBoil(ref KsTileEffectBoilEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryAddBoiledGas(args.GridUid, args.Tile, args.Gas, args.Units, args.SensibleEnergy);
    }

    /// <summary>
    ///     Puts something boiled into a tile's air as gas.
    /// </summary>
    /// <remarks>
    ///     The gas joins the air carrying exactly <paramref name="sensibleEnergy"/>, rather than at the temperature
    ///         it boiled at: gas and reagent heat capacities differ, so the two do not agree, and only this one
    ///         conserves energy.
    /// </remarks>
    /// <param name="units">Units boiled, treated as grams, as <c>ExtinguishTileReaction</c> treats them.</param>
    /// <param name="sensibleEnergy">Thermal energy, in joules, the boiled units carry into the air.</param>
    /// <returns>False if the tile has no mutable mixture (space) to take the gas, in which case nothing changed.</returns>
    public bool TryAddBoiledGas(EntityUid gridUid, Vector2i tile, Gas gas, float units, float sensibleEnergy)
    {
        if (_atmosphereSystem.GetTileMixture((gridUid, null, null), null, tile, excite: true) is not { Immutable: false } mixture)
            return false;

        var heatCapacity = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);
        var energy = heatCapacity * mixture.Temperature + sensibleEnergy;

        mixture.AdjustMoles(gas, units / _atmosphereSystem.GasMolarMasses[(int)gas]);

        var heatCapacityAfter = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);
        if (heatCapacityAfter > Atmospherics.MinimumHeatCapacity)
            mixture.Temperature = MathF.Max(energy / heatCapacityAfter, Atmospherics.TCMB);

        return true;
    }
}
