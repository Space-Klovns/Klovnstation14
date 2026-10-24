using Content.Shared.Atmos;
using Content.Shared.Light.Components;
using Content.Shared.Physics;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
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
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private MetaDataSystem _metaDataSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;

    [Dependency] private EntityQuery<ChemicalFireComponent> _chemicalFireQuery = default!;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<ChemicalFireGridComponent> _chemicalFireGridQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;

    /// <summary>
    ///     One paused, never-map-inited singleton per chemfire prototype that's been sustain-checked so far,
    ///         kept in nullspace purely to raise <see cref="ChemicalFireCanSustainEvent"/> against - see
    ///         <see cref="CanSustain"/>. Built identically on client and server (chemfires are predicted, so
    ///         both sides need to be able to ask the same question), even though only the server can answer it
    ///         with real atmos data - see <see cref="ResolveTileMixture"/>. Cleared out on prototype reload.
    /// </summary>
    private readonly Dictionary<EntProtoId, EntityUid> _templateFires = new();

    public override void Initialize()
    {
        base.Initialize();

        InitialiseNetworking();
    }

    /// <summary>
    ///     Drops every cached template singleton so the next sustain check rebuilds it against fresh data.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<EntityPrototype>())
            return;

        foreach (var templateUid in _templateFires.Values)
            Del(templateUid);

        _templateFires.Clear();
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
    /// <param name="duration">
    ///     Overrides the prototype's <see cref="ChemicalFireComponent.Duration"/>, on a fire that is spawned and
    ///         on one that is refreshed in place alike. Null keeps whatever the prototype says.
    /// </param>
    /// <returns>Either the chemfire now occupying the tile, the chemfire already occupying it, or null if no chemfire could not be placed.</returns>
    public Entity<ChemicalFireComponent>? SpawnChemicalFire(
        EntProtoId prototypeId,
        Entity<MapGridComponent?> grid,
        Vector2i tile,
        TimeSpan? duration = null)
    {
        if (!_mapGridQuery.Resolve(grid.Owner, ref grid.Comp, false) ||
            !TryGetPrototypeChemicalFire(prototypeId, out var prototypeComponent))
            return null;

        if (!CanSustain(prototypeId, grid, tile))
            return null;

        var connectionKey = GetConnectionKey(prototypeComponent, prototypeId);

        if (TryGetChemicalFire((grid.Owner, null), tile, connectionKey, out var existingFire))
        {
            // Same prototype: the caller is re-applying an effect, so just retune the existing fire in place.
            if (Prototype(existingFire.Owner)?.ID == prototypeId.Id)
            {
                RefreshFromPrototype(existingFire, prototypeComponent, duration);
                return existingFire;
            }

            // Different prototype sharing the connection key - the tile only has room for one of them.
            // This has to be an immediate deletion, otherwise the old fire would still hold the key below.
            PredictedDel(existingFire.Owner);
        }

        var fireUid = PredictedSpawnAttachedTo(prototypeId, _mapSystem.GridTileToLocal(grid.Owner, grid.Comp, tile));
        if (!_chemicalFireQuery.TryGetComponent(fireUid, out var fireComponent))
        {
            Log.Error($"Prototype {prototypeId} indexed a {nameof(ChemicalFireComponent)} but the spawned entity has none.");
            PredictedDel(fireUid);
            return null;
        }

        // Chemfire prototypes are anchored, so initialisation has usually already snapped the entity onto the
        //     tile it spawned on - anchoring it a second time would double-register it in the snap grid cell.
        var transformComponent = EntityManager.TransformQuery.GetComponent(fireUid);
        if (!transformComponent.Anchored)
            _transformSystem.AnchorEntity((fireUid, transformComponent), (grid.Owner, grid.Comp), tile);

        if (duration is { } overrideDuration)
            SetDuration((fireUid, fireComponent), overrideDuration);

        return (fireUid, fireComponent);
    }

    /// <inheritdoc cref="SpawnChemicalFire(EntProtoId, Entity{MapGridComponent?}, Vector2i, TimeSpan?)"/>
    public Entity<ChemicalFireComponent>? SpawnChemicalFire(EntProtoId prototypeId, EntityCoordinates coordinates, TimeSpan? duration = null)
    {
        var gridUid = _transformSystem.GetGrid(coordinates);
        if (gridUid is not { } validGridUid ||
            !_mapGridQuery.TryGetComponent(validGridUid, out var mapGridComponent))
            return null;

        return SpawnChemicalFire(
            prototypeId,
            (validGridUid, mapGridComponent),
            _mapSystem.CoordinatesToTile(validGridUid, mapGridComponent, coordinates),
            duration
        );
    }

    /// <inheritdoc cref="SpawnChemicalFire(EntProtoId, Entity{MapGridComponent?}, Vector2i, TimeSpan?)"/>
    public Entity<ChemicalFireComponent>? SpawnChemicalFire(EntProtoId prototypeId, TileRef tileRef, TimeSpan? duration = null)
        => SpawnChemicalFire(prototypeId, (tileRef.GridUid, _mapGridQuery.GetComponent(tileRef.GridUid)), tileRef.GridIndices, duration);

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
    ///     Sets how long a chemfire burns for, restarting its lifetime from now.
    /// </summary>
    public void SetDuration(Entity<ChemicalFireComponent> fire, TimeSpan duration)
    {
        fire.Comp.Duration = duration;
        fire.Comp.EndTime = _gameTiming.CurTime + duration;

        Dirty(fire);
    }

    /// <summary>
    ///     Puts a chemfire out.
    /// </summary>
    /// <param name="playSound">Whether to play its <see cref="ChemicalFireComponent.ExtinguishSound"/>.</param>
    public void ExtinguishChemicalFire(Entity<ChemicalFireComponent> fire, bool playSound = false)
    {
        // Only the server plays it: the paths that ask for it (cooling, starving) need the air, which the client
        //     does not have.
        if (playSound)
            _audioSystem.PlayPvs(fire.Comp.ExtinguishSound, Transform(fire.Owner).Coordinates);

        PredictedQueueDel(fire.Owner);
    }

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
            if (fireComponent.BurnsOut && curTime >= fireComponent.EndTime)
            {
                PredictedQueueDel(uid);
                continue;
            }

            if (curTime < fireComponent.NextHeatTime)
                continue;

            fireComponent.NextHeatTime += fireComponent.HeatInterval;

            if (fireComponent.LocalGridUid is not { } gridUid)
                continue;

            var mixture = ResolveTileMixture(gridUid, fireComponent.LocalTile, excite: true);
            var heatEvent = new ChemicalFireHeatTileEvent(gridUid, fireComponent.LocalTile, (float)fireComponent.HeatInterval.TotalSeconds, mixture);
            RaiseLocalEvent(uid, ref heatEvent);
        }
    }

    [SubscribeLocalEvent]
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
        tileEmissionComponent.Range = GetEmissionRange(entity.Comp);
        Dirty(entity.Owner, tileEmissionComponent);

        RegisterFire(entity);

        AfterFireStartup(entity);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<ChemicalFireComponent> entity, ref ComponentShutdown args)
    {
        BeforeFireShutdown(entity);

        UnregisterFire(entity);
    }

    /// <summary>
    ///     Runs once a chemfire is alive and registered on its tile.
    ///     Nothing to do in shared or on the client; the server uses it to announce the new fire to whatever is
    ///         standing in it.
    /// </summary>
    protected virtual void AfterFireStartup(Entity<ChemicalFireComponent> entity) { }

    /// <inheritdoc cref="AfterFireStartup"/>
    /// <remarks>
    ///     Runs while the chemfire is still registered on its tile, so the tile is still resolvable from it.
    /// </remarks>
    protected virtual void BeforeFireShutdown(Entity<ChemicalFireComponent> entity) { }

    /// <summary>
    ///     Resolves a tile's gas mixture, for a heat tick or a sustain check alike. Atmos doesn't exist
    ///         client-side, so we use an estimate on the client and a real lookup on the server.
    /// </summary>
    /// <param name="excite">
    ///     Whether the lookup should wake the tile up for atmos processing - true for an actual heat tick,
    ///         false for a sustain check probing whether a fire could survive there, which should not have
    ///         side effects of its own.
    /// </param>
    protected abstract GasMixture? ResolveTileMixture(EntityUid gridUid, Vector2i tile, bool excite);

    /// <summary>
    ///     Whether a chemfire of this prototype could survive being placed on this tile right now. Raises
    ///         <see cref="ChemicalFireCanSustainEvent"/> against the prototype's template singleton - see that
    ///         type's remarks for what a "template singleton" is and why it has to be built the way it is.
    ///     Runs identically client and server side, since chemfires spawn through prediction; the client just
    ///         has no <see cref="GasMixture"/> to hand the event (<see cref="ResolveTileMixture"/> is a no-op
    ///         there), so any veto that needs real atmos data effectively only bites once the server confirms
    ///         or corrects the prediction.
    /// </summary>
    private bool CanSustain(EntProtoId prototypeId, Entity<MapGridComponent?> grid, Vector2i tile)
    {
        if (!ResolveTemplateFire(prototypeId, out var templateUid))
            return true; // No ChemicalFireComponent to speak of - SpawnChemicalFire's own check already covers this.

        if (IsWall(grid, tile))
            return false;

        var mixture = ResolveTileMixture(grid.Owner, tile, excite: false);
        if (mixture != null &&
            _chemicalFireQuery.TryGetComponent(templateUid, out var templateComponent) &&
            IsTooCold(templateComponent, mixture))
            return false;
        var ev = new ChemicalFireCanSustainEvent(grid.Owner, tile, mixture);
        RaiseLocalEvent(templateUid, ref ev);

        return ev.CanSustain;
    }

    /// <summary>
    ///     Whether a tile is a wall, or a window: something anchored on it with a hard fixture on the
    ///         <see cref="CollisionGroup.Impassable"/> layer, which nothing walks through. No chemfire burns inside one.
    /// </summary>
    /// <remarks>
    ///     Only anchored entities are looked at, rather than <c>TurfSystem.IsTileBlocked</c>'s lookup of everything
    ///         on the tile: walls are always anchored, and this runs on every puddle's tile effect.
    /// </remarks>
    public bool IsWall(Entity<MapGridComponent?> grid, Vector2i tile)
    {
        if (!_mapGridQuery.Resolve(grid.Owner, ref grid.Comp, false))
            return false;

        var anchoredEnumerator = _mapSystem.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, tile);
        while (anchoredEnumerator.MoveNext(out var anchoredUid))
        {
            if (!_fixturesQuery.TryGetComponent(anchoredUid, out var fixturesComponent))
                continue;

            foreach (var fixture in fixturesComponent.Fixtures.Values)
            {
                if (fixture.Hard && (fixture.CollisionLayer & (int)CollisionGroup.Impassable) != 0)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Whether the air is colder than a chemfire's <see cref="ChemicalFireComponent.MinimumAirTemperature"/>.
    /// </summary>
    public static bool IsTooCold(ChemicalFireComponent component, GasMixture mixture)
        => component.MinimumAirTemperature is { } minimumTemperature && mixture.Temperature < minimumTemperature;

    /// <summary>
    ///     Sets how close foam is to smothering a chemfire, which the client draws as the flames guttering, and dims
    ///         its glow to match. Only dirties the fire when the networked step changes.
    /// </summary>
    public void SetSmotherProgress(Entity<ChemicalFireComponent> fire, float progress)
    {
        var stepped = MathF.Floor(Math.Clamp(progress, 0f, 1f) / ChemicalFireComponent.SmotherProgressStep) *
                      ChemicalFireComponent.SmotherProgressStep;
        if (stepped == fire.Comp.SmotherProgress)
            return;

        fire.Comp.SmotherProgress = stepped;
        Dirty(fire);

        if (TryComp<TileEmissionComponent>(fire.Owner, out var tileEmissionComponent))
        {
            tileEmissionComponent.Range = GetEmissionRange(fire.Comp);
            Dirty(fire.Owner, tileEmissionComponent);
        }
    }

    /// <summary>
    ///     How far a chemfire's tile emission reaches, which shrinks as foam smothers it.
    /// </summary>
    public static float GetEmissionRange(ChemicalFireComponent component)
        => component.EmissionRange * (1f - ChemicalFireComponent.SmotheredShrink * component.SmotherProgress);

    /// <summary>
    ///     Gets or lazily creates the paused, nullspace singleton chemfire used to answer sustain checks for a
    ///         prototype. Never map-inited - <see cref="EntityManager.InitializeAndStartEntity"/> is called
    ///         with <c>doMapInit: false</c> - because a real spawn's <c>MapInitEvent</c> could trigger
    ///         spawn-on-init effects a prototype might carry (e.g. thermite's spark), and pausing the entity
    ///         does not prevent that; it only has to run <c>ComponentStartup</c> to be fully queryable.
    /// </summary>
    private bool ResolveTemplateFire(EntProtoId prototypeId, out EntityUid templateUid)
    {
        if (_templateFires.TryGetValue(prototypeId, out templateUid) && Exists(templateUid))
            return true;

        if (!ProtoMan.TryIndex<EntityPrototype>(prototypeId, out var entityPrototype) ||
            !entityPrototype.TryGetComponent(out ChemicalFireComponent? _, _componentFactory))
        {
            templateUid = default;
            return false;
        }

        templateUid = EntityManager.CreateEntityUninitialized(prototypeId, MapCoordinates.Nullspace);
        EntityManager.InitializeAndStartEntity(templateUid, doMapInit: false);

        _metaDataSystem.SetEntityPaused(templateUid, true);

        _templateFires[prototypeId] = templateUid;
        return true;
    }

    [SubscribeLocalEvent]
    private void OnEntParentChanged(Entity<ChemicalFireComponent> entity, ref EntParentChangedMessage args)
    {
        if (!entity.Comp.Running)
            return;

        // Deleting an entity detaches it to nullspace before shutting its components down, so handling that
        //     detach here would forget the tile the fire is on before BeforeFireShutdown ever gets to see it.
        //     The shutdown unregisters the fire itself, so there is nothing to do for a dying one.
        if (TerminatingOrDeleted(entity.Owner))
            return;

        UnregisterFire(entity);
        RegisterFire(entity, args.Transform);
    }

    /// <summary>
    ///     Copies every datafield off a chemfire prototype's component onto an existing chemfire, restarting
    ///         its lifetime. Fields are copied by hand rather than reflected over, since this runs on
    ///         every re-application.
    /// </summary>
    private void RefreshFromPrototype(Entity<ChemicalFireComponent> fire, ChemicalFireComponent prototypeComponent, TimeSpan? duration = null)
    {
        var component = fire.Comp;
        var curTime = _gameTiming.CurTime;

        component.Duration = duration ?? prototypeComponent.Duration;
        component.Color = prototypeComponent.Color;
        component.Temperature = prototypeComponent.Temperature;
        component.ExposedVolume = prototypeComponent.ExposedVolume;
        component.HeatPower = prototypeComponent.HeatPower;
        component.PuddleHeatFraction = prototypeComponent.PuddleHeatFraction;
        component.Extinguishable = prototypeComponent.Extinguishable;
        component.BurnsOut = prototypeComponent.BurnsOut;
        component.MinimumAirTemperature = prototypeComponent.MinimumAirTemperature;
        component.FoamSmotherable = prototypeComponent.FoamSmotherable;
        component.FoamSmotherResistance = prototypeComponent.FoamSmotherResistance;
        component.FoamHeatFraction = prototypeComponent.FoamHeatFraction;
        component.SmotherRecoveryRate = prototypeComponent.SmotherRecoveryRate;
        component.ExtinguishSound = prototypeComponent.ExtinguishSound;
        component.FoamResistSound = prototypeComponent.FoamResistSound;
        component.HeatInterval = prototypeComponent.HeatInterval;
        component.EmissionRange = prototypeComponent.EmissionRange;
        component.ConnectionKey = prototypeComponent.ConnectionKey;
        component.Sprite = prototypeComponent.Sprite;
        component.SpriteOffset = prototypeComponent.SpriteOffset;
        component.SpriteVariations = prototypeComponent.SpriteVariations;
        component.UnderStatePrefix = prototypeComponent.UnderStatePrefix;
        component.OverStatePrefix = prototypeComponent.OverStatePrefix;

        component.EndTime = curTime + component.Duration;
        component.NextHeatTime = curTime + component.HeatInterval;

        Dirty(fire);

        if (TryComp<TileEmissionComponent>(fire.Owner, out var tileEmissionComponent))
        {
            tileEmissionComponent.Color = component.Color;
            tileEmissionComponent.Range = GetEmissionRange(component);
            Dirty(fire.Owner, tileEmissionComponent);
        }

        RaiseTileChanged(component.LocalGridUid, component.LocalTile);
    }

    private bool TryGetPrototypeChemicalFire(EntProtoId prototypeId, out ChemicalFireComponent prototypeComponent)
    {
        prototypeComponent = default!;

        if (!ProtoMan.TryIndex(prototypeId, out var entityPrototype) ||
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
        transformComponent ??= EntityManager.TransformQuery.GetComponent(entity.Owner);

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
