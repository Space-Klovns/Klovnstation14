using Content.Shared.Atmos;

namespace Content.Shared._KS14.TileEffects;

/// <summary>
///     Raised by <see cref="Effects.BoilTileEffect"/> to put boiled reagent into a tile's air, which only the server
///         can reach. Whoever adds it sets <see cref="BoiledUnits"/>; zero means nothing boiled.
/// </summary>
/// <param name="MaxUnits">Most units that may boil.</param>
/// <param name="MolesPerUnit">Moles of gas each unit becomes.</param>
/// <param name="SensibleEnergyPerUnit">Thermal energy each boiled unit carries into the air, in joules.</param>
/// <param name="LatentEnergyPerUnitFromAir">
///     Thermal energy, in joules, the change of phase takes from the air for each unit, rather than from the solution:
///         for a solution too cold to boil on its own heat. Fewer units boil if the air cannot spare it.
/// </param>
/// <param name="MinimumAirTemperature">The air is not cooled below this, in kelvin; fewer units boil instead.</param>
/// <param name="Displace">Whether the gas pushes the tile's other gases out into its neighbours.</param>
[ByRefEvent]
public record struct KsTileEffectBoilEvent(
    EntityUid GridUid,
    Vector2i Tile,
    Gas Gas,
    float MaxUnits,
    float MolesPerUnit,
    float SensibleEnergyPerUnit,
    float LatentEnergyPerUnitFromAir,
    float MinimumAirTemperature,
    bool Displace)
{
    /// <summary>
    ///     Units that did boil, floored to the 0.01u a solution stores.
    /// </summary>
    public float BoiledUnits;
}

/// <summary>
///     Raised by tile effects to read a tile's air, which only the server can reach. Whoever has it sets
///         <see cref="Mixture"/>, which the effect only reads: changes to it go through events like
///         <see cref="KsTileEffectBoilEvent"/>.
/// </summary>
[ByRefEvent]
public record struct KsTileEffectAirQueryEvent(EntityUid GridUid, Vector2i Tile)
{
    public GasMixture? Mixture;
}

/// <summary>
///     Raised by <see cref="Effects.BoilTileEffect"/> in smoke mode: a reagent with no gas of its own boiled off a
///         solution, and leaves as smoke over the tile.
/// </summary>
/// <param name="Reagent">The reagent that boiled, data included.</param>
/// <param name="Units">How much boiled.</param>
/// <param name="Temperature">The temperature it boiled at, in kelvin.</param>
[ByRefEvent]
public record struct KsTileEffectSmokeEvent(
    EntityUid GridUid,
    Vector2i Tile,
    Content.Shared.Chemistry.Reagent.ReagentId Reagent,
    float Units,
    float Temperature)
{
    /// <summary>
    ///     Whether anyone took the smoke.
    /// </summary>
    public bool Handled;
}

/// <summary>
///     Raised by <see cref="Effects.FreezeTileEffect"/>: units of a reagent froze out of a solution, and go into the
///         tile's ice sheet at <paramref name="Temperature"/>. Only the server can make the sheet.
/// </summary>
[ByRefEvent]
public record struct KsTileEffectFreezeEvent(
    EntityUid GridUid,
    Vector2i Tile,
    Content.Shared.Chemistry.Reagent.ReagentId Reagent,
    float Units,
    float Temperature)
{
    /// <summary>
    ///     Whether anyone took the ice.
    /// </summary>
    public bool Handled;
}
