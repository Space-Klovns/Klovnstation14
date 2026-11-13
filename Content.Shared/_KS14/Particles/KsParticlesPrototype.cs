using System.Numerics;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Particles;

/// <summary>
///     One sprite a particle may be given at spawn, with how likely it is against the others. BYOND's weighted
///         <c>icon_state</c> list.
/// </summary>
[DataDefinition]
public sealed partial class KsParticleSprite
{
    [DataField(required: true)]
    public SpriteSpecifier Sprite = SpriteSpecifier.Invalid;

    [DataField]
    public float Weight = 1f;
}

/// <summary>
///     A particle effect: what an emitter spawns, how each particle moves and looks over its life, and where it is
///         drawn. A functional superset of BYOND's <c>/particles</c>, in SS14 units: metres, seconds, degrees.
///         Attach one to an entity with <see cref="KsParticlesComponent"/>; the client does the rest.
/// </summary>
/// <remarks>
///     Every per-particle field is a <see cref="KsParticleGenerator"/>: a constant or a random rule, sampled when
///         the particle spawns, except <see cref="Drift"/>, which is sampled every simulation step. See
///         <c>Klovn/Docs/particles.md</c> for the YAML reference and the conversion table from BYOND values.
/// </remarks>
[Prototype]
public sealed partial class KsParticlesPrototype : IPrototype, IInheritingPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<KsParticlesPrototype>))]
    public string[]? Parents { get; private set; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; private set; }

    #region Emitter

    /// <summary>
    ///     The most particles alive at once. Spawning pauses at this many.
    /// </summary>
    [DataField]
    public int Count = 100;

    /// <summary>
    ///     Particles spawned per second. Fractions accumulate, so 0.5 is one every two seconds.
    /// </summary>
    [DataField]
    public float Spawning = 10f;

    /// <summary>
    ///     Particles spawned all at once when the emitter starts, before <see cref="Spawning"/> takes over.
    /// </summary>
    [DataField]
    public int Burst;

    /// <summary>
    ///     How long the emitter spawns for, in seconds, from when it starts. Null spawns forever. Particles already
    ///         alive when it ends live out their lifespans.
    /// </summary>
    [DataField]
    public float? EmitDuration;

    /// <summary>
    ///     The region about the emitter that its particles are expected to stay inside, in metres, used to decide
    ///         whether it is on screen. BYOND's <c>width</c> and <c>height</c>, except that particles outside it are
    ///         still drawn rather than clipped. The actual extent of the live particles is tracked as well, so this
    ///         only has to be roughly right.
    /// </summary>
    [DataField]
    public Vector2 Size = new(3f, 3f);

    /// <summary>
    ///     Particles outside this box, in metres about the emitter, die. BYOND's <c>bound1</c> and <c>bound2</c>.
    ///         Null never kills by position.
    /// </summary>
    [DataField]
    public Box2? Bounds;

    /// <summary>
    ///     Acceleration applied to every particle, in metres per second squared. Positive y is up.
    /// </summary>
    [DataField]
    public Vector2 Gravity;

    /// <summary>
    ///     With one set, <see cref="Color"/> is an index into it and <see cref="ColorChange"/> the index's change
    ///         per second.
    /// </summary>
    [DataField]
    public KsParticleGradient? Gradient;

    /// <summary>
    ///     A transform applied to the whole emitter, after its position: a shear, a squash, an offset. In metres.
    /// </summary>
    [DataField]
    public Matrix3x2 Transform = Matrix3x2.Identity;

    /// <summary>
    ///     Simulation steps per second. Particles are drawn interpolated between steps, so this only has to be high
    ///         enough for the motion to be right: 20 matches BYOND's rate for most values.
    /// </summary>
    [DataField]
    public float TickRate = 20f;

    /// <summary>
    ///     Whether particles move with the emitter or stay where they were emitted.
    /// </summary>
    [DataField]
    public KsParticleSpace Space = KsParticleSpace.Local;

    /// <summary>
    ///     Whether the emitter turns with its entity. Off, as in BYOND, it is square to the grid the entity stands on
    ///         (the frame sprites are drawn in and the eye is turned to), so smoke rises up the screen whichever way
    ///         the thing it comes from is facing. Off a grid, square to the map.
    /// </summary>
    [DataField]
    public bool FollowRotation;

    /// <summary>
    ///     Where the particles are drawn relative to sprites.
    /// </summary>
    [DataField]
    public KsParticleLayer Layer = KsParticleLayer.Effects;

    /// <summary>
    ///     Whether the particles ignore lighting, as sparks and embers should.
    /// </summary>
    [DataField]
    public bool Unshaded;

    /// <summary>
    ///     Whether an animated sprite plays, from the particle's birth, rather than showing its first frame.
    /// </summary>
    [DataField]
    public bool Animated = true;

    /// <summary>
    ///     Whether particles are drawn between simulation steps by interpolation. Off, they move in steps of
    ///         <see cref="TickRate"/>, as BYOND's do.
    /// </summary>
    [DataField]
    public bool Interpolate = true;

    /// <summary>
    ///     Whether the emitter's origin is nudged onto a whole screen pixel, as the engine does for grids, so
    ///         particles sit still against the floor while the eye moves.
    /// </summary>
    [DataField]
    public bool PixelSnap;

    #endregion

    #region Per particle

    /// <summary>
    ///     The sprites a particle may be given, picked by weight at spawn. Empty draws a single white pixel, which
    ///         <see cref="Color"/> tints and <see cref="Scale"/> sizes, as BYOND does with no <c>icon</c>.
    /// </summary>
    [DataField]
    public List<KsParticleSprite> Sprites = new();

    /// <summary>
    ///     How long a particle lives, in seconds. Zero or less lives until killed by <see cref="Bounds"/> or by
    ///         shrinking to nothing.
    /// </summary>
    [DataField]
    public KsParticleGenerator Lifespan = KsParticleGenerator.One;

    /// <summary>
    ///     Seconds at the end of the lifespan over which the particle fades out.
    /// </summary>
    [DataField]
    public KsParticleGenerator Fade = KsParticleGenerator.Zero;

    /// <summary>
    ///     Seconds at the start of the lifespan over which the particle fades in.
    /// </summary>
    [DataField]
    public KsParticleGenerator FadeIn = KsParticleGenerator.Zero;

    /// <summary>
    ///     The particle's colour, multiplied into its sprite. With a <see cref="Gradient"/>, an index into it instead.
    /// </summary>
    [DataField]
    public KsParticleGenerator Color = KsParticleGenerator.White;

    /// <summary>
    ///     With a <see cref="Gradient"/>, how far the colour index moves per second.
    /// </summary>
    [DataField]
    public KsParticleGenerator ColorChange = KsParticleGenerator.Zero;

    /// <summary>
    ///     Where the particle spawns, in metres from the emitter. Z only orders particles against each other.
    /// </summary>
    [DataField]
    public KsParticleGenerator Position = KsParticleGenerator.Zero;

    /// <summary>
    ///     The particle's starting velocity, in metres per second.
    /// </summary>
    [DataField]
    public KsParticleGenerator Velocity = KsParticleGenerator.Zero;

    /// <summary>
    ///     The particle's starting scale, per axis. A scalar scales both.
    /// </summary>
    [DataField]
    public KsParticleGenerator Scale = KsParticleGenerator.One;

    /// <summary>
    ///     Scale change per second. A particle that shrinks to zero on either axis dies.
    /// </summary>
    [DataField]
    public KsParticleGenerator Grow = KsParticleGenerator.Zero;

    /// <summary>
    ///     The particle's starting rotation, in degrees, clockwise positive as in BYOND.
    /// </summary>
    [DataField]
    public KsParticleGenerator Rotation = KsParticleGenerator.Zero;

    /// <summary>
    ///     Rotation change in degrees per second, clockwise positive.
    /// </summary>
    [DataField]
    public KsParticleGenerator Spin = KsParticleGenerator.Zero;

    /// <summary>
    ///     The fraction of its velocity a particle loses each second, 0 to 1.
    /// </summary>
    [DataField]
    public KsParticleGenerator Friction = KsParticleGenerator.Zero;

    /// <summary>
    ///     An extra velocity, in metres per second, sampled afresh every simulation step: a random walk that leaves
    ///         <see cref="Velocity"/> alone.
    /// </summary>
    [DataField]
    public KsParticleGenerator Drift = KsParticleGenerator.Zero;

    #endregion
}
