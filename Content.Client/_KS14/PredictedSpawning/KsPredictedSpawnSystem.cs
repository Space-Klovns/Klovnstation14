using System.Numerics;
using Content.Shared._KS14.PredictedSpawning;
using Robust.Client.GameObjects;
using Robust.Client.Physics;
using Robust.Client.Player;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Client._KS14.PredictedSpawning;

/// <inheritdoc/>
public sealed partial class KsPredictedSpawnSystem : KsSharedPredictedSpawnSystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SharedPointLightSystem _pointLightSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<KsKeptPredictedSpawnComponent> _keptQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

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

            // Re-queued every tick, because whether a kept copy steps depends on which kind of tick this is. Physics
            //      only adds or removes the predicted marker when the answer changes, which is twice a frame: off for
            //      the re-predicted ticks, on again for the first-time one.
            PhysicsSystem.UpdateIsPredicted(keptUid);

            if (firstTimePredicted)
                RestoreSimulatedTransform((keptUid, keptComponent, transformComponent));
        }
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
        EnsureComp<KsKeptPredictedSpawnComponent>(uid);
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
