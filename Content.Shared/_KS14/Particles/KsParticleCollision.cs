namespace Content.Shared._KS14.Particles;

/// <summary>
///     What a particle does when it meets a wall.
/// </summary>
public enum KsParticleHitResponse : byte
{
    /// <summary>Reflect off the surface, losing speed by <see cref="KsParticleCollision.Bounce"/> and <see cref="KsParticleCollision.Friction"/>.</summary>
    Bounce,

    /// <summary>Stop on the surface and stay there for the rest of its life: gravity and drift no longer move it.</summary>
    Stick,

    /// <summary>Die on the spot.</summary>
    Die,
}

/// <summary>
///     Opt-in, approximate, client-side collision of an emitter's particles with what blocks them in the world: the
///         static, hard, tall fixtures anchored to grids (walls, windows, closed airlocks). A particle is a point of
///         <see cref="Radius"/> moving along a segment each step; nothing about it is networked or simulated by the
///         physics engine. See <c>Klovn/Docs/particles.md</c>.
/// </summary>
[DataDefinition]
public sealed partial class KsParticleCollision
{
    /// <summary>
    ///     Off turns collision back off for a child of a prototype that has it.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>
    ///     What a particle does on hitting a wall.
    /// </summary>
    [DataField]
    public KsParticleHitResponse OnHit = KsParticleHitResponse.Bounce;

    /// <summary>
    ///     Restitution, 0 to 1: the fraction of the speed into the surface kept, outwards, after a bounce. 0 slides
    ///         along it.
    /// </summary>
    [DataField]
    public float Bounce = 0.5f;

    /// <summary>
    ///     The fraction of the speed along the surface lost on each contact, 0 to 1.
    /// </summary>
    [DataField]
    public float Friction = 0.2f;

    /// <summary>
    ///     How far from a wall a particle's centre stops, in metres, scaled with the emitter. Roughly half the
    ///         sprite's visible size.
    /// </summary>
    [DataField]
    public float Radius = 0.05f;
}
