using System.Numerics;
using Content.Shared._KS14.PredictedSpawning;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.Shared._KS14.Particles;

/// <summary>
///     Spawns and drives <see cref="KsParticlesComponent"/>s. Shaped like <c>SharedAudioSystem</c>: the particles
///         live on the client, so the three ways of making an emitter differ only in who makes the entity.
///     <list type="bullet">
///         <item><see cref="SpawnLocal(ProtoId{KsParticlesPrototype}, EntityCoordinates)"/>: a client-only entity,
///             for this client alone. Does nothing on the server.</item>
///         <item><see cref="SpawnPredicted"/>: from a user's own predicted input. Their client keeps a copy of its
///             own; the server's copy is shown to everyone else (see <see cref="KsSharedPredictedSpawnSystem"/>).</item>
///         <item><see cref="SpawnPvs"/>: a server entity, for everyone in range. Does nothing on the client.</item>
///         <item><see cref="SpawnPredictedEffect"/>: from shared code both sides run, whoever caused it (a hit's
///             spray). The client that predicted it shows its own copy at once and hides the server's.</item>
///     </list>
///     An existing entity can carry the component itself instead, in its prototype or by <c>EnsureComp</c> on the
///         server.
/// </summary>
public abstract partial class KsSharedParticlesSystem : EntitySystem
{
    [Dependency] protected KsSharedPredictedSpawnSystem PredictedSpawnSystem = default!;
    [Dependency] protected SharedTransformSystem TransformSystem = default!;
    [Dependency] private EntityQuery<KsParticlesComponent> _particlesQuery = default!;
    [Dependency] private INetManager _netManager = default!;

    /// <summary>
    ///     The bare emitter entity the spawn methods make.
    /// </summary>
    public static readonly EntProtoId ParticlesEntity = "KsParticles";

    #region Spawning

    /// <summary>
    ///     Spawns an emitter this client alone sees, attached to <paramref name="coordinates"/>. Null on the server,
    ///         or while re-predicting, unless <paramref name="once"/>.
    /// </summary>
    /// <param name="once">
    ///     The caller makes sure this runs once, so it spawns even on a re-predicted tick: a kept projectile's hit
    ///         (<see cref="KsSharedPredictedSpawnSystem.IsFirstTimePredicted(EntityUid)"/>), which can land on one.
    /// </param>
    public abstract EntityUid? SpawnLocal(ProtoId<KsParticlesPrototype> prototype, EntityCoordinates coordinates, bool once = false);

    /// <inheritdoc cref="SpawnLocal(ProtoId{KsParticlesPrototype}, EntityCoordinates)"/>
    public EntityUid? SpawnLocal(ProtoId<KsParticlesPrototype> prototype, EntityUid parentUid)
        => SpawnLocal(prototype, new EntityCoordinates(parentUid, Vector2.Zero));

    /// <summary>
    ///     Spawns an emitter everyone in range sees. Null on the client.
    /// </summary>
    public abstract EntityUid? SpawnPvs(ProtoId<KsParticlesPrototype> prototype, EntityCoordinates coordinates);

    /// <inheritdoc cref="SpawnPvs(ProtoId{KsParticlesPrototype}, EntityCoordinates)"/>
    public EntityUid? SpawnPvs(ProtoId<KsParticlesPrototype> prototype, EntityUid parentUid)
        => SpawnPvs(prototype, new EntityCoordinates(parentUid, Vector2.Zero));

    /// <summary>
    ///     Spawns an emitter from shared code running for <paramref name="user"/>'s own input, on both sides. The
    ///         user's client keeps the copy it spawned; the server's copy is hidden there and shown to everyone else.
    ///         With no user, only the server spawns, so the effect arrives a round trip later and nobody sees it
    ///         twice.
    /// </summary>
    /// <remarks>
    ///     Call this only on the user's own predicted path, as <see cref="KsSharedPredictedSpawnSystem"/> requires:
    ///         a server-only path with a user hides the server's copy from a client that never made one.
    /// </remarks>
    public EntityUid? SpawnPredicted(ProtoId<KsParticlesPrototype> prototype, EntityCoordinates coordinates, EntityUid? user)
    {
        if (user == null)
            return SpawnPvs(prototype, coordinates);

        var uid = PredictedSpawnSystem.PredictedSpawnAttachedTo(ParticlesEntity, coordinates, user: user);
        SetPrototype(uid, prototype);
        return uid;
    }

    /// <summary>
    ///     Spawns a short effect from shared code that both sides run, such as the reaction to a predicted hit,
    ///         without knowing whose input caused it. The server spawns it for everyone in range. A client predicting
    ///         that code spawns its own copy at once, only once however often the tick is re-predicted, and hides the
    ///         server's copy when it arrives (the same prototype, close by, soon after). A client that did not predict
    ///         it shows the server's, a round trip later, so the effect is never lost.
    /// </summary>
    /// <param name="source">
    ///     What the effect is about (the mob that was hit): with the prototype and the tick, it tells a re-prediction
    ///         of this spawn from a second spawn.
    /// </param>
    /// <param name="once">
    ///     The caller makes sure this runs once, so another of the same prototype, source and tick is a second spawn and
    ///         not a re-prediction: a kept projectile's hit
    ///         (<see cref="KsSharedPredictedSpawnSystem.IsFirstTimePredicted(EntityUid)"/>). Every pellet of a shotgun's
    ///         shot hits on one tick, and the server sprays for each: taken for one spawn here, all but one of the
    ///         server's were left showing, and the hit sprayed again when they arrived.
    /// </param>
    /// <returns>The new emitter, or null on a client re-predicting a spawn it already made.</returns>
    public abstract EntityUid? SpawnPredictedEffect(ProtoId<KsParticlesPrototype> prototype, EntityCoordinates coordinates, EntityUid source, bool once = false);

    #endregion

    #region One-shot

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<KsParticlesComponent> entity, ref MapInitEvent args)
    {
        ApplyOneShot(entity);
    }

    /// <summary>
    ///     Gives the entity of a <see cref="KsParticlesPrototype.OneShot"/> emitter a <see cref="TimedDespawnComponent"/>
    ///         that deletes it once its particles are gone. Only on the side that owns the entity: the server for
    ///         networked ones, which the client couldn't delete anyway, and the client for its own.
    /// </summary>
    private void ApplyOneShot(Entity<KsParticlesComponent> entity)
    {
        if (_netManager.IsClient && !IsClientSide(entity.Owner))
            return;

        if (entity.Comp.Prototype is not { } prototypeId ||
            !ProtoMan.TryIndex(prototypeId, out var prototype) ||
            prototype.OneShotLifetime is not { } lifetime)
            return;

        EnsureComp<TimedDespawnComponent>(entity.Owner).Lifetime = (float)lifetime.TotalSeconds;
    }

    #endregion

    #region Setters

    public void SetPrototype(Entity<KsParticlesComponent?> entity, ProtoId<KsParticlesPrototype>? prototype)
    {
        if (!_particlesQuery.Resolve(entity.Owner, ref entity.Comp) || entity.Comp.Prototype == prototype)
            return;

        entity.Comp.Prototype = prototype;
        Dirty(entity);
        ApplyOneShot((entity.Owner, entity.Comp));
        AfterPrototypeChanged((entity.Owner, entity.Comp));
    }

    /// <summary>
    ///     Runs after <see cref="SetPrototype"/> changes the prototype, on the side that changed it. The client
    ///         rebuilds the emitter here: a client-side entity gets no network state to tell it.
    /// </summary>
    protected virtual void AfterPrototypeChanged(Entity<KsParticlesComponent> entity)
    {
    }

    public void SetEmitting(Entity<KsParticlesComponent?> entity, bool emitting)
    {
        if (!_particlesQuery.Resolve(entity.Owner, ref entity.Comp) || entity.Comp.Emitting == emitting)
            return;

        entity.Comp.Emitting = emitting;
        Dirty(entity);
    }

    public void SetColor(Entity<KsParticlesComponent?> entity, Color color)
    {
        if (!_particlesQuery.Resolve(entity.Owner, ref entity.Comp) || entity.Comp.Color == color)
            return;

        entity.Comp.Color = color;
        Dirty(entity);
    }

    public void SetOffset(Entity<KsParticlesComponent?> entity, Vector2 offset)
    {
        if (!_particlesQuery.Resolve(entity.Owner, ref entity.Comp) || entity.Comp.Offset == offset)
            return;

        entity.Comp.Offset = offset;
        Dirty(entity);
    }

    public void SetRotatingOffset(Entity<KsParticlesComponent?> entity, Vector2 rotatingOffset)
    {
        if (!_particlesQuery.Resolve(entity.Owner, ref entity.Comp) || entity.Comp.RotatingOffset == rotatingOffset)
            return;

        entity.Comp.RotatingOffset = rotatingOffset;
        Dirty(entity);
    }

    public void SetRotation(Entity<KsParticlesComponent?> entity, Angle rotation)
    {
        if (!_particlesQuery.Resolve(entity.Owner, ref entity.Comp) || entity.Comp.Rotation.Equals(rotation))
            return;

        entity.Comp.Rotation = rotation;
        Dirty(entity);
    }

    public void SetAmount(Entity<KsParticlesComponent?> entity, float amount)
    {
        if (!_particlesQuery.Resolve(entity.Owner, ref entity.Comp) || entity.Comp.Amount.Equals(amount))
            return;

        entity.Comp.Amount = amount;
        Dirty(entity);
    }

    public void SetSpeed(Entity<KsParticlesComponent?> entity, float speed)
    {
        if (!_particlesQuery.Resolve(entity.Owner, ref entity.Comp) || entity.Comp.Speed.Equals(speed))
            return;

        entity.Comp.Speed = speed;
        Dirty(entity);
    }

    public void SetScale(Entity<KsParticlesComponent?> entity, float scale)
    {
        if (!_particlesQuery.Resolve(entity.Owner, ref entity.Comp) || entity.Comp.Scale.Equals(scale))
            return;

        entity.Comp.Scale = scale;
        Dirty(entity);
    }

    #endregion
}
