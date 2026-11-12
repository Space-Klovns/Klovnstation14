using System.Numerics;
using Content.Shared._KS14.Particles;
using Robust.Shared.Map;

namespace Content.Client._KS14.Particles;

/// <summary>
///     The client's simulation of a <see cref="KsParticlesComponent"/>: the live particles and the emitter's clock.
///         Added by <see cref="KsParticlesSystem"/> alongside the shared component, on any entity that has one, and
///         never networked or saved.
/// </summary>
[RegisterComponent]
[Access(typeof(KsParticlesSystem))]
public sealed partial class KsParticleEmitterComponent : Component
{
    /// <summary>
    ///     The resolved effect, or null when the shared component names none or an unknown one.
    /// </summary>
    [ViewVariables]
    public KsParticlesPrototype? Prototype;

    [ViewVariables]
    public KsParticleSpriteSet? Sprites;

    /// <summary>
    ///     Live particles occupy <c>[0, Alive)</c>. Grows by doubling up to the prototype's count.
    /// </summary>
    public KsParticle[] Particles = Array.Empty<KsParticle>();

    [ViewVariables]
    public int Alive;

    /// <summary>
    ///     Seconds of simulation time not yet stepped, below one step. Also how far between steps to draw.
    /// </summary>
    [ViewVariables]
    public float TimeAccumulator;

    /// <summary>
    ///     Seconds per simulation step, from the prototype's tick rate.
    /// </summary>
    [ViewVariables]
    public float TickLength = 0.05f;

    /// <summary>
    ///     Fractional particles owed by <see cref="KsParticlesPrototype.Spawning"/>.
    /// </summary>
    [ViewVariables]
    public float SpawnAccumulator;

    /// <summary>
    ///     Seconds since the emitter started, for <see cref="KsParticlesPrototype.EmitDuration"/>.
    /// </summary>
    [ViewVariables]
    public float Age;

    [ViewVariables]
    public bool BurstDone;

    /// <summary>
    ///     Whether any particle can have a z, so drawing has to sort.
    /// </summary>
    [ViewVariables]
    public bool UsesZ;

    public System.Random Random = new();

    #region Per frame, for the overlays

    [ViewVariables]
    public MapId MapId = MapId.Nullspace;

    /// <summary>
    ///     The emitter's position in the map, offset included.
    /// </summary>
    [ViewVariables]
    public Vector2 Origin;

    /// <summary>
    ///     The emitter's rotation in the map, or zero when the prototype does not follow rotation.
    /// </summary>
    [ViewVariables]
    public Angle Rotation;

    /// <summary>
    ///     Emitter frame to map frame, for a <see cref="KsParticleSpace.Local"/> emitter. Identity for a
    ///         <see cref="KsParticleSpace.World"/> one.
    /// </summary>
    [ViewVariables]
    public Matrix3x2 Matrix = Matrix3x2.Identity;

    /// <summary>
    ///     Everything the emitter might draw, in the map frame, for culling.
    /// </summary>
    [ViewVariables]
    public Box2 WorldBounds;

    #endregion
}
