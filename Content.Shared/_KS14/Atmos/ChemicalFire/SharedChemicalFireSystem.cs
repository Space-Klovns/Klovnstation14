using Content.Shared._KS14.PredictedSpawning;
using Content.Shared.Light.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Atmos.ChemicalFire;

/// <summary>
///     Owns the lifetime of every chemfire: spawning them through <see cref="SpawnChemicalFire"/>, keeping the
///         per-grid <see cref="ChemicalFireGridComponent"/> cache in sync, expiring them, and raising
///         <see cref="ChemicalFireHeatTileEvent"/> so that atmos ignition and gas consumption can act on the
///         tile without re-resolving anything.
/// </summary>
public abstract partial class SharedChemicalFireSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private KsSharedPredictedSpawnSystem _predictedSpawnSystem = default!;

    [Dependency] private EntityQuery<ChemicalFireComponent> _chemicalFireQuery = default!;
    [Dependency] private EntityQuery<ChemicalFireGridComponent> _chemicalFireGridQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _transformQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChemicalFireComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ChemicalFireComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ChemicalFireComponent, EntParentChangedMessage>(OnEntParentChanged);

        InitialiseNetworking();
    }

    #region Public API

    /// <summary>
    ///     Spawns a chemfire of the given prototype on a tile, or refreshes/replaces whatever chemfire already
    ///         holds that prototype's connection key there.
    /// </summary>
    /// <remarks>
    ///     Only one chemfire per connection key may occupy a tile. Adding the same prototype again just copies
    ///         the prototype's data onto the existing chemfire (resetting its duration); adding a different
    ///         prototype that shares the connection key replaces the existing one outright. Chemfires with
    ///         differing connection keys stack freely on one tile.
    /// </remarks>
    /// <returns>The chemfire now occupying the tile, or null if it could not be placed.</returns>
    public Entity<ChemicalFireComponent>? SpawnChemicalFire(EntProtoId prototypeId, Entity<MapGridComponent?> grid, Vector2i tile)
    {
        if (!_mapGridQuery.Resolve(grid.Owner, ref grid.Comp, false) ||
            !TryGetPrototypeChemicalFire(prototypeId, out var prototypeComponent))
            return null;

        var connectionKey = GetConnectionKey(prototypeComponent, prototypeId);

        if (TryGetChemicalFire((grid.Owner, null), tile, connectionKey, out var existingFire))
        {
            // Same prototype: the caller is re-applying an effect, so just retune the existing fire in place.
            if (Prototype(existingFire.Owner)?.ID == prototypeId.Id)
            {
                RefreshFromPrototype(existingFire, prototypeComponent);
                return existingFire;
            }

            // Different prototype sharing the connection key - the tile only has room for one of them.
            // This has to be an immediate deletion, otherwise the old fire would still hold the key below.
            PredictedDel(existingFire.Owner);
        }

        var fireUid = _predictedSpawnSystem.PredictedSpawnAttachedTo(prototypeId, _mapSystem.GridTileToLocal(grid.Owner, grid.Comp, tile));
        if (!_chemicalFireQuery.TryGetComponent(fireUid, out var fireComponent))
        {
            Log.Error($"Prototype {prototypeId} indexed a {nameof(ChemicalFireComponent)} but the spawned entity has none.");
            PredictedDel(fireUid);
            return null;
        }

        // Chemfire prototypes are anchored, so initialisation has usually already snapped the entity onto the
        //     tile it spawned on - anchoring it a second time would double-register it in the snap grid cell.
        var transformComponent = _transformQuery.GetComponent(fireUid);
        if (!transformComponent.Anchored)
            _transformSystem.AnchorEntity((fireUid, transformComponent), (grid.Owner, grid.Comp), tile);

        return (fireUid, fireComponent);
    }

    /// <inheritdoc cref="SpawnChemicalFire(EntProtoId, Entity{MapGridComponent?}, Vector2i)"/>
    public Entity<ChemicalFireComponent>? SpawnChemicalFire(EntProtoId prototypeId, EntityCoordinates coordinates)
    {
        var gridUid = _transformSystem.GetGrid(coordinates);
        if (gridUid is not { } validGridUid ||
            !_mapGridQuery.TryGetComponent(validGridUid, out var mapGridComponent))
            return null;

        return SpawnChemicalFire(prototypeId, (validGridUid, mapGridComponent), _mapSystem.CoordinatesToTile(validGridUid, mapGridComponent, coordinates));
    }

    /// <summary>
    ///     Looks up the chemfire holding <paramref name="connectionKey"/> on a tile. O(1), no entity lookup.
    /// </summary>
    public bool TryGetChemicalFire(Entity<ChemicalFireGridComponent?> grid, Vector2i tile, string connectionKey, out Entity<ChemicalFireComponent> fire)
    {
        fire = default;

        if (!_chemicalFireGridQuery.Resolve(grid.Owner, ref grid.Comp, false) ||
            !grid.Comp.Tiles.TryGetValue(tile, out var tileData) ||
            !tileData.Fires.TryGetValue(connectionKey, out var foundFire))
            return false;

        fire = foundFire;
        return true;
    }

    /// <summary>
    ///     The chemfires on a tile, keyed by connection key, or null if the tile holds none.
    /// </summary>
    public TileChemicalFireData<Entity<ChemicalFireComponent>>? GetTileChemicalFires(Entity<ChemicalFireGridComponent?> grid, Vector2i tile)
    {
        if (!_chemicalFireGridQuery.Resolve(grid.Owner, ref grid.Comp, false) ||
            !grid.Comp.Tiles.TryGetValue(tile, out var tileData))
            return null;

        return tileData;
    }

    /// <summary>
    ///     Puts a chemfire out.
    /// </summary>
    public void ExtinguishChemicalFire(Entity<ChemicalFireComponent> fire)
        => PredictedQueueDel(fire.Owner);

    /// <summary>
    ///     The key a chemfire smooths and deduplicates by, falling back to its prototype id.
    /// </summary>
    public string GetConnectionKey(Entity<ChemicalFireComponent> fire)
        => fire.Comp.ConnectionKey ?? Prototype(fire.Owner)?.ID ?? string.Empty;

    #endregion

    #region Lifetime

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;

        var enumerator = EntityQueryEnumerator<ChemicalFireComponent>();
        while (enumerator.MoveNext(out var uid, out var fireComponent))
        {
            if (curTime >= fireComponent.EndTime)
            {
                PredictedQueueDel(uid);
                continue;
            }

            if (curTime < fireComponent.NextHeatTime)
                continue;

            fireComponent.NextHeatTime += fireComponent.HeatInterval;

            if (fireComponent.LocalGridUid is not { } gridUid)
                continue;

            var heatEvent = new ChemicalFireHeatTileEvent(gridUid, fireComponent.LocalTile, (float)fireComponent.HeatInterval.TotalSeconds);
            RaiseLocalEvent(uid, ref heatEvent);
        }
    }

    private void OnStartup(Entity<ChemicalFireComponent> entity, ref ComponentStartup args)
    {
        var curTime = _gameTiming.CurTime;

        // The server owns EndTime; on the client it arrives via component state, and a predicted-spawn
        //     client fire schedules its own so it still expires if the server never confirms it.
        if (entity.Comp.EndTime == TimeSpan.Zero)
            entity.Comp.EndTime = curTime + entity.Comp.Duration;

        entity.Comp.NextHeatTime = curTime + entity.Comp.HeatInterval;

        var tileEmissionComponent = EnsureComp<TileEmissionComponent>(entity.Owner);
        tileEmissionComponent.Color = entity.Comp.Color;
        tileEmissionComponent.Range = entity.Comp.EmissionRange;
        Dirty(entity.Owner, tileEmissionComponent);

        RegisterFire(entity);
    }

    private void OnShutdown(Entity<ChemicalFireComponent> entity, ref ComponentShutdown args)
        => UnregisterFire(entity);

    private void OnEntParentChanged(Entity<ChemicalFireComponent> entity, ref EntParentChangedMessage args)
    {
        if (!entity.Comp.Running)
            return;

        UnregisterFire(entity);
        RegisterFire(entity, args.Transform);
    }

    /// <summary>
    ///     Copies every datafield off a chemfire prototype's component onto an existing chemfire, restarting
    ///         its lifetime. Fields are copied by hand rather than reflected over, since this runs on
    ///         every re-application.
    /// </summary>
    private void RefreshFromPrototype(Entity<ChemicalFireComponent> fire, ChemicalFireComponent prototypeComponent)
    {
        var component = fire.Comp;
        var curTime = _gameTiming.CurTime;

        component.Duration = prototypeComponent.Duration;
        component.Color = prototypeComponent.Color;
        component.Temperature = prototypeComponent.Temperature;
        component.ExposedVolume = prototypeComponent.ExposedVolume;
        component.HeatInterval = prototypeComponent.HeatInterval;
        component.EmissionRange = prototypeComponent.EmissionRange;
        component.ConnectionKey = prototypeComponent.ConnectionKey;
        component.SpriteVariations = prototypeComponent.SpriteVariations;
        component.UnderStatePrefix = prototypeComponent.UnderStatePrefix;
        component.OverStatePrefix = prototypeComponent.OverStatePrefix;

        component.EndTime = curTime + component.Duration;
        component.NextHeatTime = curTime + component.HeatInterval;

        Dirty(fire);

        if (TryComp<TileEmissionComponent>(fire.Owner, out var tileEmissionComponent))
        {
            tileEmissionComponent.Color = component.Color;
            tileEmissionComponent.Range = component.EmissionRange;
            Dirty(fire.Owner, tileEmissionComponent);
        }

        RaiseTileChanged(component.LocalGridUid, component.LocalTile);
    }

    private bool TryGetPrototypeChemicalFire(EntProtoId prototypeId, out ChemicalFireComponent prototypeComponent)
    {
        prototypeComponent = default!;

        if (!_prototypeManager.TryIndex(prototypeId, out var entityPrototype) ||
            !entityPrototype.TryGetComponent(out ChemicalFireComponent? foundComponent, _componentFactory))
            return false;

        prototypeComponent = foundComponent;
        return true;
    }

    private static string GetConnectionKey(ChemicalFireComponent component, EntProtoId prototypeId)
        => component.ConnectionKey ?? prototypeId.Id;

    #endregion

    #region Grid cache

    private void RegisterFire(Entity<ChemicalFireComponent> entity, TransformComponent? transformComponent = null)
    {
        transformComponent ??= _transformQuery.GetComponent(entity.Owner);

        if (transformComponent.GridUid is not { } gridUid ||
            !_transformSystem.TryGetGridTilePosition((entity.Owner, transformComponent), out var tile))
            return;

        var gridComponent = EnsureComp<ChemicalFireGridComponent>(gridUid);
        var tileData = gridComponent.Tiles.GetOrNew(tile);

        // Replacing rather than adding: SpawnChemicalFire already guarantees uniqueness per key, and being
        //     lenient here keeps a mispredicted or hand-spawned duplicate from wedging the cache.
        tileData.Fires[GetConnectionKey(entity)] = entity;

        entity.Comp.LocalGridUid = gridUid;
        entity.Comp.LocalTile = tile;
        Dirty(entity);

        Dirty(gridUid, gridComponent);
        RaiseTileChanged(gridUid, tile);
    }

    private void UnregisterFire(Entity<ChemicalFireComponent> entity)
    {
        if (entity.Comp.LocalGridUid is not { } gridUid)
            return;

        var tile = entity.Comp.LocalTile;

        entity.Comp.LocalGridUid = null;
        Dirty(entity);

        if (!_chemicalFireGridQuery.TryGetComponent(gridUid, out var gridComponent) ||
            !gridComponent.Tiles.TryGetValue(tile, out var tileData))
            return;

        var connectionKey = GetConnectionKey(entity);

        // Only drop the entry if it is still ours - a replacement may already have claimed the key.
        if (!tileData.Fires.TryGetValue(connectionKey, out var registeredFire) ||
            registeredFire.Owner != entity.Owner)
            return;

        tileData.Fires.Remove(connectionKey);
        if (tileData.Fires.Count == 0)
            gridComponent.Tiles.Remove(tile);

        Dirty(gridUid, gridComponent);
        RaiseTileChanged(gridUid, tile);
    }

    /// <summary>
    ///     Announces that a tile's set of chemfires changed, so visuals can resmooth it and its neighbours.
    /// </summary>
    protected void RaiseTileChanged(EntityUid? gridUid, Vector2i tile)
    {
        if (gridUid is not { } validGridUid)
            return;

        var tileChangedEvent = new ChemicalFireTileChangedEvent(validGridUid, tile);
        RaiseLocalEvent(ref tileChangedEvent);
    }

    #endregion
}
