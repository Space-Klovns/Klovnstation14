using Content.Shared._Trauma.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.PredictedSpawning;

/// <summary>
///     Predicted spawns of short-lived, purely cosmetic entities that move: sparks, and the projectiles of a
///         player's own shots. Two things set these apart from the engine's <c>PredictedSpawn*</c>:
///     <list type="bullet">
///         <item>
///             The spawned entity's physics is predicted, so a predicted spark or bullet actually flies on the
///                 client instead of hanging where it spawned.
///         </item>
///         <item>
///             <b>User mode</b>, when a <c>user</c> is given: the client of that user keeps <i>its own</i> copy for
///                 the copy's whole life, and the server's copy is hidden and inert on that client only. Everyone
///                 else sees the server's copy. This is the opposite of the engine's predicted spawn, where the
///                 client's copy is a stand-in deleted on every state reset and the server's replaces it - which
///                 for a fast projectile means a visible jump, since the server's copy is somewhere else (a
///                 lag-compensated shot, most of all).
///         </item>
///     </list>
///
///     User mode has one hard requirement: <b>call it only from code that runs on that user's own predicted
///         path</b>, the code their client runs for their own input, such as a shot request. Anywhere else, the
///         server hides its copy from a client that never made one, and the user sees nothing at all. A spawn
///         that is only server-side, or that some other entity's action causes, passes no user.
///
///     Anything long-lived, or that gameplay reads (a fire, a foam blanket, an effect that has to agree with the
///         server), uses the engine's <c>PredictedSpawn*</c> instead: the kept copy never hears from the server
///         again, so it cannot follow anything the server does to the real one.
/// </summary>
/// <remarks>
///     How a kept copy works on the client: it starts as an ordinary engine predicted spawn, made in the first
///         prediction of the user's input; <see cref="KeepForUser"/> removes its <see cref="PredictedSpawnComponent"/>
///         so the state reset no longer deletes it. Each later re-prediction of that same input spawns a
///         throwaway again, which is made non-collidable and deleted at the end of its tick. The kept copy's
///         physics steps only on first-time-predicted ticks: the client re-predicts several ticks per frame, and
///         nothing rolls a client-only entity back between them, so stepping it on all of them would fly it that
///         many times too fast. And before each step it is put back where the last one left it, because the
///         client's transform lerp writes the rendered, interpolated position over the real one between ticks.
///
///     Whatever happens to a kept copy happens once, but not necessarily on a first-time-predicted tick: its
///         contacts are found by the next physics pass after its step, which is usually a re-predicted tick, so
///         its hit lands there. Gate its one-off effects on <see cref="IsFirstTimePredicted(EntityUid)"/>.
/// </remarks>
public abstract partial class KsSharedPredictedSpawnSystem : EntitySystem
{
    [Dependency] protected IGameTiming GameTiming = default!;
    [Dependency] protected SharedPhysicsSystem PhysicsSystem = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    /// <summary>
    ///     The entity whose predicted path is running, while a <see cref="BeginUserPrediction"/> scope is open.
    /// </summary>
    private EntityUid? _scopeUser;

    /// <summary>
    ///     Entities created while the scope is open. Only those can be a fresh spawn of the user's prediction; an
    ///         entity that already existed (an arrow taken from a quiver, say) has no client-side copy to keep.
    /// </summary>
    private readonly HashSet<EntityUid> _scopeSpawns = new();

    public override void Initialize()
    {
        base.Initialize();

        EntityManager.EntityAdded += OnEntityAdded;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        EntityManager.EntityAdded -= OnEntityAdded;
    }

    private void OnEntityAdded(Entity<MetaDataComponent> entity)
    {
        if (_scopeUser != null)
            _scopeSpawns.Add(entity.Owner);
    }

    /// <summary>
    ///     Opens a scope in which every projectile <see cref="Content.Shared.Weapons.Ranged.Systems.SharedGunSystem"/>
    ///         shoots, if it was spawned inside the scope, is a user-mode spawn of <paramref name="user"/>.
    ///         Open it around the handling of the user's own predicted request, and nowhere else (see the class docs).
    /// </summary>
    /// <param name="user">The entity the requesting session is attached to; null opens no scope.</param>
    public UserPredictionScope BeginUserPrediction(EntityUid? user)
    {
        var scope = new UserPredictionScope(this, _scopeUser);
        if (user != null)
            _scopeUser = user;

        return scope;
    }

    private void EndUserPrediction(EntityUid? previousUser)
    {
        _scopeUser = previousUser;
        if (_scopeUser == null)
            _scopeSpawns.Clear();
    }

    [SubscribeLocalEvent]
    private void OnPlayerShotProjectile(ref PlayerShotProjectileEvent args)
    {
        if (_scopeUser is not { } user || !_scopeSpawns.Contains(args.Projectile))
            return;

        FlagPredicted(args.Projectile, user);
    }

    /// <summary>
    ///     <see cref="IGameTiming.IsFirstTimePredicted"/>, except that it is always true for a client's kept copy:
    ///         nothing about a kept copy is ever re-predicted, so whatever happens to it happens once, and whenever
    ///         it happens is its first time. Gate the one-off effects of something that can be a kept copy (its
    ///         impact effect, a camera kick) on this rather than on the timing.
    /// </summary>
    public bool IsFirstTimePredicted(EntityUid uid)
    {
        return GameTiming.IsFirstTimePredicted || IsKeptLocally(uid);
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> is this client's kept copy of a user-mode spawn. Never true on the server.
    /// </summary>
    public virtual bool IsKeptLocally(EntityUid uid) => false;

    /// <summary>
    ///     <c>PredictedQueueDel</c>, except that a client's kept copy deleted while a state is being applied goes at
    ///         the start of the next tick instead. Use it for anything that can be a kept copy and deletes itself
    ///         from a collision handler, as a projectile's hit does.
    /// </summary>
    /// <remarks>
    ///     The state reset raises collision events too (<c>PhysicsSystem.ResetContacts</c>), from a list of
    ///         contacts it gathered first. Queueing a client-side entity for deletion destroys its contacts at once,
    ///         and a kept copy is client-side, so its other contacts further down that list would go back to the
    ///         pool, cleared, before the reset reached them: <c>KeyNotFoundException: Entity 0 does not have a
    ///         component of type TransformComponent</c> in <c>UpdateIsTouching</c>.
    /// </remarks>
    public virtual void PredictedQueueDelDeferrable(EntityUid uid) => PredictedQueueDel(uid);

    /// <summary>
    ///     Spawns <paramref name="entityProtoId"/> as a predicted spawn whose physics is predicted too.
    /// </summary>
    /// <param name="user">
    ///     Makes this a user-mode spawn of that entity (see the class docs). Only from that user's own predicted path.
    /// </param>
    public EntityUid PredictedSpawn(string entityProtoId, ComponentRegistry? componentOverrides = null, bool doMapInit = false, EntityUid? user = null)
        => FlagPredicted(base.PredictedSpawn(entityProtoId, overrides: componentOverrides, doMapInit: doMapInit), user);

    /// <inheritdoc cref="PredictedSpawn(string, ComponentRegistry?, bool, EntityUid?)"/>
    public EntityUid PredictedSpawn(string entityProtoId, MapCoordinates coordinates, ComponentRegistry? componentOverrides = null, Angle rotation = default, EntityUid? user = null)
        => FlagPredicted(base.PredictedSpawn(entityProtoId, coordinates, overrides: componentOverrides, rotation: rotation), user);

    /// <inheritdoc cref="PredictedSpawn(string, ComponentRegistry?, bool, EntityUid?)"/>
    public EntityUid PredictedSpawnAttachedTo(string entityProtoId, EntityCoordinates coordinates, ComponentRegistry? componentOverrides = null, Angle rotation = default, EntityUid? user = null)
        => FlagPredicted(base.PredictedSpawnAttachedTo(entityProtoId, coordinates, overrides: componentOverrides, rotation: rotation), user);

    /// <summary>
    ///     Flags a just-spawned entity as predicted, predicting its physics, and makes it a user-mode spawn when
    ///         <paramref name="user"/> is given.
    /// </summary>
    /// <returns>The same <paramref name="uid"/>, so the spawn methods can be one-liners.</returns>
    private EntityUid FlagPredicted(EntityUid uid, EntityUid? user)
    {
        EnsureComp<KsPredictedSpawnComponent>(uid);
        if (_physicsQuery.HasComponent(uid))
            PhysicsSystem.UpdateIsPredicted(uid);

        if (user != null)
            KeepForUser(uid, user.Value);

        return uid;
    }

    /// <summary>
    ///     Makes a fresh predicted spawn a user-mode one: the server's copy is marked with its user, and that user's
    ///         client keeps its own.
    /// </summary>
    protected abstract void KeepForUser(EntityUid uid, EntityUid user);

    /// <summary>
    ///     Marks the server's copy, so the user's client hides it.
    /// </summary>
    protected void SetPredictingUser(Entity<KsPredictedSpawnComponent> entity, EntityUid user)
    {
        entity.Comp.PredictingUser = user;
        Dirty(entity);
    }

    /// <summary>
    ///     Restores the previous scope when disposed. See <see cref="BeginUserPrediction"/>.
    /// </summary>
    public readonly struct UserPredictionScope : IDisposable
    {
        private readonly KsSharedPredictedSpawnSystem _system;
        private readonly EntityUid? _previousUser;

        internal UserPredictionScope(KsSharedPredictedSpawnSystem system, EntityUid? previousUser)
        {
            _system = system;
            _previousUser = previousUser;
        }

        public void Dispose()
        {
            _system.EndUserPrediction(_previousUser);
        }
    }
}
