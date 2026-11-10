using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ZLevel.Elevators;

/// <summary>
///     Calls an elevator to whichever z-level this button is on.
/// </summary>
/// <remarks>
///     The floor it calls to is the button's own, read off its transform at the moment it is pressed, so a
///         mapper places the same prototype on every floor and wires nothing. Give it an UseDelay in yaml;
///         the call set swallows repeats but the popup and sound should not.
/// </remarks>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
[Access(typeof(SharedZLevelElevatorSystem))]
public sealed partial class ZLevelElevatorCallButtonComponent : Component
{
    /// <summary>
    ///     Which shaft to call. Null resolves to the nearest elevator in the button's own z-level stack.
    /// </summary>
    /// <seealso cref="ZLevelElevatorComponent.ShaftId"/>
    [DataField, AutoNetworkedField]
    public string? ShaftId;

    /// <summary>
    ///     Whether pressing this button while the elevator is already here does anything.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool CallWhenPresent;

    /// <summary>
    ///     Played on every press, answered or not, so that a button always feels like it did something.
    /// </summary>
    [DataField]
    public SoundSpecifier? ClickSound = new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg");
}
