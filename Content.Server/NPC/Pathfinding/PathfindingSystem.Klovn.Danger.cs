// KS14: added in this fork
using Content.Server._KS14.NPC.Dangers;
using Content.Server._KS14.NPC.Pathfinding;
using Content.Server.Atmos.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.NPC.Pathfinding;

/// <summary>
///     Tiles that are dangerous to walk onto, for NPCs that avoid that sort of danger. See
///         <see cref="NpcAvoidsTileDangerComponent"/>. Fires start and go out all the time, and the navmesh is shared and dear
///         to rebuild, so dangers are kept beside it, and only raise what a poly costs a request that avoids them.
///         Raising a cost also leaves the coarse maps' estimates good: they never come out above the true cost. Tiles touching
///         a danger cost a little more too, so that a path gives it room rather than turning hard beside it at a run.
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
    ///     What a tile poses, and what it and the tiles touching it pose between them.
    /// </summary>
    private struct TileDanger
    {
        public NpcTileDangerKind Posed;
        public NpcTileDangerKind Near;
    }

    /// <summary>
    ///     Each tile of a chunk, by <see cref="PathPoly.TileIndex"/>. Only chunks with a danger in or next to them. Tiles
    ///         near one are worked out here rather than looked up on the way, as a search reads this far more often.
    /// </summary>
    private readonly Dictionary<PathChunkKey, TileDanger[]> _tileDangers = new();

    /// <summary>
    ///     Cleared arrays from <see cref="_tileDangers"/>, so that a fire burning on does not allocate.
    /// </summary>
    private readonly Stack<TileDanger[]> _spareTileDangers = new();

    private readonly List<(EntityUid Grid, Vector2i Tile)> _gasFires = new();

    private TimeSpan _nextTileDangerRefresh;

    /// <summary>
    ///     What was burning, and where, at the last refresh: a sum of hashes, so the order things are found in does not count.
    /// </summary>
    private int _tileDangerHash;

    /// <summary>
    ///     Goes up whenever what is dangerous, and where, changes. The same at two moments means a path found between them
    ///         was found knowing all there is to know about the dangers now.
    /// </summary>
    public int TileDangerVersion { get; private set; }

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
        return _tileDangers.TryGetValue(PathChunkKey.Of(poly), out var tiles) && (tiles[poly.TileIndex].Posed & dangers) != 0x0;
    }

    /// <summary>
    ///     Whether <paramref name="coordinates"/> are on a tile posing any of <paramref name="dangers"/>.
    /// </summary>
    public bool IsDangerous(EntityCoordinates coordinates, NpcTileDangerKind dangers)
    {
        return _transform.GetGrid(coordinates) is { } gridUid &&
            _mapGridQuery.TryComp(gridUid, out var mapGridComponent) &&
            IsDangerous(gridUid, _maps.TileIndicesFor(gridUid, mapGridComponent, coordinates), dangers);
    }

    /// <inheritdoc cref="IsDangerous(EntityCoordinates, NpcTileDangerKind)"/>
    public bool IsDangerous(EntityUid gridUid, Vector2i tile, NpcTileDangerKind dangers)
    {
        return _tileDangers.TryGetValue(new PathChunkKey(gridUid, GetOrigin(tile)), out var tiles) &&
            (tiles[GetTileIndex(tile)].Posed & dangers) != 0x0;
    }

    /// <summary>
    ///     Whether <paramref name="poly"/> is on, or on a tile touching, one posing any of <paramref name="dangers"/>.
    /// </summary>
    public bool IsNearDanger(PathPoly poly, NpcTileDangerKind dangers)
    {
        return _tileDangers.TryGetValue(PathChunkKey.Of(poly), out var tiles) && (tiles[poly.TileIndex].Near & dangers) != 0x0;
    }

    /// <summary>
    ///     What the tile at <paramref name="coordinates"/> poses, and what it and the tiles touching it pose between them, to
    ///         anyone, for asking about a place rather than on behalf of an NPC that avoids something.
    /// </summary>
    public (NpcTileDangerKind Posed, NpcTileDangerKind Near) GetDangersAt(EntityCoordinates coordinates)
    {
        RefreshTileDangers();

        if (_transform.GetGrid(coordinates) is not { } gridUid || !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return default;

        var tile = _maps.TileIndicesFor(gridUid, mapGridComponent, coordinates);

        if (!_tileDangers.TryGetValue(new PathChunkKey(gridUid, GetOrigin(tile)), out var tiles))
            return default;

        ref var tileDanger = ref tiles[GetTileIndex(tile)];
        return (tileDanger.Posed, tileDanger.Near);
    }

    /// <summary>
    ///     How many times what <paramref name="poly"/> would otherwise cost it costs <paramref name="request"/>.
    /// </summary>
    private float GetDangerCost(PathRequest request, PathPoly poly)
    {
        if (!_tileDangers.TryGetValue(PathChunkKey.Of(poly), out var tiles))
            return 1f;

        ref var tile = ref tiles[poly.TileIndex];

        if ((tile.Posed & request.AvoidedDangers) != 0x0)
            return request.DangerCost;

        return (tile.Near & request.AvoidedDangers) != 0x0 ? request.DangerMarginCost : 1f;
    }

    private T WithAvoidedDangers<T>(T request, EntityUid entity) where T : PathRequest
    {
        request.AvoidedDangers = GetAvoidedDangers(entity);

        if (request.AvoidedDangers != NpcTileDangerKind.None)
        {
            var avoidsTileDangerComponent = _avoidsTileDangerQuery.Comp(entity);
            request.DangerCost = avoidsTileDangerComponent.Cost;
            request.DangerMarginCost = avoidsTileDangerComponent.MarginCost;
        }

        return request;
    }

    /// <summary>
    ///     Starts <paramref name="request"/>'s search over as if its NPC avoided nothing. Its heuristic stands: it never
    ///         knew of dangers.
    /// </summary>
    private static void RestartIgnoringDangers(AStarPathRequest request, PathPoly startNode)
    {
        request.AvoidedDangers = NpcTileDangerKind.None;
        request.IgnoredDangers = true;
        request.CostSoFar.Clear();
        request.CameFrom.Clear();
        request.PolyFrontier!.Clear();
        request.PolyFrontier.Add(0.0f, startNode);
        request.CostSoFar[startNode] = 0.0f;
    }

    /// <summary>
    ///     Works dangers out again right now, whether that is due or not. For tests.
    /// </summary>
    internal void RefreshTileDangersNow()
    {
        _nextTileDangerRefresh = TimeSpan.Zero;
        RefreshTileDangers();
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

        var hash = 0;
        var enumerator = EntityQueryEnumerator<NpcTileDangerComponent, TransformComponent>();
        while (enumerator.MoveNext(out var tileDangerComponent, out var transformComponent))
        {
            if (transformComponent.GridUid is { } gridUid && _mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            {
                var tile = _maps.TileIndicesFor(gridUid, mapGridComponent, transformComponent.Coordinates);
                AddTileDanger(gridUid, tile, tileDangerComponent.Dangers);
                hash += HashCode.Combine(gridUid, tile, tileDangerComponent.Dangers);
            }
        }

        // Gas fires are not entities: they are atmospherics' own, by tile.
        _gasFires.Clear();
        _atmosphereSystem.GetGasFires(_gasFires);

        foreach (var (gridUid, tile) in _gasFires)
        {
            AddTileDanger(gridUid, tile, NpcTileDangerKind.Fire);
            hash += HashCode.Combine(gridUid, tile, NpcTileDangerKind.Fire);
        }

        AddBlastDangers(ref hash);

        if (hash == _tileDangerHash)
            return;

        _tileDangerHash = hash;
        TileDangerVersion++;
    }

    private void AddTileDanger(EntityUid gridUid, Vector2i tile, NpcTileDangerKind dangers)
    {
        var origin = GetOrigin(tile);
        var tiles = GetChunkDangers(gridUid, origin);

        tiles[GetTileIndex(tile)].Posed |= dangers;

        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                var near = tile + new Vector2i(x, y);
                var nearOrigin = GetOrigin(near);

                // Nearly always the one chunk, and fires come in their hundreds.
                if (nearOrigin != origin)
                {
                    origin = nearOrigin;
                    tiles = GetChunkDangers(gridUid, origin);
                }

                tiles[GetTileIndex(near)].Near |= dangers;
            }
        }
    }

    private TileDanger[] GetChunkDangers(EntityUid gridUid, Vector2i origin)
    {
        var key = new PathChunkKey(gridUid, origin);

        if (!_tileDangers.TryGetValue(key, out var tiles))
        {
            tiles = _spareTileDangers.TryPop(out var spareTiles) ? spareTiles : new TileDanger[ChunkSize * ChunkSize];
            _tileDangers[key] = tiles;
        }

        return tiles;
    }

    private static int GetTileIndex(Vector2i tile)
    {
        return MathHelper.Mod(tile.X, ChunkSize) * ChunkSize + MathHelper.Mod(tile.Y, ChunkSize);
    }
}
