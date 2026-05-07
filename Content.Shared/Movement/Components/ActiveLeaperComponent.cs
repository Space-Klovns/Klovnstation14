using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

/// <summary>
/// Marker component given to the users of the <see cref="JumpAbilityComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ActiveLeaperComponent : Component
{
    /// <summary>
    /// The duration to stun the owner on collide with environment.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? KnockdownDuration = null; // KS14: Made optional

    // KS14
    [DataField]
    public bool HitAnything = false;

    // KS14 addition
    /// <summary>
    /// If specified, this is how long to stun the owner for if they collided with the environment.
    ///     Otherwise, they will be knocked down with this duration if they hit something.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? GuaranteedKnockdownDuration = null;

    // KS14 addition
    /// <summary>
    /// If specified, the enemy hit will be knocked down for this many seconds.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? HitKnockdownDuration = null;

    // KS14 addition
    /// <summary>
    /// If specified, this much stamina damage will be dealt to any hit targets.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float StaminaDamage = 0f;
}
