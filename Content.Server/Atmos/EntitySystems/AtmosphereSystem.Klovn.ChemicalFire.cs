// KS14: added in this fork
using Content.Shared.Atmos.Components;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    /// <summary>
    ///     Whether atmospherics is running an actual gas fire on a tile.
    /// </summary>
    /// <remarks>
    ///     <see cref="IsHotspotActive"/> asks the grid whether the tile <em>reads</em> as burning, which anything
    ///         subscribed to <see cref="IsHotspotActiveMethodEvent"/> may answer - chemfires do exactly that, so
    ///         that extinguishers reach them. Callers that need to know whether there is a hotspot rather than
    ///         something standing on the tile claiming to be one want this instead.
    /// </remarks>
    public bool HasGasHotspot(Entity<GridAtmosphereComponent?> grid, Vector2i tile)
    {
        if (!Resolve(grid.Owner, ref grid.Comp, false))
            return false;

        return grid.Comp.Tiles.TryGetValue(tile, out var tileAtmosphere) && tileAtmosphere.Hotspot.Valid;
    }
}
