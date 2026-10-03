// KS14: added in this fork
using Content.Server._KS14.NPC.Doors;
using Robust.Shared.Map.Components;

namespace Content.Server.NPC.Pathfinding;

/// <summary>
///     The navmesh is shared by every NPC, so it cannot say that one NPC cannot get through a door another can. An NPC
///         that walked up to a door and could get through by no means at all (see
///         <see cref="NpcDoorSystem.ReportBlocked"/>) has that door's tile treated as a wall in its own path requests
///         for a while, rather than being sent straight back into it.
/// </summary>
public sealed partial class PathfindingSystem
{
    [Dependency] private NpcDoorSystem _npcDoorSystem = default!;

    private readonly List<EntityUid> _blockedDoors = new();

    /// <summary>
    ///     The tiles of the doors <paramref name="entity"/> cannot get through, or null if there are none.
    /// </summary>
    private HashSet<(EntityUid Grid, Vector2i Tile)>? GetAvoidedTiles(EntityUid entity)
    {
        _blockedDoors.Clear();
        _npcDoorSystem.GetBlockedDoors(entity, _blockedDoors);

        if (_blockedDoors.Count == 0)
            return null;

        var avoidedTiles = new HashSet<(EntityUid Grid, Vector2i Tile)>();
        foreach (var doorUid in _blockedDoors)
        {
            var transformComponent = Transform(doorUid);
            if (transformComponent.GridUid is not { } gridUid ||
                !TryComp<MapGridComponent>(gridUid, out var gridComponent))
                continue;

            avoidedTiles.Add((gridUid, _maps.TileIndicesFor(gridUid, gridComponent, transformComponent.Coordinates)));
        }

        return avoidedTiles;
    }

    /// <summary>
    ///     Whether <paramref name="poly"/> lies on one of <paramref name="avoidedTiles"/>. Read on the worker threads.
    /// </summary>
    private static bool IsAvoided(HashSet<(EntityUid Grid, Vector2i Tile)> avoidedTiles, PathPoly poly)
    {
        var tile = poly.ChunkOrigin * ChunkSize + new Vector2i(poly.TileIndex / ChunkSize, poly.TileIndex % ChunkSize);
        return avoidedTiles.Contains((poly.GraphUid, tile));
    }
}
