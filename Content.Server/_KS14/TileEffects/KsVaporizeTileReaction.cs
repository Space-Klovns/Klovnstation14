using Content.Server.Atmos.EntitySystems;
using Content.Server.Chemistry.TileReactions;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;

namespace Content.Server._KS14.TileEffects;

/// <summary>
///     Turns the reagent a spray or smoke hands a tile straight into gas in that tile's air, taking the latent heat
///         from the air: liquid carbon dioxide flashing to gas as it leaves an extinguisher. Unlike
///         <c>ExtinguishTileReaction</c>, it works on any tile, burning or not.
/// </summary>
/// <remarks>
///     Only as much turns to gas as the air can pay for without dropping below the reagent's boiling point. The rest
///         stays with the spray, for the next tile, or with <see cref="SpillRemainder"/> lands here as a puddle. The gas carries the spray's own temperature, read from
///         <see cref="Shared.Fluids.SharedPuddleSystem.KsTileReactionSourceTemperature"/>. Energy is accounted as
///         <see cref="KsTileEffectAtmosSystem.TryChangePhase"/> does it.
/// </remarks>
[DataDefinition]
public sealed partial class KsVaporizeTileReaction : ITileReaction
{
    /// <summary>
    ///     The gas the reagent becomes. Null uses the gas whose reagent is this one, and does nothing without one.
    /// </summary>
    [DataField]
    public Gas? Gas;

    /// <summary>
    ///     Energy, in the scaled units atmos uses, that turning a unit to gas takes out of the air.
    /// </summary>
    [DataField]
    public float EnergyPerUnit = 600f;

    /// <summary>
    ///     Moles of gas each unit becomes. Null is one mole per molar mass in units, treating a unit as a gram.
    /// </summary>
    /// <remarks>
    ///     A reagent given more than that should have its specific heat matched to it (moles per unit times the gas's
    ///         molar heat capacity), or its gas does not come out at the spray's temperature.
    /// </remarks>
    [DataField]
    public float? MolesPerUnit;

    /// <summary>
    ///     Whether the gas pushes the tile's other gases out into its neighbours, rather than raising the tile's pressure.
    /// </summary>
    [DataField]
    public bool Displace;

    /// <summary>
    ///     Whether what the air cannot turn to gas lands on the tile as a puddle, at the spray's temperature, instead of
    ///         staying with the spray. For a reagent whose puddle turns to gas by itself as the air warms (a
    ///         <see cref="Shared._KS14.TileEffects.Effects.BoilTileEffect"/> that sublimates), so that a spray into air
    ///         already too cold still ends up as gas, later.
    /// </summary>
    [DataField]
    public bool SpillRemainder;

    public FixedPoint2 TileReact(TileRef tile,
        ReagentPrototype reagent,
        FixedPoint2 reactVolume,
        IEntityManager entityManager,
        List<ReagentData>? data)
    {
        if (reactVolume <= FixedPoint2.Zero || tile.Tile.IsEmpty)
            return FixedPoint2.Zero;

        var atmosphereSystem = entityManager.System<AtmosphereSystem>();
        if ((Gas ?? ExtinguishTileReaction.GetBoiledGas(atmosphereSystem, reagent)) is not { } gas)
            return FixedPoint2.Zero;

        var sourceTemperature = entityManager.System<PuddleSystem>().KsTileReactionSourceTemperature ?? Atmospherics.T20C;
        var minimumTemperature = reagent.BoilingPoint is { } boilingPoint
            ? boilingPoint + Atmospherics.T0C // BoilingPoint is in Celsius
            : Atmospherics.TCMB;

        var units = entityManager.System<KsTileEffectAtmosSystem>().TryChangePhase(
            tile.GridUid,
            tile.GridIndices,
            gas,
            (float)reactVolume,
            MolesPerUnit ?? 1f / atmosphereSystem.GasMolarMasses[(int)gas],
            reagent.SpecificHeat * sourceTemperature,
            EnergyPerUnit,
            minimumTemperature,
            Displace);

        var vaporized = FixedPoint2.New(units);
        if (!SpillRemainder || vaporized >= reactVolume)
            return vaporized;

        // Not spilled with tile reactions: those are what is running now.
        var remainder = new Solution(reagent.ID, reactVolume - vaporized, data) { Temperature = sourceTemperature };
        return entityManager.System<PuddleSystem>().TrySpillAt(tile, remainder, out _, sound: false, tileReact: false)
            ? reactVolume
            : vaporized;
    }
}
