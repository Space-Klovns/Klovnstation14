using System.Numerics;
using Content.Shared._KS14.PredictedSpawning;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Physics;
using Robust.Client.Player;
using Robust.Client.Timing;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Client._KS14.PredictedSpawning;

/// <inheritdoc/>
public sealed partial class KsPredictedSpawnSystem : KsSharedPredictedSpawnSystem
{
    [Dependency] private IClientGameTiming _clientGameTiming = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SharedPointLightSystem _pointLightSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<KsKeptPredictedSpawnComponent> _keptQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;

    /// <summary>
    ///     How far, in metres, a server copy may be from a kept copy for the two to be matched as one spawn.
    /// </summary>
    private const float MaxPairDistance = 4f;

    /// <summary>
    ///     The time constant, in seconds, of a kept copy's pull towards its server copy: each such span closes about
    ///         two thirds of the gap between them.
    /// </summary>
    private const float CorrectionTime = 0.1f;

    /// <summary>
    ///     How long a kept copy waits for its server copy before deciding none is coming.
    /// </summary>
    private static readonly TimeSpan UnpairedLifetime = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Server copies hidden here that are not matched to a kept copy yet.
    /// </summary>
    private readonly HashSet<EntityUid> _unpairedServerCopies = new();

    /// <summary>
    ///     A pairing of a newly arrived server copy with one of this client's shots: a live kept copy, or one already
    ///         gone (<see cref="_goneShots"/>). Lower ranks pair first, then shorter distances.
    /// </summary>
    private readonly List<(int Rank, float Distance, EntityUid ServerUid, EntityUid? KeptUid, GoneShot? Gone)> _pairCandidates = new();

    /// <summary>
    ///     How far apart, in metres, a server hit and a hit the kept copy already played may be and still be shown
    ///         once: the same wall, or the grille and window on one tile, rather than a wall only this client has.
    /// </summary>
    private const float SameImpactDistance = 1f;

    /// <summary>
    ///     Kept copies already gone, by a hit or otherwise, kept for as long as their server copy can still be on its
    ///         way: that server copy is matched to them and not to the next shot's live copy, and its impact to theirs.
    ///         Not per-entity state, since the entity they describe no longer exists. Pruned in <see cref="Update"/>.
    /// </summary>
    private readonly List<GoneShot> _goneShots = new();

    private sealed class GoneShot
    {
        public string? Prototype;
        public GameTick SpawnTick;
        public MapCoordinates Coordinates;
        public TimeSpan GoneTime;
        public EntityUid? ServerCopy;
        public NetEntity? ServerCopyNetEntity;
        public List<(EntityUid Target, MapCoordinates Coordinates)> PlayedImpacts = new();
    }

    public override void Initialize()
    {
        base.Initialize();

        // Physics reads whether a body is predicted from the queue UpdateIsPredicted fills, at the start of its own
        //      update, so the kept copies have to be queued before it on every tick.
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var firstTimePredicted = GameTiming.IsFirstTimePredicted;
        if (firstTimePredicted && _unpairedServerCopies.Count > 0)
            PairServerCopies();

        if (firstTimePredicted)
            _goneShots.RemoveAll(gone => GameTiming.CurTime > gone.GoneTime + UnpairedLifetime);

        var keptEnumerator = EntityQueryEnumerator<KsKeptPredictedSpawnComponent, TransformComponent>();
        while (keptEnumerator.MoveNext(out var keptUid, out var keptComponent, out var transformComponent))
        {
            // Before physics, so it takes no further part in anything; out of the way like a throwaway until the
            //      queue deletes it at the end of this tick.
            if (keptComponent.DeletionDeferred)
            {
                PhysicsSystem.SetCanCollide(keptUid, false);
                QueueDel(keptUid);
                continue;
            }

            // Its server copy never came: the server refused the shot, so this is a bullet nobody fired.
            if (firstTimePredicted &&
                keptComponent.ServerCopy == null &&
                GameTiming.CurTime > keptComponent.SpawnTime + UnpairedLifetime)
            {
                PhysicsSystem.SetCanCollide(keptUid, false);
                QueueDel(keptUid);
                continue;
            }

            // Re-queued every tick, because whether a kept copy steps depends on which kind of tick this is. Physics
            //      only adds or removes the predicted marker when the answer changes, which is twice a frame: off for
            //      the re-predicted ticks, on again for the first-time one.
            PhysicsSystem.UpdateIsPredicted(keptUid);

            if (firstTimePredicted)
            {
                RestoreSimulatedTransform((keptUid, keptComponent, transformComponent));
                FollowServerCopy((keptUid, keptComponent, transformComponent));
            }
        }
    }

    /// <summary>
    ///     Matches each newly arrived server copy to this client's copy of the same shot. The two were spawned on the
    ///         same tick, the input's, so a copy of the same prototype and spawn tick is the one; when several share
    ///         it (a shotgun's pellets), the nearest to where the server copy is now. A copy already gone (it hit
    ///         before its server copy arrived) still takes its own server copy. Otherwise that server copy would be
    ///         matched to the next shot's live copy, and delete it when it hit.
    ///
    ///     If no copy shares its tick (the server took the input late), the nearest unmatched live copy of the same
    ///         prototype spawned before it is taken instead.
    /// </summary>
    private void PairServerCopies()
    {
        _pairCandidates.Clear();
        foreach (var serverUid in _unpairedServerCopies)
        {
            if (!TryGetServerCopyMotion(serverUid, out var serverCoordinates, out _) ||
                !TryComp(serverUid, out KsPredictedSpawnComponent? serverComponent))
                continue;

            var serverPrototype = MetaData(serverUid).EntityPrototype;
            var keptEnumerator = EntityQueryEnumerator<KsKeptPredictedSpawnComponent, MetaDataComponent>();
            while (keptEnumerator.MoveNext(out var keptUid, out var keptComponent, out var keptMetaDataComponent))
            {
                if (keptComponent.ServerCopy != null ||
                    keptComponent.ServerCopyNetEntity != null ||
                    keptComponent.DeletionDeferred ||
                    keptMetaDataComponent.EntityPrototype != serverPrototype)
                    continue;

                var keptCoordinates = _transformSystem.GetMapCoordinates(keptUid);
                if (keptCoordinates.MapId != serverCoordinates.MapId)
                    continue;

                var distance = Vector2.Distance(keptCoordinates.Position, serverCoordinates.Position);
                if (keptComponent.SpawnTick == serverComponent.SpawnTick)
                    _pairCandidates.Add((0, distance, serverUid, keptUid, null));
                else if (keptComponent.SpawnTick < serverComponent.SpawnTick && distance <= MaxPairDistance)
                    _pairCandidates.Add((1, distance, serverUid, keptUid, null));
            }

            foreach (var gone in _goneShots)
            {
                if (gone.ServerCopyNetEntity != null ||
                    gone.SpawnTick != serverComponent.SpawnTick ||
                    gone.Prototype != serverPrototype?.ID)
                    continue;

                _pairCandidates.Add((0, DistanceOrMax(gone.Coordinates, serverCoordinates), serverUid, null, gone));
            }
        }

        _pairCandidates.Sort((first, second) => first.Rank != second.Rank
            ? first.Rank.CompareTo(second.Rank)
            : first.Distance.CompareTo(second.Distance));
        foreach (var (_, _, serverUid, keptUid, gone) in _pairCandidates)
        {
            if (!_unpairedServerCopies.Contains(serverUid))
                continue;

            if (keptUid is { } liveUid)
            {
                var keptComponent = _keptQuery.GetComponent(liveUid);
                if (keptComponent.ServerCopy != null || keptComponent.ServerCopyNetEntity != null)
                    continue;

                keptComponent.ServerCopy = serverUid;
                keptComponent.ServerCopyNetEntity = GetNetEntity(serverUid);
            }
            else if (gone is { ServerCopyNetEntity: null })
            {
                gone.ServerCopy = serverUid;
                gone.ServerCopyNetEntity = GetNetEntity(serverUid);
            }
            else
            {
                continue;
            }

            _unpairedServerCopies.Remove(serverUid);
        }
    }

    private static float DistanceOrMax(MapCoordinates first, MapCoordinates second)
    {
        return first.MapId == second.MapId ? Vector2.Distance(first.Position, second.Position) : float.MaxValue;
    }

    /// <summary>
    ///     Pulls a kept copy towards where its server copy is now, and its velocity towards the server copy's, so a
    ///         shot the server fired differently (lag compensation, a reflection) ends up with the real one over a
    ///         few frames, instead of flying on apart from it or jumping to it.
    /// </summary>
    private void FollowServerCopy(Entity<KsKeptPredictedSpawnComponent, TransformComponent> entity)
    {
        var (uid, keptComponent, transformComponent) = entity;
        if (keptComponent.ServerCopy is not { } serverUid ||
            !TryGetServerCopyMotion(serverUid, out var serverCoordinates, out var serverVelocity))
            return;

        var keptCoordinates = _transformSystem.GetMapCoordinates((uid, transformComponent));
        if (keptCoordinates.MapId != serverCoordinates.MapId)
            return;

        var fraction = 1f - MathF.Exp(-(float)GameTiming.TickPeriod.TotalSeconds / CorrectionTime);
        var offset = serverCoordinates.Position - keptCoordinates.Position;
        if (offset.LengthSquared() > 1e-8f)
            _transformSystem.SetWorldPosition((uid, transformComponent), keptCoordinates.Position + offset * fraction);

        if (!_physicsQuery.TryComp(uid, out var physicsComponent))
            return;

        var velocityChange = (serverVelocity - PhysicsSystem.GetMapLinearVelocity(uid, physicsComponent)) * fraction;
        if (velocityChange.LengthSquared() <= 1e-8f)
            return;

        // The change is in map space; the body's velocity is relative to its parent.
        var parentRotation = _transformSystem.GetWorldRotation(transformComponent.ParentUid);
        PhysicsSystem.SetLinearVelocity(uid,
            physicsComponent.LinearVelocity + (-parentRotation).RotateVec(velocityChange),
            body: physicsComponent);
    }

    /// <summary>
    ///     Where a server copy is as of the tick the kept copies were last stepped on. Its state is from
    ///         <see cref="IClientGameTiming.LastRealTick"/>, and it is not predicted here, so it is carried forward at its
    ///         velocity.
    /// </summary>
    private bool TryGetServerCopyMotion(EntityUid serverUid, out MapCoordinates coordinates, out Vector2 velocity)
    {
        coordinates = MapCoordinates.Nullspace;
        velocity = Vector2.Zero;
        if (!TryComp(serverUid, out MetaDataComponent? metaDataComponent) ||
            (metaDataComponent.Flags & MetaDataFlags.Detached) != 0 ||
            !_physicsQuery.TryComp(serverUid, out var physicsComponent))
            return false;

        velocity = PhysicsSystem.GetMapLinearVelocity(serverUid, physicsComponent);
        var ticksAhead = Math.Max(0, (int)GameTiming.CurTick.Value - 1 - (int)_clientGameTiming.LastRealTick.Value);
        var serverCoordinates = _transformSystem.GetMapCoordinates(serverUid);
        coordinates = new MapCoordinates(
            serverCoordinates.Position + velocity * ticksAhead * (float)GameTiming.TickPeriod.TotalSeconds,
            serverCoordinates.MapId);
        return true;
    }

    /// <summary>
    ///     Puts a kept copy back where its last step left it, before it steps again. Between ticks the client's
    ///         transform lerp writes the interpolated, rendered position into the entity's actual position. For a
    ///         networked entity that is harmless, since the next state reset and re-prediction overwrite it; nothing
    ///         resets a client-only entity, so without this each step would start from the rendered position, short
    ///         of the simulated one, and the copy would fall behind by a fraction of every tick.
    /// </summary>
    private void RestoreSimulatedTransform(Entity<KsKeptPredictedSpawnComponent, TransformComponent> entity)
    {
        var (uid, keptComponent, transformComponent) = entity;
        if (!keptComponent.HasSimulatedTransform || transformComponent.ParentUid != keptComponent.SimulatedParent)
            return;

        var position = transformComponent.LocalPosition;
        if (position == keptComponent.SimulatedPosition)
            return;

        // Anywhere off the lerp's segment, something other than the lerp moved it (a teleport, say): keep that.
        if (DistanceToSegment(position, keptComponent.LerpOrigin, keptComponent.SimulatedPosition) > 0.001f)
            return;

        // Without a lerp of its own, so the step's lerp starts from the simulated position, as a reset networked
        //      entity's does.
        _transformSystem.SetLocalPositionNoLerp(uid, keptComponent.SimulatedPosition, transformComponent);
        _transformSystem.SetLocalRotationNoLerp(uid, keptComponent.SimulatedRotation, transformComponent);
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= float.Epsilon)
            return Vector2.Distance(point, start);

        var along = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, start + segment * along);
    }

    /// <summary>
    ///     Records where each first-time step leaves the kept copies, for <see cref="RestoreSimulatedTransform"/>.
    ///         Raised once per substep; the last one wins.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPhysicsUpdateAfterSolve(ref PhysicsUpdateAfterSolveEvent args)
    {
        if (!GameTiming.IsFirstTimePredicted)
            return;

        var keptEnumerator = EntityQueryEnumerator<KsKeptPredictedSpawnComponent, TransformComponent>();
        while (keptEnumerator.MoveNext(out _, out var keptComponent, out var transformComponent))
        {
            keptComponent.HasSimulatedTransform = true;
            keptComponent.SimulatedPosition = transformComponent.LocalPosition;
            keptComponent.SimulatedRotation = transformComponent.LocalRotation;
            keptComponent.SimulatedParent = transformComponent.ParentUid;
            keptComponent.LerpOrigin = transformComponent.ActivelyLerping ? transformComponent.PrevPosition : transformComponent.LocalPosition;
        }
    }

    public override bool IsKeptLocally(EntityUid uid) => _keptQuery.HasComponent(uid);

    public override void PredictedQueueDelDeferrable(EntityUid uid)
    {
        if (GameTiming.ApplyingState && _keptQuery.TryComp(uid, out var keptComponent))
        {
            keptComponent.DeletionDeferred = true;
            return;
        }

        base.PredictedQueueDelDeferrable(uid);
    }

    protected override void KeepForUser(EntityUid uid, EntityUid user)
    {
        // Not this client's own prediction, or not a fresh predicted spawn (an entity the server already networked):
        //      an ordinary predicted spawn, and the server's copy stays visible here.
        if (user != _playerManager.LocalEntity || !HasComp<PredictedSpawnComponent>(uid))
            return;

        // A re-prediction of an input whose spawn this client already kept. The engine would delete this one at the
        //      next state reset anyway; until then it must neither be seen nor hit anything, and it is gone by the
        //      end of this tick.
        if (!GameTiming.IsFirstTimePredicted)
        {
            PhysicsSystem.SetCanCollide(uid, false);
            QueueDel(uid);
            return;
        }

        // The first prediction: this one is kept. Without its PredictedSpawnComponent the state reset leaves it alone.
        RemComp<PredictedSpawnComponent>(uid);
        var keptComponent = EnsureComp<KsKeptPredictedSpawnComponent>(uid);
        keptComponent.SpawnTime = GameTiming.CurTime;
        keptComponent.SpawnTick = GameTiming.CurTick;
        EnsureComp<KsPredictedSpawnComponent>(uid);
        PhysicsSystem.UpdateIsPredicted(uid);
    }

    /// <summary>
    ///     Whether this is the server's copy of a spawn this client keeps a copy of itself.
    /// </summary>
    private bool IsHiddenHere(Entity<KsPredictedSpawnComponent> entity)
    {
        return entity.Comp.PredictingUser is { } predictingUser &&
               predictingUser == _playerManager.LocalEntity &&
               !IsClientSide(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnUpdateIsPredicted(Entity<KsPredictedSpawnComponent> entity, ref UpdateIsPredictedEvent args)
    {
        // Not the server's copy of another user's spawn. Predicted, it would run this client's whole lead ahead of
        //      its last state, so another player's shot would appear that far out from where it was fired (#410);
        //      left alone, it is interpolated like any other networked entity.
        if (entity.Comp.PredictingUser is not { } predictingUser ||
            predictingUser == _playerManager.LocalEntity ||
            IsClientSide(entity.Owner))
            args.IsPredicted = true;

        // A kept copy steps on first-time-predicted ticks only; the hidden server copy never steps here.
        if (_keptQuery.HasComponent(entity.Owner) ? !GameTiming.IsFirstTimePredicted : IsHiddenHere(entity))
            args.BlockPrediction = true;
    }

    // ComponentStartup rather than the state handler for the usual case, the entity arriving: a new entity's
    //      components all have their states applied before any of them starts, so by now nothing else in this state
    //      (its light's, say) can switch back on what this switches off.
    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsPredictedSpawnComponent> entity, ref ComponentStartup args)
    {
        // Predicted physics, whoever's copy this is; or, for the hidden copy, not.
        PhysicsSystem.UpdateIsPredicted(entity.Owner);
        RefreshHidden(entity);

        if (IsHiddenHere(entity))
            _unpairedServerCopies.Add(entity.Owner);
    }

    /// <summary>
    ///     A server copy the server deleted takes its kept copy with it, wherever that has got to: the real shot is
    ///         over, by a hit or by running out.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsPredictedSpawnComponent> entity, ref ComponentShutdown args)
    {
        _unpairedServerCopies.Remove(entity.Owner);
        if (!IsHiddenHere(entity))
            return;

        // Deferred, as this usually runs while a state is applied (see PredictedQueueDelDeferrable).
        var keptEnumerator = EntityQueryEnumerator<KsKeptPredictedSpawnComponent>();
        while (keptEnumerator.MoveNext(out var keptComponent))
        {
            if (keptComponent.ServerCopy == entity.Owner)
                keptComponent.DeletionDeferred = true;
        }
    }

    /// <summary>
    ///     A kept copy that goes records what shot it was, for <see cref="PairServerCopies"/> and
    ///         <see cref="OnImpact"/>.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnKeptShutdown(Entity<KsKeptPredictedSpawnComponent> entity, ref ComponentShutdown args)
    {
        _goneShots.Add(new GoneShot
        {
            Prototype = MetaData(entity.Owner).EntityPrototype?.ID,
            SpawnTick = entity.Comp.SpawnTick,
            Coordinates = _transformSystem.GetMapCoordinates(entity.Owner),
            GoneTime = GameTiming.CurTime,
            ServerCopy = entity.Comp.ServerCopy,
            ServerCopyNetEntity = entity.Comp.ServerCopyNetEntity,
            PlayedImpacts = entity.Comp.PlayedImpacts,
        });
    }

    /// <summary>
    ///     A kept copy plays each of its hits at once, unless the server's copy of the same shot played the shot's
    ///         impact before this one hit anything.
    /// </summary>
    public override bool ClaimImpact(EntityUid projectile, EntityUid target)
    {
        if (!_keptQuery.TryComp(projectile, out var keptComponent))
            return true;

        if (keptComponent.ServerImpactPlayed)
            return false;

        keptComponent.PlayedImpacts.Add((target, _transformSystem.GetMapCoordinates(projectile)));
        return true;
    }

    /// <summary>
    ///     The server's copy of a projectile hit something. Unless it is this player's own, nothing here predicted that
    ///         hit, and this is its impact.
    ///
    ///     For this player's own, the shot is found by its server copy, or by its spawn tick when that copy never
    ///         arrived. If its kept copy already played this hit (the same target, or close by), nothing plays. If the
    ///         kept copy has not hit anything yet, this plays and the kept copy plays none. A hit the kept copy never
    ///         made (the wall behind one only this client has) plays as well.
    /// </summary>
    [SubscribeNetworkEvent]
    private void OnImpact(KsPredictedSpawnImpactEvent args)
    {
        TryGetEntity(args.Target, out var targetUid);
        if (args.ToPredictingUser && !ClaimServerImpact(args, targetUid))
            return;

        if (args.ImpactEffect is { } impactEffect)
            RaiseLocalEvent(new ImpactEffectEvent(impactEffect, args.Coordinates));

        if (targetUid != null && !TerminatingOrDeleted(targetUid.Value))
            _gunSystem.PlayImpactSound(targetUid.Value, args.Damage, args.SoundHit, args.ForceSound);
    }

    /// <summary>
    ///     Whether the server's hit on this player's own shot still has to be shown here. If it is shown before the
    ///         shot's kept copy hit anything, the kept copy is muted.
    /// </summary>
    private bool ClaimServerImpact(KsPredictedSpawnImpactEvent args, EntityUid? targetUid)
    {
        // A server copy that arrived since the last tick is matched first.
        if (_unpairedServerCopies.Count > 0)
            PairServerCopies();

        var impactCoordinates = _transformSystem.ToMapCoordinates(GetCoordinates(args.Coordinates));

        KsKeptPredictedSpawnComponent? keptComponent = null;
        GoneShot? gone = null;
        FindShot(args.Projectile, out keptComponent, out gone);

        // Never reached this client (spawned and spent within a tick): the unmatched shot of the same tick, if any.
        if (keptComponent == null && gone == null)
            FindShotBySpawnTick(args, impactCoordinates, out keptComponent, out gone);

        var playedImpacts = keptComponent?.PlayedImpacts ?? gone?.PlayedImpacts;
        if (playedImpacts == null)
            return true;

        foreach (var (playedTarget, playedCoordinates) in playedImpacts)
        {
            if (playedTarget == targetUid || DistanceOrMax(playedCoordinates, impactCoordinates) <= SameImpactDistance)
                return false;
        }

        if (keptComponent != null && playedImpacts.Count == 0)
            keptComponent.ServerImpactPlayed = true;

        return true;
    }

    private void FindShot(NetEntity serverNetEntity, out KsKeptPredictedSpawnComponent? keptComponent, out GoneShot? gone)
    {
        keptComponent = null;
        gone = null;

        var keptEnumerator = EntityQueryEnumerator<KsKeptPredictedSpawnComponent>();
        while (keptEnumerator.MoveNext(out var candidateComponent))
        {
            if (candidateComponent.ServerCopyNetEntity != serverNetEntity)
                continue;

            keptComponent = candidateComponent;
            return;
        }

        foreach (var candidate in _goneShots)
        {
            if (candidate.ServerCopyNetEntity != serverNetEntity)
                continue;

            gone = candidate;
            return;
        }
    }

    /// <summary>
    ///     The nearest unmatched shot, live or gone, of the prototype and spawn tick in <paramref name="args"/>. It is
    ///         marked as matched, since its server copy is never coming.
    /// </summary>
    private void FindShotBySpawnTick(
        KsPredictedSpawnImpactEvent args,
        MapCoordinates impactCoordinates,
        out KsKeptPredictedSpawnComponent? keptComponent,
        out GoneShot? gone)
    {
        keptComponent = null;
        gone = null;
        var bestDistance = float.PositiveInfinity;

        var keptEnumerator = EntityQueryEnumerator<KsKeptPredictedSpawnComponent, MetaDataComponent>();
        while (keptEnumerator.MoveNext(out var keptUid, out var candidateComponent, out var metaDataComponent))
        {
            if (candidateComponent.ServerCopy != null ||
                candidateComponent.ServerCopyNetEntity != null ||
                candidateComponent.SpawnTick != args.SpawnTick ||
                metaDataComponent.EntityPrototype?.ID != args.Prototype)
                continue;

            var distance = DistanceOrMax(_transformSystem.GetMapCoordinates(keptUid), impactCoordinates);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            keptComponent = candidateComponent;
        }

        foreach (var candidate in _goneShots)
        {
            if (candidate.ServerCopyNetEntity != null ||
                candidate.SpawnTick != args.SpawnTick ||
                candidate.Prototype != args.Prototype)
                continue;

            var distance = DistanceOrMax(candidate.Coordinates, impactCoordinates);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            keptComponent = null;
            gone = candidate;
        }

        // Either way it is now this server copy's shot, so another impact of the same tick and prototype (a shotgun's
        //      next pellet) takes a different one.
        if (gone != null)
            gone.ServerCopyNetEntity = args.Projectile;
        else if (keptComponent != null)
            keptComponent.ServerCopyNetEntity = args.Projectile;
    }

    [SubscribeLocalEvent]
    private void OnAfterAutoHandleState(Entity<KsPredictedSpawnComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        if (entity.Comp.LifeStage < ComponentLifeStage.Running)
            return; // a new entity's; OnStartup handles it

        RefreshHidden(entity);
    }

    /// <summary>
    ///     The hidden copy must not take part in anything here either: its predicted hit, impact effect or fly-by
    ///         would play next to the kept copy's.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPreventCollide(Entity<KsPredictedSpawnComponent> entity, ref PreventCollideEvent args)
    {
        if (IsHiddenHere(entity))
            args.Cancelled = true;
    }

    private void RefreshHidden(Entity<KsPredictedSpawnComponent> entity)
    {
        if (!IsHiddenHere(entity))
            return;

        if (_spriteQuery.TryComp(entity.Owner, out var spriteComponent))
            _spriteSystem.SetVisible((entity.Owner, spriteComponent), false);

        // Done outside prediction (state application), so the reset does not restore it; it stays off until the
        //      server next changes the light, which a projectile's does not.
        _pointLightSystem.SetEnabled(entity.Owner, false);

        PhysicsSystem.UpdateIsPredicted(entity.Owner);
    }
}
