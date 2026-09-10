using Content.Shared.Atmos;

namespace Content.Shared._KS14.Atmos.ChemicalFire;

/// <summary>
///     Raised on a chemfire every <see cref="ChemicalFireComponent.HeatInterval"/>, carrying the tile it
///         currently occupies. Both the atmos ignition and any gas consumption hang off this, so anything
///         that wants to act on a chemfire's tile can subscribe without re-resolving the transform.
/// </summary>
/// <param name="GridUid">Grid the chemfire sits on.</param>
/// <param name="Tile">Tile indices of the chemfire on <paramref name="GridUid"/>.</param>
/// <param name="Seconds">Seconds elapsed since the previous heat tick, i.e. the heat interval.</param>
/// <param name="Mixture">
///     The tile's gas mixture, resolved once for every subscriber to share - null client-side, where atmos
///         doesn't exist.
/// </param>
[ByRefEvent]
public readonly record struct ChemicalFireHeatTileEvent(EntityUid GridUid, Vector2i Tile, float Seconds, GasMixture? Mixture);

/// <summary>
///     Raised on a paused, nullspace singleton "template" chemfire - never on a real, tile-anchored one - to
///         ask "could a real instance of you survive at this tile?" Any component that needs something from
///         the tile to keep burning (e.g. <see cref="ChemicalFireGasConsumerComponent"/> needing one of its
///         gases present) answers here.
///     Defaults to sustainable; only <see cref="Deny"/> can turn it false, and nothing can turn it back.
/// </summary>
/// <param name="GridUid">Grid the candidate tile is on.</param>
/// <param name="Tile">Tile indices being checked.</param>
/// <param name="Mixture">The tile's current gas mixture, or null if there is none.</param>
[ByRefEvent]
public record struct ChemicalFireCanSustainEvent(EntityUid GridUid, Vector2i Tile, GasMixture? Mixture)
{
    public bool CanSustain { get; private set; } = true;

    public void Deny() => CanSustain = false;
}

/// <summary>
///     Raised broadcast whenever the set of chemfires on a tile changes, so visuals can resmooth that tile
///         and its lateral neighbours without polling.
/// </summary>
[ByRefEvent]
public readonly record struct ChemicalFireTileChangedEvent(EntityUid GridUid, Vector2i Tile);
