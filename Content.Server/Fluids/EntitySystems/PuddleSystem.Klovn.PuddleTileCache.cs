
using System.Diagnostics.CodeAnalysis;
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
    /// <remarks>
    ///     There is at most one puddle per tile - that is an invariant of the game, enforced by
    ///     <see cref="TrySpillAt(TileRef, Solution, out EntityUid, bool, bool)"/> merging into the puddle already there -
    ///     so each tile maps to exactly one puddle and callers may rely on it. If a second puddle ever shows up on an
    ///     occupied tile, an error is logged and the first one stays cached.
    /// </remarks>
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, Entity<PuddleComponent>>> _gridPuddleCache = new();

    /// <summary>
    ///     Where every cached puddle currently is, so that a moved or removed puddle can be pulled out of the tile it
    ///     used to be on without having to search for it. Does not contain puddles that were rejected as duplicates.
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
        if (!Resolve(puddleUid, ref transformComponent, logMissing: false) ||
            transformComponent.GridUid is not { } gridUid ||
            !_puddleQuery.TryGetComponent(puddleUid, out var puddleComponent) ||
            !_transform.TryGetGridTilePosition((puddleUid, transformComponent), out var indices))
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
            tilePuddles = [];
            _gridPuddleCache[gridUid] = tilePuddles;
        }

        if (tilePuddles.TryGetValue(indices, out var existingPuddle))
        {
            // Only one puddle may exist per tile. Keep the first, and do not record the newcomer's location, so that
            //      its removal later cannot evict the puddle that is actually cached.
            Log.Error($"Puddle {ToPrettyString(puddleUid)} is on tile {indices} of grid {ToPrettyString(gridUid)}, which already has puddle {ToPrettyString(existingPuddle.Owner)}. There must only be one puddle per tile.");
            return;
        }

        tilePuddles[indices] = (puddleUid, puddleComponent);
        _puddleCacheLocations[puddleUid] = (gridUid, indices);
    }

    /// <summary>
    ///     Drops <paramref name="puddleUid"/> from the cache, pruning any grid entry it leaves empty. Does nothing to a
    ///     tile that is cached under a different puddle.
    /// </summary>
    private void RemovePuddleFromCache(EntityUid puddleUid)
    {
        if (!_puddleCacheLocations.Remove(puddleUid, out var location))
            return;

        if (!_gridPuddleCache.TryGetValue(location.GridUid, out var tilePuddles))
            return;

        if (tilePuddles.TryGetValue(location.Indices, out var cachedPuddle) && cachedPuddle.Owner == puddleUid)
            tilePuddles.Remove(location.Indices);

        if (tilePuddles.Count == 0)
            _gridPuddleCache.Remove(location.GridUid);
    }

    #region Public API

    /// <summary>
    ///     Gets the puddle on a tile, if there is one. There is never more than one puddle per tile.
    /// </summary>
    public bool TryGetCachedPuddle(EntityUid gridUid, Vector2i indices, out Entity<PuddleComponent> puddleEntity)
    {
        if (_gridPuddleCache.TryGetValue(gridUid, out var tilePuddles) &&
            tilePuddles.TryGetValue(indices, out puddleEntity))
        {
            return true;
        }

        puddleEntity = (EntityUid.Invalid, default!);
        return false;
    }

    /// <inheritdoc cref="TryGetCachedPuddle(EntityUid, Vector2i, out Entity{PuddleComponent})"/>
    public bool TryGetCachedPuddle(TileRef tileRef, out Entity<PuddleComponent> puddleEntity)
        => TryGetCachedPuddle(tileRef.GridUid, tileRef.GridIndices, out puddleEntity);

    /// <inheritdoc cref="TryGetCachedPuddle(EntityUid, Vector2i, out Entity{PuddleComponent})"/>
    public bool TryGetCachedPuddle(TileRef tileRef, out EntityUid puddleUid)
    {
        var result = TryGetCachedPuddle(tileRef.GridUid, tileRef.GridIndices, out Entity<PuddleComponent> puddleEntity);
        puddleUid = puddleEntity.Owner;

        return result;
    }

    /// <summary>
    ///     Whether a tile has a puddle on it.
    /// </summary>
    public bool HasCachedPuddle(EntityUid gridUid, Vector2i indices)
        => TryGetCachedPuddle(gridUid, indices, out Entity<PuddleComponent> _);

    /// <inheritdoc cref="HasCachedPuddle(EntityUid, Vector2i)"/>
    public bool HasCachedPuddle(TileRef tileRef)
        => HasCachedPuddle(tileRef.GridUid, tileRef.GridIndices);

    /// <summary>
    ///     Gets the puddle on every tile of a grid that has one, keyed by tile. Returns false for a grid with no
    ///     puddles on it at all.
    /// </summary>
    /// <remarks>
    ///     The returned dictionary is the cache's own storage - do not mutate it, and do not hold onto it across
    ///     anything that could spawn or delete a puddle.
    /// </remarks>
    public bool TryGetCachedGridPuddles(EntityUid gridUid, [NotNullWhen(true)] out IReadOnlyDictionary<Vector2i, Entity<PuddleComponent>>? tilePuddleEntities)
    {
        tilePuddleEntities = null;

        if (!_gridPuddleCache.TryGetValue(gridUid, out var gridTilePuddles))
            return false;

        tilePuddleEntities = gridTilePuddles;
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
