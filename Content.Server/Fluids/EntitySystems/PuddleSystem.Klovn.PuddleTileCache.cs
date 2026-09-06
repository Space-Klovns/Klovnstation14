// KS14: added in this fork

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Fluids.Components;
using Content.Shared.GameTicking;
using Robust.Shared.Map;

namespace Content.Server.Fluids.EntitySystems;

public sealed partial class PuddleSystem
{
    /// <summary>
    ///     Every live puddle, keyed by the grid it is on and then by the tile it sits on.
    ///     Kept up to date by <see cref="OnPuddleCacheStartup"/>, <see cref="OnPuddleCacheShutdown"/>
    ///     and <see cref="OnPuddleCacheMove"/>.
    /// </summary>
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, HashSet<EntityUid>>> _gridPuddleCache = new();

    /// <summary>
    ///     Where every cached puddle currently is, so that a moved or removed puddle can be pulled out of the tile it
    ///     used to be on without having to search for it.
    /// </summary>
    private readonly Dictionary<EntityUid, (EntityUid GridUid, Vector2i Indices)> _puddleCacheLocations = new();

    private void InitialisePuddleTileCache()
    {
        SubscribeLocalEvent<PuddleComponent, ComponentStartup>(OnPuddleCacheStartup);
        SubscribeLocalEvent<PuddleComponent, ComponentShutdown>(OnPuddleCacheShutdown);
        SubscribeLocalEvent<PuddleComponent, MoveEvent>(OnPuddleCacheMove);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnPuddleCacheRoundRestartCleanup);
    }

    private void OnPuddleCacheStartup(Entity<PuddleComponent> entity, ref ComponentStartup args)
        => UpdatePuddleCache(entity.Owner);

    private void OnPuddleCacheShutdown(Entity<PuddleComponent> entity, ref ComponentShutdown args)
        => RemovePuddleFromCache(entity.Owner);

    /// <remarks>
    ///     Puddles are anchored, so this only ever fires when one is spawned, parented to a different grid, or
    ///     detached to nullspace - it is not a hot path.
    /// </remarks>
    private void OnPuddleCacheMove(Entity<PuddleComponent> entity, ref MoveEvent args)
        => UpdatePuddleCache(entity.Owner, args.Component);

    private void OnPuddleCacheRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _gridPuddleCache.Clear();
        _puddleCacheLocations.Clear();
    }

    /// <summary>
    ///     Points the cache at wherever <paramref name="puddleUid"/> currently is, dropping it from the cache entirely
    ///     if it is no longer on a grid.
    /// </summary>
    private void UpdatePuddleCache(EntityUid puddleUid, TransformComponent? transformComponent = null)
    {
        if (!Resolve(puddleUid, ref transformComponent, logMissing: false)
            || transformComponent.GridUid is not { } gridUid
            || !_transform.TryGetGridTilePosition((puddleUid, transformComponent), out var indices))
        {
            RemovePuddleFromCache(puddleUid);
            return;
        }

        if (_puddleCacheLocations.TryGetValue(puddleUid, out var oldLocation))
        {
            if (oldLocation.GridUid == gridUid && oldLocation.Indices == indices)
                return;

            RemovePuddleFromCache(puddleUid);
        }

        if (!_gridPuddleCache.TryGetValue(gridUid, out var tilePuddles))
        {
            tilePuddles = new();
            _gridPuddleCache[gridUid] = tilePuddles;
        }

        if (!tilePuddles.TryGetValue(indices, out var puddleUids))
        {
            puddleUids = new();
            tilePuddles[indices] = puddleUids;
        }

        puddleUids.Add(puddleUid);
        _puddleCacheLocations[puddleUid] = (gridUid, indices);
    }

    /// <summary>
    ///     Drops <paramref name="puddleUid"/> from the cache, pruning any tile and grid entries it leaves empty.
    /// </summary>
    private void RemovePuddleFromCache(EntityUid puddleUid)
    {
        if (!_puddleCacheLocations.Remove(puddleUid, out var location))
            return;

        if (!_gridPuddleCache.TryGetValue(location.GridUid, out var tilePuddles))
            return;

        if (tilePuddles.TryGetValue(location.Indices, out var puddleUids))
        {
            puddleUids.Remove(puddleUid);

            if (puddleUids.Count == 0)
                tilePuddles.Remove(location.Indices);
        }

        if (tilePuddles.Count == 0)
            _gridPuddleCache.Remove(location.GridUid);
    }

    #region Public API

    /// <summary>
    ///     Gets every puddle on a tile. There is normally at most one, so prefer <see cref="TryGetCachedPuddle"/>
    ///     unless you actually need to handle the several-puddles-on-one-tile case.
    /// </summary>
    /// <remarks>
    ///     The returned set is the cache's own storage - do not hold onto it across anything that could spawn or
    ///     delete a puddle.
    /// </remarks>
    public bool TryGetCachedPuddles(EntityUid gridUid, Vector2i indices, [NotNullWhen(true)] out IReadOnlySet<EntityUid>? puddleUids)
    {
        puddleUids = null;

        if (!_gridPuddleCache.TryGetValue(gridUid, out var tilePuddles)
            || !tilePuddles.TryGetValue(indices, out var tilePuddleUids))
        {
            return false;
        }

        puddleUids = tilePuddleUids;
        return true;
    }

    /// <inheritdoc cref="TryGetCachedPuddles(EntityUid, Vector2i, out IReadOnlySet{EntityUid}?)"/>
    public bool TryGetCachedPuddles(TileRef tileRef, [NotNullWhen(true)] out IReadOnlySet<EntityUid>? puddleUids)
        => TryGetCachedPuddles(tileRef.GridUid, tileRef.GridIndices, out puddleUids);

    /// <summary>
    ///     Gets a puddle on a tile, if there is one. If a tile somehow holds several puddles, which one you get is
    ///     arbitrary.
    /// </summary>
    public bool TryGetCachedPuddle(EntityUid gridUid, Vector2i indices, out EntityUid puddleUid)
    {
        puddleUid = EntityUid.Invalid;

        if (!TryGetCachedPuddles(gridUid, indices, out var puddleUids))
            return false;

        puddleUid = puddleUids.First();
        return true;
    }

    /// <inheritdoc cref="TryGetCachedPuddle(EntityUid, Vector2i, out EntityUid)"/>
    public bool TryGetCachedPuddle(TileRef tileRef, out EntityUid puddleUid)
        => TryGetCachedPuddle(tileRef.GridUid, tileRef.GridIndices, out puddleUid);

    /// <summary>
    ///     Whether a tile has any puddle on it.
    /// </summary>
    public bool HasCachedPuddle(EntityUid gridUid, Vector2i indices)
        => TryGetCachedPuddles(gridUid, indices, out _);

    /// <inheritdoc cref="HasCachedPuddle(EntityUid, Vector2i)"/>
    public bool HasCachedPuddle(TileRef tileRef)
        => TryGetCachedPuddles(tileRef.GridUid, tileRef.GridIndices, out _);

    /// <summary>
    ///     Gets every puddle on a grid, keyed by tile. Returns false for a grid with no puddles on it at all.
    /// </summary>
    /// <remarks>
    ///     The returned dictionary is the cache's own storage - do not mutate it, and do not hold onto it across
    ///     anything that could spawn or delete a puddle.
    /// </remarks>
    public bool TryGetCachedGridPuddles(EntityUid gridUid, [NotNullWhen(true)] out IReadOnlyDictionary<Vector2i, HashSet<EntityUid>>? tilePuddles)
    {
        tilePuddles = null;

        if (!_gridPuddleCache.TryGetValue(gridUid, out var gridTilePuddles))
            return false;

        tilePuddles = gridTilePuddles;
        return true;
    }

    /// <summary>
    ///     Gets the tile a puddle is cached on, which is where it is unless it has moved without raising a
    ///     <see cref="MoveEvent"/>.
    /// </summary>
    public bool TryGetCachedPuddleTile(EntityUid puddleUid, out EntityUid gridUid, out Vector2i indices)
    {
        gridUid = EntityUid.Invalid;
        indices = default;

        if (!_puddleCacheLocations.TryGetValue(puddleUid, out var location))
            return false;

        (gridUid, indices) = location;
        return true;
    }

    #endregion
}
