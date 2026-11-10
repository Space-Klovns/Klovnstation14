using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ZLevel.Physics;

/// <summary>
///     Added to an entity that is moving vertically between z-levels, and removed as soon as it
///         comes to rest on the floor of a z-level.
///     The absence of this component therefore means "at rest on the floor plane of its own z-level".
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsZLevelPhysicsSystem))]
public sealed partial class KsZLevelTransitComponent : Component
{
    /// <summary>
    ///     Normalised height within the z-level this entity is currently on: 0 is this z-level's floor
    ///         plane, 1 is the floor plane of the z-level above it.
    ///     One unit of this spans <see cref="KsZLevelComponent.Depth"/> z-levels of real distance, and it is
    ///         renormalised back into 0..1 every time the entity changes z-level, so consumers can read it as
    ///         "height above the floor I am over" without knowing anything about the stack.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Height;

    /// <summary>
    ///     Vertical speed, in z-levels per second. Negative descends, positive ascends.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float VerticalVelocity;
}
