using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.FixedPoint;
using Content.Shared.StepTrigger.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Atmos.FoamBlanket;

/// <summary>
///     Lays foam blankets, smothers what burns under them, keeps the per-grid foam blanket cache, and keeps
///         chemfires from starting under a blanket. Shared, since chemfires spawn through prediction and the client
///         has to give the same answer, and so that a blanket can be laid and stepped on in prediction.
/// </summary>
/// <remarks>
///     What only the server can see (atmos hotspots, the flammable component's fire state) is behind
///         <see cref="SmotherTileAtmos"/> and <see cref="IsBurning"/>.
/// </remarks>
public abstract partial class SharedKsFoamBlanketSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedChemicalFireSystem _chemicalFireSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<FlammableComponent> _flammableQuery = default!;
    [Dependency] private EntityQuery<KsFoamBlanketGridComponent> _blanketGridQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;

    private readonly HashSet<Entity<FlammableComponent>> _flammableEntities = [];
    private readonly List<Entity<ChemicalFireComponent>> _smotheredFires = [];

    /// <summary>
    ///     The blanket on a tile, if there is one.
    /// </summary>
    public bool TryGetBlanket(EntityUid gridUid, Vector2i tile, out Entity<KsFoamBlanketComponent> blanket)
    {
        blanket = default;

        if (!_blanketGridQuery.TryComp(gridUid, out var gridComponent) ||
            !gridComponent.Tiles.TryGetValue(tile, out var blanketUid) ||
            !TryComp<KsFoamBlanketComponent>(blanketUid, out var blanketComponent) ||
            blanketComponent.DissolveEndTime != null)
            return false;

        blanket = (blanketUid, blanketComponent);
        return true;
    }

    /// <summary>
    ///     Whether any blanket, dissolving or not, is on the tile's cache.
    /// </summary>
    public bool IsTileRegistered(EntityUid gridUid, Vector2i tile)
        => _blanketGridQuery.TryComp(gridUid, out var gridComponent) && gridComponent.Tiles.ContainsKey(tile);

    /// <summary>
    ///     Whether the blanket has run out of foam and is dissolving away.
    /// </summary>
    public bool IsDissolving(Entity<KsFoamBlanketComponent> blanket)
        => blanket.Comp.DissolveEndTime != null;

    /// <summary>
    ///     Starts a blanket whose foam has run out dissolving. It leaves the tile's cache at once, so a new
    ///         blanket can be laid on the tile and chemfires are no longer kept out; it is removed after
    ///         <see cref="KsFoamBlanketComponent.DissolveDuration"/>.
    /// </summary>
    public void StartDissolving(Entity<KsFoamBlanketComponent> blanket)
    {
        if (blanket.Comp.DissolveEndTime != null)
            return;

        blanket.Comp.Foam = 0f;
        blanket.Comp.DissolveEndTime = _gameTiming.CurTime + blanket.Comp.DissolveDuration;
        Unregister(blanket);
        Dirty(blanket);
    }

    /// <summary>
    ///     Whether something is on fire in a way a blanket can put out. The client only knows what the appearance
    ///         data tells it, the server reads the flammable component.
    /// </summary>
    protected abstract bool IsBurning(Entity<FlammableComponent> flammable);

    /// <summary>
    ///     Puts out the atmos fire on a tile. Atmos is server-only, so nothing happens by default.
    /// </summary>
    /// <returns>Whether there was a fire to put out.</returns>
    protected virtual bool SmotherTileAtmos(EntityUid gridUid, Vector2i tile) => false;

    /// <summary>
    ///     Lays a blanket on a tile, or tops up the one already there, and smothers whatever is burning on it.
    /// </summary>
    /// <param name="availableFoam">Units of foam on offer; at most <see cref="KsFoamBlanketComponent.FoamPerTile"/> of it is used.</param>
    /// <param name="playSound">
    ///     Whether to play the smother sound. A client predicting a shot's blankets passes false: the server lays its
    ///         own through the vapor and plays the sizzle to everyone, the shooter included.
    /// </param>
    /// <returns>The units of foam used, floored to the 0.01u a solution stores; zero if no blanket was laid.</returns>
    public float Deploy(EntProtoId prototypeId, TileRef tileRef, float availableFoam, bool playSound = true)
    {
        var gridUid = tileRef.GridUid;
        var tile = tileRef.GridIndices;

        if (availableFoam <= 0f || !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return 0f;

        var coordinates = _mapSystem.GridTileToLocal(gridUid, mapGridComponent, tile);
        float used;

        if (TryGetBlanket(gridUid, tile, out var blanket))
        {
            used = (float)FixedPoint2.New(MathF.Min(MathF.Min(availableFoam, blanket.Comp.FoamPerTile), blanket.Comp.MaxFoam - blanket.Comp.Foam));
            if (used > 0f)
            {
                blanket.Comp.Foam += used;
                Dirty(blanket);
            }
        }
        else
        {
            if (!ProtoMan.Index(prototypeId).TryComp<KsFoamBlanketComponent>(out var prototypeComponent, Factory))
            {
                Log.Error($"Foam blanket prototype {prototypeId} has no {nameof(KsFoamBlanketComponent)}.");
                return 0f;
            }

            used = (float)FixedPoint2.New(MathF.Min(MathF.Min(availableFoam, prototypeComponent.FoamPerTile), prototypeComponent.MaxFoam));
            if (used < prototypeComponent.MinimumFoam)
                return 0f;

            // The engine's predicted spawn: the server's blanket replaces the client's, and outlives it by a long way.
            var blanketUid = PredictedSpawnAttachedTo(prototypeId, coordinates);
            if (!TryComp<KsFoamBlanketComponent>(blanketUid, out var blanketComponent))
            {
                Log.Error($"Foam blanket prototype {prototypeId} spawned an entity with no {nameof(KsFoamBlanketComponent)}.");
                PredictedDel(blanketUid);
                return 0f;
            }

            // Foam prototypes are anchored, so initialisation has usually already snapped the entity onto its tile;
            //     anchoring it a second time would double-register it in the snap grid cell.
            var transformComponent = EntityManager.TransformQuery.GetComponent(blanketUid);
            if (!transformComponent.Anchored)
                _transformSystem.AnchorEntity((blanketUid, transformComponent), (gridUid, mapGridComponent), tile);

            blanketComponent.Foam = used;
            blanketComponent.NextUpdate = _gameTiming.CurTime + blanketComponent.UpdateInterval;
            Dirty(blanketUid, blanketComponent);
            blanket = (blanketUid, blanketComponent);
        }

        // Smothers on a top-up as well: spraying a blanket again is how you put out what rolled back onto it.
        Smother(blanket, gridUid, tile, coordinates, playSound);
        return used;
    }

    /// <summary>
    ///     Puts out the tile's fire and everything burning on it, with a sizzle if there was anything to put out.
    /// </summary>
    private void Smother(Entity<KsFoamBlanketComponent> blanket, EntityUid gridUid, Vector2i tile, EntityCoordinates coordinates, bool playSound)
    {
        var smothered = SmotherTileAtmos(gridUid, tile);

        if (_chemicalFireSystem.GetTileChemicalFires((gridUid, null), tile) is { } tileFires)
        {
            // Collected first: putting a fire out changes the tile's fire data.
            _smotheredFires.Clear();
            foreach (var fire in tileFires.Fires.Values)
            {
                if (fire.Comp.Extinguishable)
                    _smotheredFires.Add(fire);
            }

            foreach (var fire in _smotheredFires)
            {
                _chemicalFireSystem.ExtinguishChemicalFire(fire);
                smothered = true;
            }

            _smotheredFires.Clear();
        }

        _flammableEntities.Clear();
        _entityLookupSystem.GetLocalEntitiesIntersecting(gridUid, tile, _flammableEntities, flags: LookupFlags.Uncontained);

        foreach (var flammable in _flammableEntities)
            smothered |= TryExtinguish(flammable);

        _flammableEntities.Clear();

        // No predicted user: Deploy is called by the server, or by a client that has chosen to stay quiet.
        if (smothered && playSound)
            _audioSystem.PlayPredicted(blanket.Comp.SmotherSound, coordinates, user: null);
    }

    private bool TryExtinguish(Entity<FlammableComponent> flammable)
    {
        if (!IsBurning(flammable))
            return false;

        // Extinguishing zeroes the fire stacks as well, so there is nothing left to adjust them by.
        var extinguishEvent = new ExtinguishEvent { FireStacksAdjustment = 0f };
        RaiseLocalEvent(flammable.Owner, ref extinguishEvent);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnStepTriggerAttempt(Entity<KsFoamBlanketComponent> entity, ref StepTriggerAttemptEvent args)
    {
        args.Continue = entity.Comp.DissolveEndTime == null;
    }

    /// <summary>
    ///     Puts out whatever walks onto the blanket on fire.
    /// </summary>
    /// <remarks>
    ///     Step triggers can be raised again when prediction re-runs; the client's audio system only plays a
    ///         predicted sound on the first time predicted (<c>IsFirstTimePredicted</c>), so the sound is not repeated.
    ///     Step triggers also run on the client outside prediction, for every tripper it can see, and
    ///         <c>IsFirstTimePredicted</c> is true there. The server plays the sound to everyone but the tripper, so a
    ///         client only plays it for its own player, or anyone watching would hear it twice.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnStepTriggered(Entity<KsFoamBlanketComponent> entity, ref StepTriggeredOffEvent args)
    {
        if (entity.Comp.DissolveEndTime != null ||
            !_flammableQuery.TryComp(args.Tripper, out var flammableComponent) ||
            !TryExtinguish((args.Tripper, flammableComponent)))
            return;

        if (_netManager.IsClient && _playerManager.LocalEntity != args.Tripper)
            return;

        _audioSystem.PlayPredicted(entity.Comp.SmotherSound, args.Tripper, args.Tripper);
    }

    /// <summary>
    ///     A chemfire that can be put out cannot start, or be refreshed, under a blanket. Thermite and its like
    ///         burn through one.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnChemicalFireCanSustain(Entity<ChemicalFireComponent> entity, ref ChemicalFireCanSustainEvent args)
    {
        if (!entity.Comp.Extinguishable || !TryGetBlanket(args.GridUid, args.Tile, out _))
            return;

        args.Deny();
    }

    /// <summary>
    ///     Called once a blanket has started up, and whenever its foam is handed new state; for the client to
    ///         redraw it. Only one subscription may take the startup event, so the client hooks in here.
    /// </summary>
    protected virtual void OnBlanketUpdated(Entity<KsFoamBlanketComponent> entity)
    {
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsFoamBlanketComponent> entity, ref ComponentStartup args)
    {
        Register(entity);
        OnBlanketUpdated(entity);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsFoamBlanketComponent> entity, ref ComponentShutdown args)
    {
        Unregister(entity);
    }

    /// <summary>
    ///     A blanket that changes parent is on another tile or grid, or on none, so its registration is redone.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnEntParentChanged(Entity<KsFoamBlanketComponent> entity, ref EntParentChangedMessage args)
    {
        // Deleting an entity detaches it first; its shutdown unregisters it.
        if (entity.Comp.LifeStage > ComponentLifeStage.Running || TerminatingOrDeleted(entity.Owner))
            return;

        Unregister(entity);
        Register(entity);
    }

    /// <summary>
    ///     A blanket is a skin on its tile. Once unanchored, which also happens when the tile under it turns to
    ///         space, it no longer smothers or keeps chemfires out, and the server removes it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnAnchorStateChanged(Entity<KsFoamBlanketComponent> entity, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored ||
            entity.Comp.LifeStage > ComponentLifeStage.Running ||
            TerminatingOrDeleted(entity.Owner))
            return;

        Unregister(entity);

        if (_netManager.IsServer)
            PredictedQueueDel(entity.Owner);
    }

    /// <summary>
    ///     Puts a blanket on its tile's cache, unless it is already dissolving.
    /// </summary>
    private void Register(Entity<KsFoamBlanketComponent> entity)
    {
        if (entity.Comp.DissolveEndTime != null)
            return;

        var transformComponent = Transform(entity);
        if (transformComponent.GridUid is not { } gridUid ||
            !_transformSystem.TryGetGridTilePosition((entity.Owner, transformComponent), out var tile))
            return;

        // Replacing rather than adding: one blanket per tile is kept by whoever lays them, and a stray duplicate
        //     must not wedge the cache.
        EnsureComp<KsFoamBlanketGridComponent>(gridUid).Tiles[tile] = entity.Owner;

        entity.Comp.LocalGridUid = gridUid;
        entity.Comp.LocalTile = tile;
    }

    /// <summary>
    ///     Takes a blanket off its tile's cache, if it is still the one there.
    /// </summary>
    /// <remarks>
    ///     Also how the client drops a blanket that arrives dissolving, or starts to, since only the server calls
    ///         <see cref="StartDissolving"/>.
    /// </remarks>
    protected void Unregister(Entity<KsFoamBlanketComponent> entity)
    {
        if (entity.Comp.LocalGridUid is not { } gridUid)
            return;

        entity.Comp.LocalGridUid = null;

        // Only drop the entry if it is still ours: a new blanket may have been laid on the tile since.
        if (_blanketGridQuery.TryComp(gridUid, out var gridComponent) &&
            gridComponent.Tiles.TryGetValue(entity.Comp.LocalTile, out var registeredUid) &&
            registeredUid == entity.Owner)
            gridComponent.Tiles.Remove(entity.Comp.LocalTile);
    }
}
