using System.Diagnostics.CodeAnalysis;
using Content.Server._KS14.NPC.Squad;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.NPC.Utility;

/// <summary>
///     Finding the grid and tile something stands on, and flooding out over a grid's floor, for the NPC systems
///         that each did so by hand.
/// </summary>
public sealed partial class NpcGridSystem : EntitySystem
{
    [Dependency] private NpcSquadCoverSystem _npcSquadCoverSystem = default!;
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
    ///     Floods out from <paramref name="startTile"/> over floor <paramref name="walkerUid"/> could walk, up to
    ///         <paramref name="maxSteps"/> steps, filling <paramref name="steps"/> with how many steps each tile reached
    ///         took. Never through a wall. With <paramref name="stopAtDoors"/>, a doorway is reached but not gone
    ///         through, so a flood stays in the room or corridor it started in; without, doors are floor like any
    ///         other. The start is always included, even if it is not floor.
    /// </summary>
    /// <remarks>
    ///     What counts as floor, wall and door is the squad cover system's room classification, which the flood
    ///         itself runs in.
    /// </remarks>
    public void FloodTiles(EntityUid walkerUid,
        Entity<MapGridComponent> grid,
        Vector2i startTile,
        int maxSteps,
        Dictionary<Vector2i, int> steps,
        bool stopAtDoors = true)
    {
        _npcSquadCoverSystem.FloodTiles(walkerUid, grid, startTile, maxSteps, steps, stopAtDoors);
    }
}
