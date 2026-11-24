using System.Numerics;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Particles;
using Content.Shared._KS14.PredictedSpawning;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Client._KS14.Particles;

/// <summary>
///     An emitter the overlays draw this frame: gathered once per frame by <see cref="KsParticlesSystem.FrameUpdate"/>
///         into the list for its layer, so each overlay walks only its own.
/// </summary>
public readonly record struct KsParticleDrawEntry(EntityUid Uid, KsParticlesComponent Particles, KsParticleEmitterComponent Emitter);

/// <summary>
///     Simulates every <see cref="KsParticlesComponent"/> this client knows of, in fixed steps from
///         <see cref="FrameUpdate"/>, and hands the live emitters to the <see cref="KsParticlesOverlay"/>s by layer.
///         The server never sees a particle.
/// </summary>
/// <remarks>
///     Rendering is unverified headless. What a test can check is the state this leaves behind: the particles in
///         each <see cref="KsParticleEmitterComponent"/> and the per-layer lists in <see cref="Layers"/>.
/// </remarks>
public sealed partial class KsParticlesSystem : KsSharedParticlesSystem
{
    /// <summary>
    ///     A BYOND tick, seconds: what DM's per-tick particle values are per.
    /// </summary>
    private const float ByondTick = 0.1f;

    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private EntityQuery<KsParticleEmitterComponent> _emitterQuery = default!;
    [Dependency] private EntityQuery<KsPredictedSpawnComponent> _predictedSpawnQuery = default!;

    /// <summary>
    ///     The most simulation steps one emitter runs in one frame. A hitch drops time rather than catching up on it.
    /// </summary>
    public const int MaxStepsPerFrame = 5;

    private const int InitialCapacity = 16;
    private const float DegreesToRadians = MathF.PI / 180f;

    /// <summary>
    ///     The emitters with something to draw this frame, by <see cref="KsParticleLayer"/>. Rebuilt every
    ///         <see cref="FrameUpdate"/>; read by the overlays until the next.
    /// </summary>
    public readonly List<KsParticleDrawEntry>[] Layers = MakeLayers();

    /// <summary>
    ///     Particles alive across every emitter after the last frame.
    /// </summary>
    [ViewVariables]
    public int TotalAlive { get; private set; }

    private readonly Dictionary<string, KsParticleSpriteSet> _spriteSets = new();

    /// <summary>
    ///     How long after a predicted effect its server copy may still arrive and be matched to it: a round trip,
    ///         with room to spare.
    /// </summary>
    public static readonly TimeSpan PredictedEffectMatchWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     How far from a predicted effect its server copy may be and still be matched to it: the predicted mob and
    ///         the server's need not stand on the same spot.
    /// </summary>
    public const float PredictedEffectMatchDistance = 1.5f;

    /// <summary>
    ///     A predicted effect this client spawned (<see cref="SpawnPredictedEffect"/>), kept until its server copy
    ///         arrives or <see cref="PredictedEffectMatchWindow"/> passes. A transient work list, not per-entity
    ///         state: it outlives the local emitter, which may be gone before the server's copy arrives.
    /// </summary>
    private record struct PredictedEffect(
        ProtoId<KsParticlesPrototype> Prototype,
        EntityUid Source,
        GameTick Tick,
        MapCoordinates Coordinates,
        TimeSpan Time,
        bool Matched);

    private readonly List<PredictedEffect> _predictedEffects = new();

    private bool _enabled;
    private float _density;
    private int _maxAlive;

    /// <summary>
    ///     Particles the global budget still allows this frame.
    /// </summary>
    private int _aliveBudget;

    private static List<KsParticleDrawEntry>[] MakeLayers()
    {
        var layers = new List<KsParticleDrawEntry>[Enum.GetValues<KsParticleLayer>().Length];
        for (var i = 0; i < layers.Length; i++)
            layers[i] = new List<KsParticleDrawEntry>();
        return layers;
    }

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.ParticlesEnabled, value => _enabled = value, true);
        Subs.CVar(_configurationManager, KsCCVars.ParticlesDensity, value => _density = Math.Clamp(value, 0f, 1f), true);
        Subs.CVar(_configurationManager, KsCCVars.ParticlesMaxAlive, value =>
        {
            _maxAlive = Math.Max(0, value);
            // Spent down by spawning and dealt out afresh every frame; this covers steps run before the first frame.
            _aliveBudget = _maxAlive;
        }, true);
    }

    #region Spawning

    public override EntityUid? SpawnLocal(ProtoId<KsParticlesPrototype> prototype, EntityCoordinates coordinates, bool once = false)
    {
        // Re-predicting an input that already spawned this; the first prediction's copy is still here.
        if (!once && !_gameTiming.IsFirstTimePredicted)
            return null;

        var uid = SpawnAttachedTo(ParticlesEntity, coordinates);
        SetPrototype(uid, prototype);
        return uid;
    }

    public override EntityUid? SpawnPvs(ProtoId<KsParticlesPrototype> prototype, EntityCoordinates coordinates) => null;

    public override EntityUid? SpawnPredictedEffect(ProtoId<KsParticlesPrototype> prototype, EntityCoordinates coordinates, EntityUid source, bool once = false)
    {
        // Not gated on IsFirstTimePredicted: a kept projectile's hit can land on a re-predicted tick, and happens
        //      once. A re-prediction of the same spawn is the same prototype, source and tick instead.
        // Nor on ApplyingState: a kept projectile's hit can land during the state reset, which raises its
        //      collisions, and that is prediction too.
        var tick = _gameTiming.CurTick;
        if (!once)
        {
            foreach (var predictedEffect in _predictedEffects)
            {
                if (predictedEffect.Prototype == prototype && predictedEffect.Source == source && predictedEffect.Tick == tick)
                    return null;
            }
        }

        var uid = SpawnAttachedTo(ParticlesEntity, coordinates);
        SetPrototype(uid, prototype);
        _predictedEffects.Add(new PredictedEffect(
            prototype,
            source,
            tick,
            TransformSystem.ToMapCoordinates(coordinates),
            _gameTiming.RealTime,
            Matched: false));
        return uid;
    }

    /// <summary>
    ///     Hides <paramref name="entity"/> here if it is the server's copy of an effect this client predicted: the
    ///         oldest unmatched one of the same prototype, close enough, within the window.
    /// </summary>
    private void TryMatchPredictedEffect(Entity<KsParticlesComponent> entity, KsParticleEmitterComponent emitterComponent)
    {
        if (_predictedEffects.Count == 0 ||
            IsClientSide(entity.Owner) ||
            emitterComponent.SupersededByPrediction ||
            entity.Comp.Prototype is not { } prototype)
            return;

        var coordinates = TransformSystem.GetMapCoordinates(entity.Owner);
        for (var i = 0; i < _predictedEffects.Count; i++)
        {
            var predictedEffect = _predictedEffects[i];
            if (predictedEffect.Matched ||
                predictedEffect.Prototype != prototype ||
                predictedEffect.Coordinates.MapId != coordinates.MapId ||
                (predictedEffect.Coordinates.Position - coordinates.Position).Length() > PredictedEffectMatchDistance)
                continue;

            _predictedEffects[i] = predictedEffect with { Matched = true };
            emitterComponent.SupersededByPrediction = true;
            return;
        }
    }

    #endregion

    #region Lifecycle

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsParticlesComponent> entity, ref ComponentStartup args)
    {
        Reconfigure(entity, EnsureComp<KsParticleEmitterComponent>(entity.Owner));
    }

    [SubscribeLocalEvent]
    private void OnHandleState(Entity<KsParticlesComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        if (_emitterQuery.TryComp(entity.Owner, out var emitterComponent))
            Reconfigure(entity, emitterComponent);
    }

    protected override void AfterPrototypeChanged(Entity<KsParticlesComponent> entity)
    {
        if (_emitterQuery.TryComp(entity.Owner, out var emitterComponent))
            Reconfigure(entity, emitterComponent);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsParticlesComponent> entity, ref ComponentShutdown args)
    {
        RemCompDeferred<KsParticleEmitterComponent>(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<KsParticlesPrototype>())
            return;

        _spriteSets.Clear();

        var query = AllEntityQuery<KsParticlesComponent, KsParticleEmitterComponent>();
        while (query.MoveNext(out var uid, out var particlesComponent, out var emitterComponent))
        {
            // Force a fresh resolve: the instance behind the id changed.
            emitterComponent.Prototype = null;
            Reconfigure((uid, particlesComponent), emitterComponent);
        }
    }

    /// <summary>
    ///     Resolves the shared component's prototype into the emitter. A changed prototype restarts the emitter; the
    ///         other shared fields are read live, so a change to them needs nothing here.
    /// </summary>
    private void Reconfigure(Entity<KsParticlesComponent> entity, KsParticleEmitterComponent emitterComponent)
    {
        KsParticlesPrototype? prototype = null;
        if (entity.Comp.Prototype is { } prototypeId && !ProtoMan.TryIndex(prototypeId, out prototype))
            Log.Error($"{ToPrettyString(entity.Owner)} names unknown particle prototype '{prototypeId}'.");

        if (ReferenceEquals(prototype, emitterComponent.Prototype))
            return;

        emitterComponent.Prototype = prototype;
        emitterComponent.Alive = 0;
        emitterComponent.Age = 0f;
        emitterComponent.BurstDone = false;
        emitterComponent.SpawnAccumulator = 0f;
        emitterComponent.TimeAccumulator = 0f;
        emitterComponent.Random = new System.Random(entity.Owner.GetHashCode());

        if (prototype == null)
        {
            emitterComponent.Sprites = null;
            emitterComponent.Collision = null;
            return;
        }

        emitterComponent.Sprites = GetSpriteSet(prototype);
        TryMatchPredictedEffect(entity, emitterComponent);
        emitterComponent.TickLength = prototype.TickLength;
        emitterComponent.UsesZ = prototype.Position.CanProduceZ || prototype.Velocity.CanProduceZ || prototype.Drift.CanProduceZ;

        // Only an emitter that collides keeps walls; every other tests nothing.
        if (prototype.Collision is { Enabled: true })
        {
            emitterComponent.Collision ??= new KsParticleCollisionCache();
            emitterComponent.Collision.Invalidate();
        }
        else
        {
            emitterComponent.Collision = null;
        }

        var initial = Math.Clamp(Math.Max(prototype.Burst, InitialCapacity), 1, Math.Max(1, prototype.Count));
        if (emitterComponent.Particles.Length < initial)
            emitterComponent.Particles = new KsParticle[initial];
    }

    private KsParticleSpriteSet GetSpriteSet(KsParticlesPrototype prototype)
    {
        if (!_spriteSets.TryGetValue(prototype.ID, out var spriteSet))
        {
            spriteSet = new KsParticleSpriteSet(prototype, _spriteSystem, Log);
            _spriteSets[prototype.ID] = spriteSet;
        }

        return spriteSet;
    }

    /// <summary>
    ///     Whether this is the server's copy of something this client already shows its own copy of, so its
    ///         particles would double the local ones: a spawn this client's player made and keeps a copy of
    ///         (<see cref="KsSharedPredictedSpawnSystem"/>), or a predicted effect (<see cref="SpawnPredictedEffect"/>).
    /// </summary>
    public bool IsHiddenHere(EntityUid uid)
    {
        if (_emitterQuery.TryComp(uid, out var emitterComponent) && emitterComponent.SupersededByPrediction)
            return true;

        return _predictedSpawnQuery.TryComp(uid, out var predictedSpawnComponent) &&
               predictedSpawnComponent.PredictingUser is { } predictingUser &&
               predictingUser == _playerManager.LocalEntity &&
               !IsClientSide(uid);
    }

    #endregion

    #region Simulation

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _gameTiming.RealTime;
        _predictedEffects.RemoveAll(predictedEffect => now - predictedEffect.Time > PredictedEffectMatchWindow);

        foreach (var layer in Layers)
            layer.Clear();

        _aliveBudget = _maxAlive - TotalAlive;
        var totalAlive = 0;

        if (!_enabled)
        {
            TotalAlive = 0;
            return;
        }

        // Paused entities (mapping, a paused map) are skipped: their particles hold still.
        var query = EntityQueryEnumerator<KsParticlesComponent, KsParticleEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var particlesComponent, out var emitterComponent, out var transformComponent))
        {
            if (emitterComponent.Prototype is not { } prototype || IsHiddenHere(uid))
                continue;

            UpdateFrame(particlesComponent, emitterComponent, prototype, transformComponent);

            emitterComponent.TimeAccumulator += frameTime;
            var steps = Math.Min(MaxStepsPerFrame, (int)(emitterComponent.TimeAccumulator / emitterComponent.TickLength));
            for (var i = 0; i < steps; i++)
                Step(particlesComponent, emitterComponent, prototype, emitterComponent.TickLength);

            emitterComponent.TimeAccumulator = Math.Min(emitterComponent.TimeAccumulator - steps * emitterComponent.TickLength, emitterComponent.TickLength);

            UpdateBounds(particlesComponent, emitterComponent, prototype);

            totalAlive += emitterComponent.Alive;
            if (emitterComponent.Alive > 0)
                Layers[(int)prototype.Layer].Add(new KsParticleDrawEntry(uid, particlesComponent, emitterComponent));
        }

        TotalAlive = totalAlive;
    }

    /// <summary>
    ///     Where the emitter is this frame, for spawning into the world and for drawing.
    /// </summary>
    private void UpdateFrame(KsParticlesComponent particlesComponent, KsParticleEmitterComponent emitterComponent, KsParticlesPrototype prototype, TransformComponent transformComponent)
    {
        var (worldPosition, worldRotation) = TransformSystem.GetWorldPositionRotation(transformComponent);

        // Not following the entity means square to the grid it stands on, as sprites are drawn and as the eye is
        //      turned, not square to the map: a station grid is usually turned, and "up" is the grid's up (#453).
        var rotation = prototype.FollowRotation
            ? worldRotation
            : transformComponent.GridUid is { } gridUid ? TransformSystem.GetWorldRotation(gridUid) : Angle.Zero;
        rotation += particlesComponent.Rotation;

        emitterComponent.MapId = transformComponent.MapID;
        emitterComponent.Rotation = rotation;
        emitterComponent.Origin = worldPosition + rotation.RotateVec(particlesComponent.Offset) +
            worldRotation.RotateVec(particlesComponent.RotatingOffset);
        emitterComponent.Matrix = prototype.Space == KsParticleSpace.Local
            ? prototype.Transform * Matrix3Helpers.CreateTransform(emitterComponent.Origin, rotation, new Vector2(particlesComponent.Scale))
            : Matrix3x2.Identity;
    }

    /// <summary>
    ///     One fixed step of <paramref name="dt"/> seconds: spawn what is owed, then move, age and kill.
    /// </summary>
    public void Step(KsParticlesComponent particlesComponent, KsParticleEmitterComponent emitterComponent, KsParticlesPrototype prototype, float dt)
    {
        var maxCount = Math.Max(0, (int)MathF.Ceiling(prototype.Count * _density * particlesComponent.Amount));

        if (!emitterComponent.BurstDone)
        {
            emitterComponent.BurstDone = true;
            var burst = (int)MathF.Ceiling(prototype.Burst * _density * particlesComponent.Amount);
            for (var i = 0; i < burst && TrySpawn(particlesComponent, emitterComponent, prototype, maxCount); i++)
            {
            }
        }

        var emitting = particlesComponent.Emitting && (prototype.EmitDuration is not { } emitDuration || emitterComponent.Age < (float)emitDuration.TotalSeconds);
        emitterComponent.Age += dt;

        if (emitting)
        {
            emitterComponent.SpawnAccumulator += prototype.Spawning * _density * particlesComponent.Amount * dt;
            var owed = (int)emitterComponent.SpawnAccumulator;
            emitterComponent.SpawnAccumulator -= owed;
            for (var i = 0; i < owed && TrySpawn(particlesComponent, emitterComponent, prototype, maxCount); i++)
            {
            }
        }
        else
        {
            emitterComponent.SpawnAccumulator = 0f;
        }

        var world = prototype.Space == KsParticleSpace.World;
        var gravity = new Vector3(ToEmitter(prototype.Gravity, particlesComponent, emitterComponent, world), 0f);
        var hasDrift = !prototype.Drift.IsAlwaysZero;
        var gradient = prototype.Gradient;
        var random = emitterComponent.Random;
        var particles = emitterComponent.Particles;

        for (var i = 0; i < emitterComponent.Alive; i++)
        {
            ref var particle = ref particles[i];

            particle.PrevPosition = particle.Position;
            particle.PrevScale = particle.Scale;
            particle.PrevRotation = particle.Rotation;

            particle.Age += dt;
            if (particle.Lifespan > 0f && particle.Age >= particle.Lifespan)
            {
                Kill(emitterComponent, i--);
                continue;
            }

            // Stuck to a wall by collision: it stays where it hit.
            if (!particle.Stuck)
            {
                particle.Velocity += gravity * dt;
                if (particle.Friction > 0f)
                    particle.Velocity *= MathF.Pow(1f - particle.Friction, dt);

                var motion = particle.Velocity;
                if (hasDrift)
                {
                    var drift = prototype.Drift.SampleVector3(random);
                    if (world)
                        drift = ToEmitter(drift, particlesComponent, emitterComponent);

                    // BYOND adds drift to the velocity once a tick (0.1 s); spread over this step, that is drift / 0.1 s.
                    if (prototype.DriftMode == KsParticleDriftMode.Accelerate)
                    {
                        particle.Velocity += drift * (dt / ByondTick);
                        motion = particle.Velocity;
                    }
                    else
                    {
                        motion += drift;
                    }
                }

                particle.Position += motion * dt;
            }
            particle.Rotation += particle.Spin * dt;
            particle.Scale += particle.Grow * dt;

            if (particle.Scale.X <= 0f || particle.Scale.Y <= 0f)
            {
                Kill(emitterComponent, i--);
                continue;
            }

            if (prototype.Bounds is { } bounds)
            {
                var relative = new Vector2(particle.Position.X, particle.Position.Y);
                if (world)
                    relative -= emitterComponent.Origin;

                if (!bounds.Contains(relative))
                {
                    Kill(emitterComponent, i--);
                    continue;
                }
            }

            if (gradient != null)
            {
                particle.GradientIndex += particle.ColorChange * dt;
                particle.Color = gradient.Evaluate(particle.GradientIndex);
            }
        }

        if (emitterComponent.Collision is { } collisionCache && prototype.Collision is { } collision)
            Collide(particlesComponent, emitterComponent, prototype, collision, collisionCache, dt);
    }

    private bool TrySpawn(KsParticlesComponent particlesComponent, KsParticleEmitterComponent emitterComponent, KsParticlesPrototype prototype, int maxCount)
    {
        if (emitterComponent.Alive >= maxCount || _aliveBudget <= 0)
            return false;

        if (emitterComponent.Alive >= emitterComponent.Particles.Length)
        {
            var grown = new KsParticle[Math.Min(maxCount, Math.Max(InitialCapacity, emitterComponent.Particles.Length * 2))];
            Array.Copy(emitterComponent.Particles, grown, emitterComponent.Alive);
            emitterComponent.Particles = grown;
        }

        var world = prototype.Space == KsParticleSpace.World;
        var random = emitterComponent.Random;
        ref var particle = ref emitterComponent.Particles[emitterComponent.Alive];

        var position = prototype.Position.SampleVector3(random);
        var velocity = prototype.Velocity.SampleVector3(random) * particlesComponent.Speed;
        var scale = prototype.Scale.SampleVector2(random);
        var grow = prototype.Grow.SampleVector2(random);
        if (world)
        {
            position = ToEmitter(position, particlesComponent, emitterComponent) + new Vector3(emitterComponent.Origin, 0f);
            velocity = ToEmitter(velocity, particlesComponent, emitterComponent);
            scale *= particlesComponent.Scale;
            grow *= particlesComponent.Scale;
        }

        particle.Position = position;
        particle.PrevPosition = position;
        particle.Velocity = velocity;
        particle.Age = 0f;
        particle.Lifespan = prototype.Lifespan.SampleFloat(random);
        particle.FadeIn = prototype.FadeIn.SampleFloat(random);
        particle.Fade = prototype.Fade.SampleFloat(random);
        particle.Scale = scale;
        particle.PrevScale = scale;
        particle.Grow = grow;
        // BYOND turns clockwise for positive degrees; the engine's Angle turns counter-clockwise.
        particle.Rotation = -prototype.Rotation.SampleFloat(random) * DegreesToRadians;
        // A world particle's quad is drawn in map axes, so its own rotation carries the emitter's frame. A local one
        //      is drawn under the emitter matrix, which carries it already.
        if (world)
            particle.Rotation += (float)emitterComponent.Rotation.Theta;
        particle.PrevRotation = particle.Rotation;
        particle.Spin = -prototype.Spin.SampleFloat(random) * DegreesToRadians;
        particle.Friction = Math.Clamp(prototype.Friction.SampleFloat(random), 0f, 1f);
        particle.Sprite = emitterComponent.Sprites?.Pick(random) ?? -1;
        particle.Stuck = false;

        if (prototype.Gradient is { } gradient)
        {
            particle.GradientIndex = prototype.Color.SampleFloat(random);
            particle.ColorChange = prototype.ColorChange.SampleFloat(random);
            particle.Color = gradient.Evaluate(particle.GradientIndex);
        }
        else
        {
            particle.GradientIndex = 0f;
            particle.ColorChange = 0f;
            particle.Color = prototype.Color.SampleColor(random);
        }

        emitterComponent.Alive++;
        _aliveBudget--;
        return true;
    }

    private static void Kill(KsParticleEmitterComponent emitterComponent, int index)
    {
        emitterComponent.Alive--;
        if (index != emitterComponent.Alive)
            emitterComponent.Particles[index] = emitterComponent.Particles[emitterComponent.Alive];
    }

    /// <summary>
    ///     A prototype vector into a <see cref="KsParticleSpace.World"/> emitter's frame: scaled by the component and,
    ///         if the prototype follows rotation, turned with the entity. A local emitter's matrix does this at draw.
    /// </summary>
    private static Vector3 ToEmitter(Vector3 vector, KsParticlesComponent particlesComponent, KsParticleEmitterComponent emitterComponent)
    {
        var planar = emitterComponent.Rotation.RotateVec(new Vector2(vector.X, vector.Y)) * particlesComponent.Scale;
        return new Vector3(planar, vector.Z * particlesComponent.Scale);
    }

    private static Vector2 ToEmitter(Vector2 vector, KsParticlesComponent particlesComponent, KsParticleEmitterComponent emitterComponent, bool world)
    {
        return world ? emitterComponent.Rotation.RotateVec(vector) * particlesComponent.Scale : vector;
    }

    /// <summary>
    ///     The box every live particle, and the prototype's declared size, fits in, in the map frame.
    /// </summary>
    private static void UpdateBounds(KsParticlesComponent particlesComponent, KsParticleEmitterComponent emitterComponent, KsParticlesPrototype prototype)
    {
        var sizeBox = Box2.CenteredAround(emitterComponent.Origin, prototype.Size * particlesComponent.Scale);
        if (emitterComponent.Alive == 0)
        {
            emitterComponent.WorldBounds = sizeBox;
            return;
        }

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        var maxScale = 0f;
        var particles = emitterComponent.Particles;
        for (var i = 0; i < emitterComponent.Alive; i++)
        {
            ref var particle = ref particles[i];
            var position = new Vector2(particle.Position.X, particle.Position.Y);
            min = Vector2.Min(min, position);
            max = Vector2.Max(max, position);
            maxScale = MathF.Max(maxScale, MathF.Max(particle.Scale.X, particle.Scale.Y));
        }

        var extent = maxScale * (emitterComponent.Sprites?.MaxExtent ?? 0.5f / EyeManager.PixelsPerMeter);
        var particleBox = new Box2(min, max);
        if (prototype.Space == KsParticleSpace.Local)
        {
            particleBox = emitterComponent.Matrix.TransformBox(particleBox);
            extent *= particlesComponent.Scale;
        }

        emitterComponent.WorldBounds = Box2.Union(sizeBox, particleBox.Enlarged(extent));
    }

    #endregion
}
