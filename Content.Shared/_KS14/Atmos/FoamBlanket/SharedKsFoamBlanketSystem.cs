using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared._KS14.Atmos.Flammable;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.FixedPoint;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Temperature.HeatContainer;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
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
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<FlammableComponent> _flammableQuery = default!;
    [Dependency] private EntityQuery<KsFoamBlanketComponent> _blanketQuery = default!;
    [Dependency] private EntityQuery<KsFoamBlanketGridComponent> _blanketGridQuery = default!;
    [Dependency] private EntityQuery<KsInFoamComponent> _inFoamQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;

    private readonly HashSet<Entity<FlammableComponent>> _flammableEntities = [];
    private readonly List<Entity<ChemicalFireComponent>> _smotheredFires = [];
    private readonly List<Entity<ChemicalFireComponent>> _resistingFires = [];
    private readonly List<EntityUid> _releasedOccupants = [];

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
    ///     Whether the blanket is the one on its tile's cache, which is what lets it act at all: smother what walks
    ///         onto it, and keep what touches it from burning. A dissolving blanket, one taken off its tile, and one
    ///         another blanket has replaced on the cache (the client's own, once the server's has arrived) are not.
    /// </summary>
    public bool IsActive(Entity<KsFoamBlanketComponent> blanket)
    {
        return blanket.Comp.DissolveEndTime == null &&
               blanket.Comp.LocalGridUid is { } gridUid &&
               _blanketGridQuery.TryComp(gridUid, out var gridComponent) &&
               gridComponent.Tiles.TryGetValue(blanket.Comp.LocalTile, out var registeredUid) &&
               registeredUid == blanket.Owner;
    }

    /// <summary>
    ///     Sets how much foam the blanket holds, keeping <see cref="KsFoamBlanketComponent.Heat"/>'s capacity in step at
    ///         the temperature it was: foam that drains or boils off takes its own share of the heat with it.
    /// </summary>
    protected void SetFoam(Entity<KsFoamBlanketComponent> blanket, float foam)
    {
        blanket.Comp.Foam = foam;
        if (foam > 0f)
            blanket.Comp.Heat = new HeatContainer(foam * blanket.Comp.SpecificHeat, blanket.Comp.Heat.Temperature);
    }

    /// <summary>
    ///     Energy, in joules, that boiling a unit of the blanket's foam takes: warming it from its temperature to its
    ///         boiling point, then its latent heat.
    /// </summary>
    public static float GetEnergyPerUnit(KsFoamBlanketComponent component)
        => component.SpecificHeat * MathF.Max(0f, component.BoilingPoint - component.Heat.Temperature) + component.LatentHeat;

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
    ///     Puts out the atmos fire on a blanket's tile. Atmos is server-only, so nothing happens by default.
    /// </summary>
    /// <returns>Whether there was a fire to put out.</returns>
    protected virtual bool SmotherTileAtmos(Entity<KsFoamBlanketComponent> blanket, EntityUid gridUid, Vector2i tile) => false;

    /// <summary>
    ///     Whether the blanket keeps what is in it from catching fire. Only the server, which has the air, ever says no.
    /// </summary>
    protected virtual bool ProtectsOccupants(Entity<KsFoamBlanketComponent> blanket) => true;

    /// <summary>
    ///     Foam has landed on a chemfire it does not put out at once: one that fights it
    ///         (<see cref="ChemicalFireComponent.FoamSmotherResistance"/>), or one it cannot smother at all
    ///         (<see cref="ChemicalFireComponent.FoamSmotherable"/>). The server runs the fight and tells whoever can see.
    /// </summary>
    protected virtual void OnFoamReachedResistingFire(Entity<ChemicalFireComponent> fire)
    {
    }

    /// <summary>
    ///     Spawns a blanket the client lays ahead of the server and keeps until the server's arrives. Only the client
    ///         has such a thing; anywhere else it is the ordinary predicted spawn.
    /// </summary>
    protected virtual EntityUid SpawnPlaceholderBlanket(EntProtoId prototypeId, EntityCoordinates coordinates)
        => PredictedSpawnAttachedTo(prototypeId, coordinates);

    /// <summary>
    ///     Whether a blanket is a transient client placeholder that should stay on the cache until the server blanket
    ///         actually replaces it.
    /// </summary>
    protected virtual bool IsPlaceholderBlanket(EntityUid blanketUid)
        => false;

    /// <summary>
    ///     A shot laying placeholders has crossed a tile that already has a blanket. If it is the client's own
    ///         placeholder, it now waits for this shot's server blanket as well as the earlier one's, so the client
    ///         restarts its expiry. Does nothing anywhere else.
    /// </summary>
    protected virtual void OnPlaceholderCrossed(Entity<KsFoamBlanketComponent> blanket)
    {
    }

    /// <summary>
    ///     Lays a blanket on a tile, or tops up the one already there, and smothers whatever is burning on it.
    /// </summary>
    /// <param name="availableFoam">Units of foam on offer; at most <see cref="KsFoamBlanketComponent.FoamPerTile"/> of it is used.</param>
    /// <param name="playSound">
    ///     Whether to play the smother sound. A client predicting a shot's blankets passes false: the server lays its
    ///         own through the vapor and plays the sizzle to everyone, the shooter included.
    /// </param>
    /// <param name="placeholder">
    ///     Whether a new blanket is the client's own placeholder rather than a predicted spawn: the client keeps it
    ///         until the server's blanket for the tile arrives, instead of losing it at the next state. See
    ///         <see cref="SpawnPlaceholderBlanket"/>. Only the client passes true.
    /// </param>
    /// <param name="temperature">
    ///     The temperature, in kelvin, of the foam being laid, which mixes into the blanket's heat in proportion to how
    ///         much is laid. <see cref="KsFoamBlanketComponent.DefaultTemperature"/> if not given.
    /// </param>
    /// <returns>The units of foam used, floored to the 0.01u a solution stores; zero if no blanket was laid.</returns>
    public float Deploy(EntProtoId prototypeId, TileRef tileRef, float availableFoam, bool playSound = true, bool placeholder = false, float? temperature = null)
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
                var laidHeat = new HeatContainer(used * blanket.Comp.SpecificHeat, temperature ?? blanket.Comp.DefaultTemperature);
                HeatContainerHelpers.MergeInto(ref blanket.Comp.Heat, ref laidHeat);
                blanket.Comp.Foam += used;
                Dirty(blanket);
                OnBlanketUpdated(blanket);
            }

            // Even when it is full and takes nothing: the server's vapor for this shot still lays foam here.
            if (placeholder)
                OnPlaceholderCrossed(blanket);
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
            var blanketUid = placeholder
                ? SpawnPlaceholderBlanket(prototypeId, coordinates)
                : PredictedSpawnAttachedTo(prototypeId, coordinates);
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
            blanketComponent.Heat = new HeatContainer(used * blanketComponent.SpecificHeat, temperature ?? blanketComponent.DefaultTemperature);
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
    ///         Chemfires that fight the foam are not put out here, but start losing to it (see
    ///         <see cref="OnFoamReachedResistingFire"/>).
    /// </summary>
    private void Smother(Entity<KsFoamBlanketComponent> blanket, EntityUid gridUid, Vector2i tile, EntityCoordinates coordinates, bool playSound)
    {
        var smothered = SmotherTileAtmos(blanket, gridUid, tile);

        if (_chemicalFireSystem.GetTileChemicalFires((gridUid, null), tile) is { } tileFires)
        {
            // Collected first: putting a fire out changes the tile's fire data.
            _smotheredFires.Clear();
            _resistingFires.Clear();
            foreach (var fire in tileFires.Fires.Values)
            {
                if (fire.Comp.FoamSmotherable && fire.Comp.FoamSmotherResistance <= 0f)
                    _smotheredFires.Add(fire);
                else
                    _resistingFires.Add(fire);
            }

            foreach (var fire in _smotheredFires)
            {
                _chemicalFireSystem.ExtinguishChemicalFire(fire);
                smothered = true;
            }

            foreach (var fire in _resistingFires)
            {
                OnFoamReachedResistingFire(fire);
                // It hisses against the foam; one that ignores the foam entirely does not, and only roars on.
                smothered |= fire.Comp.FoamSmotherable;
            }

            _smotheredFires.Clear();
            _resistingFires.Clear();
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
        args.Continue = IsActive(entity);
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
        if (!IsActive(entity) ||
            !_flammableQuery.TryComp(args.Tripper, out var flammableComponent) ||
            !TryExtinguish((args.Tripper, flammableComponent)))
            return;

        if (_netManager.IsClient && _playerManager.LocalEntity != args.Tripper)
            return;

        _audioSystem.PlayPredicted(entity.Comp.SmotherSound, args.Tripper, args.Tripper);
    }

    /// <summary>
    ///     A chemfire foam can smother cannot start, or be refreshed, under a blanket. One the foam cannot smother at
    ///         all (an electrical fire) burns under it regardless.
    /// </summary>
    /// <remarks>
    ///     A fire that fights the foam (<see cref="ChemicalFireComponent.FoamSmotherResistance"/>) and is already
    ///         burning on the tile is still refreshed, so that it burns on while the fight lasts, rather than being put
    ///         out by its own fuel no longer topping it up. Once the foam has won, nothing relights it while the foam
    ///         holds.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnChemicalFireCanSustain(Entity<ChemicalFireComponent> entity, ref ChemicalFireCanSustainEvent args)
    {
        if (!entity.Comp.FoamSmotherable || !TryGetBlanket(args.GridUid, args.Tile, out _))
            return;

        if (entity.Comp.FoamSmotherResistance > 0f &&
            _chemicalFireSystem.TryGetChemicalFire((args.GridUid, null), args.Tile, _chemicalFireSystem.GetConnectionKey(entity), out _))
            return;

        args.Deny();
    }

    /// <summary>
    ///     Something flammable that touches an active blanket is in foam until it touches none.
    /// </summary>
    /// <remarks>
    ///     Any of its fixtures counts, not only hard ones as for the step trigger: a flammable entity's own
    ///         <c>flammable</c> fixture is non-hard, and is what an item lying in the foam touches it with.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnStartCollide(Entity<KsFoamBlanketComponent> entity, ref StartCollideEvent args)
    {
        var otherUid = args.OtherEntity;
        if (!IsActive(entity) || !_flammableQuery.HasComp(otherUid) || TerminatingOrDeleted(otherUid))
            return;

        EnsureComp<KsInFoamComponent>(otherUid).Blankets.Add(entity.Owner);
    }

    /// <summary>
    ///     Leaving a blanket, which for something touching it with several fixtures is when the last of them stops.
    /// </summary>
    /// <remarks>
    ///     Also raised when the contact is destroyed with the blanket still touching, as it is when either entity
    ///         is deleted or stops colliding (<c>SharedPhysicsSystem.DestroyContact</c>). Removing the component
    ///         here touches no contacts, so it is safe even inside the client's state reset.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnEndCollide(Entity<KsFoamBlanketComponent> entity, ref EndCollideEvent args)
    {
        var otherUid = args.OtherEntity;
        if (!_inFoamQuery.TryComp(otherUid, out var inFoamComponent) ||
            !inFoamComponent.Blankets.Contains(entity.Owner) ||
            IsTouching(otherUid, entity.Owner))
            return;

        LeaveBlanket((otherUid, inFoamComponent), entity.Owner);
    }

    /// <summary>
    ///     Whether any of the entity's contacts, other than ones being destroyed, is touching the blanket.
    /// </summary>
    private bool IsTouching(EntityUid uid, EntityUid blanketUid)
    {
        var contacts = _physicsSystem.GetContacts(uid);
        while (contacts.MoveNext(out var contact))
        {
            if (contact.IsTouching && (contact.EntityA == blanketUid || contact.EntityB == blanketUid))
                return true;
        }

        return false;
    }

    private void LeaveBlanket(Entity<KsInFoamComponent> entity, EntityUid blanketUid)
    {
        if (!entity.Comp.Blankets.Remove(blanketUid) || entity.Comp.Blankets.Count > 0)
            return;

        RemComp(entity.Owner, entity.Comp);
    }

    /// <summary>
    ///     Takes a blanket that has stopped acting out of everything in foam because of it, whether or not they still
    ///         touch it.
    /// </summary>
    private void ReleaseOccupants(EntityUid blanketUid)
    {
        // Without fixtures nothing ever touched it, and the physics system cannot list the contacts of what has none.
        if (!_fixturesQuery.HasComp(blanketUid))
            return;

        // Including contacts being destroyed: this can run while the blanket is being deleted, part way through.
        _releasedOccupants.Clear();
        var contacts = _physicsSystem.GetContacts(blanketUid, includeDeleting: true);
        while (contacts.MoveNext(out var contact))
            _releasedOccupants.Add(contact.EntityA == blanketUid ? contact.EntityB : contact.EntityA);

        // Collected first: removing a component while walking the contacts is not something to rely on.
        foreach (var occupantUid in _releasedOccupants)
        {
            if (_inFoamQuery.TryComp(occupantUid, out var inFoamComponent))
                LeaveBlanket((occupantUid, inFoamComponent), blanketUid);
        }

        _releasedOccupants.Clear();
    }

    /// <summary>
    ///     Nothing that can be put out catches fire, or keeps burning, while it is in foam, unless the air over the foam
    ///         is too hot for it (<see cref="ProtectsOccupants"/>).
    /// </summary>
    [SubscribeLocalEvent]
    private void OnInFoamBurnAttempt(Entity<KsInFoamComponent> entity, ref KsBurnAttemptEvent args)
    {
        if (args.Cancelled ||
            !_flammableQuery.TryComp(entity, out var flammableComponent) ||
            !flammableComponent.CanExtinguish)
            return;

        // Checked rather than trusted: a blanket that went without its occupants hearing of it must not keep them out.
        foreach (var blanketUid in entity.Comp.Blankets)
        {
            if (!_blanketQuery.TryComp(blanketUid, out var blanketComponent) ||
                !IsActive((blanketUid, blanketComponent)) ||
                !ProtectsOccupants((blanketUid, blanketComponent)))
                continue;

            args.Cancel();
            return;
        }
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
        var tiles = EnsureComp<KsFoamBlanketGridComponent>(gridUid).Tiles;
        tiles.TryGetValue(tile, out var previousUid);

        var shouldReplacePrevious = !previousUid.IsValid() || previousUid == entity.Owner
            ? true
            : ShouldSupersedePreviousBlanket(previousUid, entity.Owner);

        if (shouldReplacePrevious)
            tiles[tile] = entity.Owner;

        entity.Comp.LocalGridUid = gridUid;
        entity.Comp.LocalTile = tile;

        if (shouldReplacePrevious && previousUid.IsValid() && previousUid != entity.Owner)
            OnBlanketSuperseded(previousUid);
    }

    /// <summary>
    ///     Whether a new blanket on a tile should retire the previous one from the cache. The client keeps its own
    ///         placeholders alive while another client placeholder is simply a later pass over the same tile.
    /// </summary>
    protected virtual bool ShouldSupersedePreviousBlanket(EntityUid previousUid, EntityUid newUid)
        => true;

    /// <summary>
    ///     Called when another blanket takes over a tile's cache entry from <paramref name="previousUid"/>, which
    ///         then stops acting. On the client, this is how its own placeholder gives way to the server's blanket.
    /// </summary>
    /// <remarks>
    ///     Can run during state application, as the server's blanket arrives, so an override must not destroy
    ///         contacts here (deleting a client-side entity does); defer that.
    /// </remarks>
    protected virtual void OnBlanketSuperseded(EntityUid previousUid)
    {
        ReleaseOccupants(previousUid);
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
        ReleaseOccupants(entity.Owner);

        // Only drop the entry if it is still ours: a new blanket may have been laid on the tile since.
        if (_blanketGridQuery.TryComp(gridUid, out var gridComponent) &&
            gridComponent.Tiles.TryGetValue(entity.Comp.LocalTile, out var registeredUid) &&
            registeredUid == entity.Owner)
            gridComponent.Tiles.Remove(entity.Comp.LocalTile);
    }
}
