using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.NPC.Utility;

/// <summary>
///     Finding the grid and tile something stands on, for the NPC systems
///         that each did so by hand.
/// </summary>
public sealed partial class NpcGridSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;

    /// <summary>
    ///     Resolves <paramref name="gridUid"/> to its grid. False if it is null or not a grid.
    /// </summary>
    public bool TryGetGrid(EntityUid? gridUid, out Entity<MapGridComponent> grid)
    {
        if (gridUid is { } uid && _mapGridQuery.TryComp(uid, out var mapGridComponent))
        {
            grid = new Entity<MapGridComponent>(uid, mapGridComponent);
            return true;
        }

        grid = default;
        return false;
    }

    /// <summary>
    ///     Resolves the grid <paramref name="coordinates"/> are on, and the tile they are in. False if they are not on
    ///         a grid.
    /// </summary>
    public bool TryGetGridTile(EntityCoordinates coordinates, out Entity<MapGridComponent> grid, out Vector2i tile)
    {
        if (TryGetGrid(_transformSystem.GetGrid(coordinates), out grid))
        {
            tile = _mapSystem.TileIndicesFor(grid, coordinates);
            return true;
        }

        tile = default;
        return false;
    }

    /// <summary>
    ///     The centre of <paramref name="tile"/> on <paramref name="gridUid"/>, relative to the grid.
    /// </summary>
    public EntityCoordinates GetTileCenter(EntityUid gridUid, Vector2i tile)
    {
        return _mapGridQuery.TryComp(gridUid, out var mapGridComponent)
            ? new EntityCoordinates(gridUid, _mapSystem.TileCenterToVector((gridUid, mapGridComponent), tile))
            : new EntityCoordinates(gridUid, new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
    }
}
