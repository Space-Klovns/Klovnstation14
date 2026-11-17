using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;

namespace Content.Server.Atmos.EntitySystems;

/// <summary>
///     KS14: the one way to ask about the air on a tile known by its grid and indices.
/// </summary>
/// <remarks>
///     Asking <see cref="GetTileMixture(Entity{GridAtmosphereComponent?, GasTileOverlayComponent?}?, Entity{MapAtmosphereComponent?}?, Vector2i, bool)"/>
///         with no map gives space for every tile the grid does not simulate, even on a planet. These go on to the map the
///         grid is on, as atmos itself does: see <see cref="SharedAtmosphereSystem.GetMapMixture"/>.
/// </remarks>
public sealed partial class AtmosphereSystem
{
    /// <summary>
    ///     The air on <paramref name="tile"/> of <paramref name="grid"/>: the grid's own where it simulates that tile, the map's
    ///         elsewhere. The map's is immutable, so anything that changes the air checks <see cref="GasMixture.Immutable"/>.
    ///         Null where the grid holds the tile but has no air on it.
    /// </summary>
    /// <param name="excite">Wakes the tile, so a change to its air spreads.</param>
    public GasMixture? GetGridTileMixture(Entity<GridAtmosphereComponent?> grid, Vector2i tile, bool excite = false)
    {
        return GetTileMixture((grid.Owner, grid.Comp, null), GetGridMapAtmosphere(grid.Owner), tile, excite);
    }

    /// <summary>
    ///     Whether <paramref name="tile"/> of <paramref name="grid"/> is open to space, going by the map's atmosphere where the
    ///         grid does not simulate it. On a planet, a tile with no floor is not.
    /// </summary>
    public bool IsGridTileSpace(Entity<GridAtmosphereComponent?> grid, Vector2i tile)
    {
        return IsTileSpace(grid, GetGridMapAtmosphere(grid.Owner), tile);
    }

    private Entity<MapAtmosphereComponent?>? GetGridMapAtmosphere(EntityUid gridUid)
    {
        return Transform(gridUid).MapUid is { } mapUid ? (mapUid, null) : null;
    }
}
