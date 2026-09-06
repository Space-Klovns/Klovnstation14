namespace Content.Shared._KS14.Atmos.ChemicalFire;

/// <summary>
///     Raised on a chemfire every <see cref="ChemicalFireComponent.HeatInterval"/>, carrying the tile it
///         currently occupies. Both the atmos ignition and any gas consumption hang off this, so anything
///         that wants to act on a chemfire's tile can subscribe without re-resolving the transform.
/// </summary>
/// <param name="GridUid">Grid the chemfire sits on.</param>
/// <param name="Tile">Tile indices of the chemfire on <paramref name="GridUid"/>.</param>
/// <param name="Seconds">Seconds elapsed since the previous heat tick, i.e. the heat interval.</param>
[ByRefEvent]
public readonly record struct ChemicalFireHeatTileEvent(EntityUid GridUid, Vector2i Tile, float Seconds);

/// <summary>
///     Raised broadcast whenever the set of chemfires on a tile changes, so visuals can resmooth that tile
///         and its lateral neighbours without polling.
/// </summary>
[ByRefEvent]
public readonly record struct ChemicalFireTileChangedEvent(EntityUid GridUid, Vector2i Tile);
