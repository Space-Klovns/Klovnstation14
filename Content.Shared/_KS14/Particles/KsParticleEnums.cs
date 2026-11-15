namespace Content.Shared._KS14.Particles;

/// <summary>
///     Where an emitter's particles are drawn relative to sprites. Each is one overlay on the client, so an emitter
///         is drawn at exactly one of these.
/// </summary>
public enum KsParticleLayer : byte
{
    /// <summary>Above floor tiles and puddles, below everything standing on the floor.</summary>
    Floor,

    /// <summary>Above objects and items, below mobs.</summary>
    BelowMobs,

    /// <summary>Above mobs, doors and gas, with other effects. The default, and where BYOND's particles sit.</summary>
    Effects,

    /// <summary>Above every sprite, ghosts included.</summary>
    Top,
}

/// <summary>
///     The frame an emitter's particles live in.
/// </summary>
public enum KsParticleSpace : byte
{
    /// <summary>Particles move with the emitter, as BYOND's do: smoke on a moving thing moves with it.</summary>
    Local,

    /// <summary>Particles stay where they were emitted: smoke trails behind a moving thing.</summary>
    World,
}

/// <summary>
///     How <see cref="KsParticlesPrototype.Drift"/> moves a particle.
/// </summary>
public enum KsParticleDriftMode : byte
{
    /// <summary>
    ///     Added to the step's motion only, its velocity left alone: a fresh jolt every step, which jitters. Suits
    ///         sparks and embers.
    /// </summary>
    Offset,

    /// <summary>
    ///     Added to the velocity every BYOND tick (0.1 s), as BYOND does (OpenDream applies drift as an
    ///         acceleration, against friction): the jolts build up and are damped, so the particle wanders smoothly.
    ///         The values port from DM unchanged.
    /// </summary>
    Accelerate,
}
