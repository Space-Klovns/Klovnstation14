using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ZLevel.Elevators;

/// <summary>
///     An entity with an activatable UI used to control Z-level elevators.
///         Requires ActivatableUIComponent.
///
///     Does not necessarily need to be on the same grid as the elevator,
///         but will automatically link if it is.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
[Access(typeof(SharedZLevelElevatorSystem))]
public sealed partial class ZLevelElevatorControllerComponent : Component
{
    /// <summary>
    ///     Which shaft this controls. Null resolves to the nearest elevator in the controller's own z-level
    ///         stack.
    /// </summary>
    /// <seealso cref="ZLevelElevatorComponent.ShaftId"/>
    [DataField, AutoNetworkedField]
    public string? ShaftId;
}
