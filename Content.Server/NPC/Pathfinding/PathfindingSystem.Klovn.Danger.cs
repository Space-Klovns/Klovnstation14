// KS14: added in this fork
using Content.Server._KS14.NPC.Dangers;
using Content.Server._KS14.NPC.Pathfinding;
using Content.Server.Atmos.EntitySystems;
using Robust.Shared.Map.Components;

namespace Content.Server.NPC.Pathfinding;

/// <summary>
///     Tiles that are dangerous to walk onto, for NPCs that avoid that sort of danger. See
///         <see cref="NpcAvoidsTileDangerComponent"/>. Fires start and go out all the time, and the navmesh is shared and dear
///         to rebuild, so dangers are kept beside it, and only raise what a poly costs a request that avoids them.
///         Raising a cost also leaves the coarse maps' estimates good: they never come out above the true cost.
/// </summary>
/// <remarks>
///     Worked out on demand, at most every <see cref="TileDangerRefreshInterval"/> and only for an NPC that avoids
///         something, so with none about this costs nothing, and otherwise what is burning rather than the size of the map.
///         Read on the search threads, written here: steering, which asks for this, never runs alongside a search.
/// </remarks>
public sealed partial class PathfindingSystem
{
    private static readonly TimeSpan TileDangerRefreshInterval = TimeSpan.FromSeconds(0.5);

    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;

    [Dependency] private EntityQuery<NpcAvoidsTileDangerComponent> _avoidsTileDangerQuery = default!;

    /// <summary>
    ///     What each tile of a chunk poses, by <see cref="PathPoly.TileIndex"/>. Only chunks with a danger in them.
    /// </summary>
    private readonly Dictionary<PathChunkKey, NpcTileDangerKind[]> _tileDangers = new();

    /// <summary>
    ///     Cleared arrays from <see cref="_tileDangers"/>, so that a fire burning on does not allocate.
    /// </summary>
    private readonly Stack<NpcTileDangerKind[]> _spareTileDangers = new();

    private readonly List<(EntityUid Grid, Vector2i Tile)> _gasFires = new();

    private TimeSpan _nextTileDangerRefresh;

    /// <summary>
    ///     What <paramref name="uid"/> steers clear of, of what there is right now: none, for most NPCs, or if there is no
    ///         danger about at all.
    /// </summary>
    public NpcTileDangerKind GetAvoidedDangers(EntityUid uid)
    {
        if (!_avoidsTileDangerQuery.TryComp(uid, out var avoidsTileDangerComponent) ||
            avoidsTileDangerComponent.Dangers == NpcTileDangerKind.None)
            return NpcTileDangerKind.None;

        RefreshTileDangers();
        return _tileDangers.Count > 0 ? avoidsTileDangerComponent.Dangers : NpcTileDangerKind.None;
    }

    /// <summary>
    ///     Whether <paramref name="poly"/> is on a tile posing any of <paramref name="dangers"/>.
    /// </summary>
    public bool IsDangerous(PathPoly poly, NpcTileDangerKind dangers)
    {
        return _tileDangers.TryGetValue(PathChunkKey.Of(poly), out var tiles) && (tiles[poly.TileIndex] & dangers) != 0x0;
    }

    private T WithAvoidedDangers<T>(T request, EntityUid entity) where T : PathRequest
    {
        request.AvoidedDangers = GetAvoidedDangers(entity);

        if (request.AvoidedDangers != NpcTileDangerKind.None)
            request.DangerCost = _avoidsTileDangerQuery.Comp(entity).Cost;

        return request;
    }

    private void RefreshTileDangers()
    {
        var now = _timing.CurTime;
        if (now < _nextTileDangerRefresh)
            return;

        _nextTileDangerRefresh = now + TileDangerRefreshInterval;

        foreach (var tiles in _tileDangers.Values)
        {
            Array.Clear(tiles);
            _spareTileDangers.Push(tiles);
        }

        _tileDangers.Clear();

        var enumerator = EntityQueryEnumerator<NpcTileDangerComponent, TransformComponent>();
        while (enumerator.MoveNext(out var tileDangerComponent, out var transformComponent))
        {
            if (transformComponent.GridUid is { } gridUid && _mapGridQuery.TryComp(gridUid, out var mapGridComponent))
                AddTileDanger(gridUid, _maps.TileIndicesFor(gridUid, mapGridComponent, transformComponent.Coordinates), tileDangerComponent.Dangers);
        }

        // Gas fires are not entities: they are atmospherics' own, by tile.
        _gasFires.Clear();
        _atmosphereSystem.GetGasFires(_gasFires);

        foreach (var (gridUid, tile) in _gasFires)
        {
            AddTileDanger(gridUid, tile, NpcTileDangerKind.Fire);
        }
    }

    private void AddTileDanger(EntityUid gridUid, Vector2i tile, NpcTileDangerKind dangers)
    {
        var key = new PathChunkKey(gridUid, GetOrigin(tile));

        if (!_tileDangers.TryGetValue(key, out var tiles))
        {
            tiles = _spareTileDangers.TryPop(out var spareTiles) ? spareTiles : new NpcTileDangerKind[ChunkSize * ChunkSize];
            _tileDangers[key] = tiles;
        }

        tiles[MathHelper.Mod(tile.X, ChunkSize) * ChunkSize + MathHelper.Mod(tile.Y, ChunkSize)] |= dangers;
    }
}
