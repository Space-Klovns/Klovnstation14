using System.Numerics;

namespace Content.Client._KS14.Particles;

/// <summary>
///     One live particle. A struct in <see cref="KsParticleEmitterComponent.Particles"/>: the simulation only ever
///         touches these in a tight loop, and never allocates one.
/// </summary>
/// <remarks>
///     Positions are metres in the emitter's frame (<see cref="Content.Shared._KS14.Particles.KsParticleSpace.Local"/>)
///         or in the map's (<see cref="Content.Shared._KS14.Particles.KsParticleSpace.World"/>). Z orders particles
///         against each other and nothing else. The <c>Prev*</c> fields are the previous step's values, for drawing
///         between steps.
/// </remarks>
public struct KsParticle
{
    public Vector3 Position;
    public Vector3 PrevPosition;
    public Vector3 Velocity;

    /// <summary>Seconds alive.</summary>
    public float Age;

    /// <summary>Seconds to live; zero or less is forever.</summary>
    public float Lifespan;
    public float FadeIn;
    public float Fade;

    public Vector2 Scale;
    public Vector2 PrevScale;
    public Vector2 Grow;

    /// <summary>Radians, counter-clockwise positive, as the engine's <see cref="Angle"/>.</summary>
    public float Rotation;
    public float PrevRotation;
    public float Spin;

    public Color Color;
    public float GradientIndex;
    public float ColorChange;

    /// <summary>Fraction of velocity lost per second.</summary>
    public float Friction;

    /// <summary>Index into the emitter's <see cref="KsParticleSpriteSet"/>, or -1 for a white pixel.</summary>
    public int Sprite;
}
