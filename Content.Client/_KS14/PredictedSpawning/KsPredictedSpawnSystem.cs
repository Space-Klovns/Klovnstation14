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

    private readonly List<(float Distance, EntityUid ServerUid, EntityUid KeptUid)> _pairCandidates = new();

    /// <summary>
    ///     How long an impact played by one copy of a shot stops the other copy playing one on the same target: long
    ///         enough for a round trip, short enough that a second shot at the same target still plays.
    /// </summary>
    private static readonly TimeSpan RecentImpactWindow = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Impacts on this player's own shots' targets played lately, by the kept copy or by the server's copy, each
    ///         waiting to be matched with the other copy's hit on the same target. A shotgun's pellets each leave one.
    ///         Pruned in <see cref="Update"/>.
    /// </summary>
    private readonly List<(EntityUid Target, TimeSpan Time, bool FromServer)> _recentImpacts = new();

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
            _recentImpacts.RemoveAll(impact => GameTiming.CurTime > impact.Time + RecentImpactWindow);

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
    ///     Matches each newly arrived server copy to the kept copy of the same spawn: the nearest one of the same
    ///         prototype to where the server copy is now, closest pairs first. Nothing networked names a kept copy,
    ///         which exists only here, so position is all there is to go on; the two start at the same place and
    ///         speed, a lag-compensated shot aside.
    /// </summary>
    private void PairServerCopies()
    {
        _pairCandidates.Clear();
        foreach (var serverUid in _unpairedServerCopies)
        {
            if (!TryGetServerCopyMotion(serverUid, out var serverCoordinates, out _))
                continue;

            var serverPrototype = MetaData(serverUid).EntityPrototype;
            var keptEnumerator = EntityQueryEnumerator<KsKeptPredictedSpawnComponent, MetaDataComponent>();
            while (keptEnumerator.MoveNext(out var keptUid, out var keptComponent, out var keptMetaDataComponent))
            {
                if (keptComponent.ServerCopy != null ||
                    keptComponent.DeletionDeferred ||
                    keptMetaDataComponent.EntityPrototype != serverPrototype)
                    continue;

                var keptCoordinates = _transformSystem.GetMapCoordinates(keptUid);
                if (keptCoordinates.MapId != serverCoordinates.MapId)
                    continue;

                var distance = Vector2.Distance(keptCoordinates.Position, serverCoordinates.Position);
                if (distance <= MaxPairDistance)
                    _pairCandidates.Add((distance, serverUid, keptUid));
            }
        }

        _pairCandidates.Sort((first, second) => first.Distance.CompareTo(second.Distance));
        foreach (var (_, serverUid, keptUid) in _pairCandidates)
        {
            var keptComponent = _keptQuery.GetComponent(keptUid);
            if (keptComponent.ServerCopy != null || !_unpairedServerCopies.Remove(serverUid))
                continue;

            keptComponent.ServerCopy = serverUid;
        }
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
        EnsureComp<KsKeptPredictedSpawnComponent>(uid).SpawnTime = GameTiming.CurTime;
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

    public override bool ClaimImpact(EntityUid projectile, EntityUid target)
    {
        if (!IsKeptLocally(projectile))
            return true;

        // The server's copy got there first and already played this hit.
        if (TryTakeRecentImpact(target, fromServer: true))
            return false;

        _recentImpacts.Add((target, GameTiming.CurTime, FromServer: false));
        return true;
    }

    /// <summary>
    ///     Removes an impact on <paramref name="target"/> played within <see cref="RecentImpactWindow"/> by the given
    ///         side, if there is one.
    /// </summary>
    private bool TryTakeRecentImpact(EntityUid target, bool fromServer)
    {
        for (var i = 0; i < _recentImpacts.Count; i++)
        {
            var (impactTarget, time, impactFromServer) = _recentImpacts[i];
            if (impactTarget != target || impactFromServer != fromServer || GameTiming.CurTime > time + RecentImpactWindow)
                continue;

            _recentImpacts.RemoveAt(i);
            return true;
        }

        return false;
    }

    /// <summary>
    ///     The server's copy of this player's own projectile hit something. The kept copy usually played that hit
    ///         already, on the same target, and then this plays nothing; when the kept copy missed it, or hit something
    ///         else, the real hit's impact plays here as well.
    /// </summary>
    [SubscribeNetworkEvent]
    private void OnImpact(KsPredictedSpawnImpactEvent args)
    {
        if (TryGetEntity(args.Target, out var targetUid))
        {
            if (TryTakeRecentImpact(targetUid.Value, fromServer: false))
                return;

            _recentImpacts.Add((targetUid.Value, GameTiming.CurTime, FromServer: true));
        }

        if (args.ImpactEffect is { } impactEffect)
            RaiseLocalEvent(new ImpactEffectEvent(impactEffect, args.Coordinates));

        if (targetUid != null && !TerminatingOrDeleted(targetUid.Value))
            _gunSystem.PlayImpactSound(targetUid.Value, args.Damage, args.SoundHit, args.ForceSound);
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
