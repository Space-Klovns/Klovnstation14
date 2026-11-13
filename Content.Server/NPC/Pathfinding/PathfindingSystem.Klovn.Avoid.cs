// KS14: added in this fork
using Content.Server._KS14.NPC.Doors;
using Content.Server._KS14.NPC.Pushing;
using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.NPC.Pathfinding;

/// <summary>
///     The navmesh is shared by every NPC, so it cannot say that one NPC cannot get through a door another can. An NPC
///         that walked up to a door and could get through by no means at all (see
///         <see cref="NpcDoorSystem.ReportBlocked"/>) has that door's tile treated as a wall in its own path requests
///         for a while, rather than being sent straight back into it. So does the tile of anything loose it walked up
///         to and could not push out of the way (see <see cref="NpcPushSystem.ReportUnpushable"/>), where it is now.
/// </summary>
public sealed partial class PathfindingSystem
{
    [Dependency] private NpcDoorSystem _npcDoorSystem = default!;

    private readonly List<EntityUid> _avoidedEntities = new();

    /// <summary>
    ///     The tiles of the doors <paramref name="entity"/> cannot get through, and of the loose things it cannot push, or
    ///         null if there are none.
    /// </summary>
    private HashSet<(EntityUid Grid, Vector2i Tile)>? GetAvoidedTiles(EntityUid entity)
    {
        _avoidedEntities.Clear();
        _npcDoorSystem.GetBlockedDoors(entity, _avoidedEntities);
        _npcPushSystem.GetUnpushable(entity, _avoidedEntities);

        if (_avoidedEntities.Count == 0)
            return null;

        var avoidedTiles = new HashSet<(EntityUid Grid, Vector2i Tile)>();
        foreach (var avoidedUid in _avoidedEntities)
        {
            var transformComponent = Transform(avoidedUid);
            if (transformComponent.GridUid is not { } gridUid ||
                !TryComp<MapGridComponent>(gridUid, out var gridComponent))
                continue;

            avoidedTiles.Add((gridUid, _maps.TileIndicesFor(gridUid, gridComponent, transformComponent.Coordinates)));
        }

        return avoidedTiles;
    }

    /// <summary>
    ///     A path for <paramref name="entity"/> that keeps off <paramref name="extraAvoidedTiles"/> as well, as if they were
    ///         walls: for a way round somewhere, rather than through it. The set becomes the request's, read by the worker
    ///         threads, and must not be changed after.
    /// </summary>
    public async Task<PathResultEvent> GetPathAvoiding(EntityUid entity,
        EntityCoordinates start,
        EntityCoordinates end,
        float range,
        HashSet<(EntityUid Grid, Vector2i Tile)> extraAvoidedTiles,
        CancellationToken cancelToken,
        PathFlags flags = PathFlags.None)
    {
        var request = GetRequest(entity, start, end, range, cancelToken, flags);

        if (request.AvoidedTiles is { } ownAvoidedTiles)
            extraAvoidedTiles.UnionWith(ownAvoidedTiles);

        request.AvoidedTiles = extraAvoidedTiles;
        return await GetPath(request);
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
