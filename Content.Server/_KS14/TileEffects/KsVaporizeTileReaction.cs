using Content.Server.Atmos.EntitySystems;
using Content.Server.Chemistry.TileReactions;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Fluidmos;
using Content.Shared._KS14.TileEffects;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
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
///     Only as much turns to gas as the air can pay for without dropping below the reagent's boiling point at the tile's
///         pressure, from its Fluidmos vapor-pressure curve when it has one: thinner air, as a displacing spray leaves,
///         lets it flash colder. A reagent linked to a gas becomes that gas, at its Fluidmos moles per unit. The rest
///         stays with the spray, for the next tile, or with <see cref="SpillRemainder"/> lands here as a puddle. The gas carries the spray's own temperature, read from
///         <see cref="Shared.Fluids.SharedPuddleSystem.KsTileReactionSourceTemperature"/>. Energy is accounted as
///         <see cref="KsTileEffectAtmosSystem.TryChangePhase"/> does it.
/// </remarks>
public sealed partial class KsVaporizeTileReaction : KsTileReaction
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private KsFluidmosPropertiesSystem _fluidmosPropertiesSystem = default!;
    [Dependency] private KsTileEffectAtmosSystem _tileEffectAtmosSystem = default!;
    [Dependency] private PuddleSystem _puddleSystem = default!;

    /// <summary>
    ///     The gas the reagent becomes. Null uses its Fluidmos-linked gas, or else the gas whose reagent is this one, and
    ///         does nothing without either.
    /// </summary>
    [DataField]
    public Gas? Gas;

    /// <summary>
    ///     Energy, in the scaled units atmos uses, that turning a unit to gas takes out of the air.
    /// </summary>
    [DataField]
    public float EnergyPerUnit = 600f;

    /// <summary>
    ///     Moles of gas each unit becomes. Null is its Fluidmos moles per unit when it is linked to the gas, or else one
    ///         mole per molar mass in units, treating a unit as a gram.
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

    protected override FixedPoint2 React(TileRef tile, ReagentPrototype reagent, FixedPoint2 reactVolume, List<ReagentData>? data)
    {
        if (reactVolume <= FixedPoint2.Zero || tile.Tile.IsEmpty)
            return FixedPoint2.Zero;

        _fluidmosPropertiesSystem.TryGetProperties(reagent.ID, out var properties);
        var linkedGas = properties is { Linked: true } ? (Gas?)properties.GasIndex : null;
        if ((Gas ?? linkedGas ?? ExtinguishTileReaction.GetBoiledGas(_atmosphereSystem, reagent)) is not { } gas)
            return FixedPoint2.Zero;

        var molesPerUnit = MolesPerUnit ??
            (gas == linkedGas ? properties!.MolesPerUnit : 1f / _atmosphereSystem.GasMolarMasses[(int)gas]);

        var sourceTemperature = _puddleSystem.KsTileReactionSourceTemperature ?? Atmospherics.T20C;
        var minimumTemperature = reagent.BoilingPoint ?? Atmospherics.TCMB;
        if (properties is { Volatile: true } &&
            _atmosphereSystem.GetGridTileMixture(tile.GridUid, tile.GridIndices) is { Immutable: false } air)
            minimumTemperature = KsPhaseMath.BoilingPointAt(properties.VaporizationEntropy, properties.BoilingPoint, air.Pressure);

        var units = _tileEffectAtmosSystem.TryChangePhase(
            tile.GridUid,
            tile.GridIndices,
            gas,
            (float)reactVolume,
            molesPerUnit,
            reagent.SpecificHeat * sourceTemperature,
            EnergyPerUnit,
            minimumTemperature,
            Displace);

        var vaporized = FixedPoint2.New(units);
        if (!SpillRemainder || vaporized >= reactVolume)
            return vaporized;

        // Not spilled with tile reactions: those are what is running now.
        var remainder = new Solution(reagent.ID, reactVolume - vaporized, data) { Temperature = sourceTemperature };
        return _puddleSystem.TrySpillAt(tile, remainder, out _, sound: false, tileReact: false)
            ? reactVolume
            : vaporized;
    }
}
