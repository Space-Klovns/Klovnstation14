using System.Numerics;
using Robust.Shared.Physics;

namespace Content.Server._KS14.Physics;

/// <summary>
///     Holds a physics body at a fixed linear and/or angular velocity, with no damping or tile friction slowing it
///         down, and optionally stops it from colliding with anything. Works on ordinary entities and grids alike.
///     Every field may be changed at runtime through VV; <see cref="KsConstantVelocitySystem"/> picks the change up
///         on the next physics tick.
/// </summary>
/// <remarks>
///     Velocities are in the body's own physics frame, i.e. relative to its broadphase: the map for a grid or an
///         entity floating in space, the grid for an entity standing on one. Static bodies cannot be given velocity,
///         so they are left alone.
/// </remarks>
[RegisterComponent, Access(typeof(KsConstantVelocitySystem))]
public sealed partial class KsConstantVelocityComponent : Component
{
    /// <summary>
    ///     Whether <see cref="LinearVelocity"/> is enforced. When false, the body keeps whatever linear velocity it
    ///         has, but still without damping.
    /// </summary>
    [DataField]
    public bool EnforceLinearVelocity = true;

    /// <summary>
    ///     Linear velocity the body is held at, in metres per second.
    /// </summary>
    [DataField]
    public Vector2 LinearVelocity = Vector2.Zero;

    /// <summary>
    ///     Whether <see cref="AngularVelocity"/> is enforced. When false, the body keeps whatever angular velocity it
    ///         has, but still without damping.
    /// </summary>
    [DataField]
    public bool EnforceAngularVelocity = true;

    /// <summary>
    ///     Angular velocity the body is held at, in radians per second.
    /// </summary>
    [DataField]
    public float AngularVelocity;

    /// <summary>
    ///     Makes the body completely immovable while leaving it free to rotate. The body is switched to
    ///         <see cref="BodyType.Kinematic"/>, which the solver treats as having infinite mass, so nothing can push
    ///         it; linear velocity is held at zero regardless of <see cref="EnforceLinearVelocity"/>. Rotation still
    ///         pivots about the body's centre of mass.
    /// </summary>
    /// <remarks>
    ///     Kinematic bodies do not collide with static or other kinematic bodies, so a locked body spins straight
    ///         through walls and anchored structures. Dynamic bodies and mobs still collide with it.
    /// </remarks>
    [DataField]
    public bool LockPosition;

    /// <summary>
    ///     The body type to restore when <see cref="LockPosition"/> is turned off or this component is removed. Null
    ///         while nothing is locked.
    /// </summary>
    [ViewVariables]
    public BodyType? OriginalBodyType;

    /// <summary>
    ///     Whether the body may collide with anything at all.
    /// </summary>
    [DataField]
    public bool CanCollide = true;

    /// <summary>
    ///     The value of <see cref="CanCollide"/> that the body's contacts were last built against. Differs from it
    ///         only for the tick after something changed it, which is how a VV edit gets noticed.
    /// </summary>
    [ViewVariables]
    public bool AppliedCanCollide = true;

    /// <summary>
    ///     Linear damping the body had before this component was added, restored when it is removed.
    /// </summary>
    [ViewVariables]
    public float OriginalLinearDamping;

    /// <summary>
    ///     Angular damping the body had before this component was added, restored when it is removed.
    /// </summary>
    [ViewVariables]
    public float OriginalAngularDamping;
}
