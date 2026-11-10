using Content.Shared.DeviceLinking;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.ZLevel.Elevators;

/// <summary>
///     Emits a device link signal on one port when an elevator stops, and on another when it is about to
///         move again.
/// </summary>
/// <remarks>
///     Put on anything - a door on the elevator itself, a shutter in the shaft, a light on a landing. The
///         departure signal is raised before the elevator actually leaves, so that a door wired to it has
///         the travel time to finish closing.
/// </remarks>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
[Access(typeof(SharedZLevelElevatorSystem))]
public sealed partial class ZLevelElevatorSignalComponent : Component
{
    /// <summary>
    ///     Invoked when the elevator has stopped at a floor.
    /// </summary>
    [DataField]
    public ProtoId<SourcePortPrototype> StoppedPort = "KsElevatorStopped";

    /// <summary>
    ///     Invoked just before the elevator leaves a floor.
    /// </summary>
    [DataField]
    public ProtoId<SourcePortPrototype> MovingPort = "KsElevatorMoving";

    /// <summary>
    ///     Which shaft to listen to. Null resolves to the nearest elevator in this entity's own z-level
    ///         stack - which, for something sitting on the elevator, is the elevator it is riding.
    /// </summary>
    /// <seealso cref="ZLevelElevatorComponent.ShaftId"/>
    [DataField, AutoNetworkedField]
    public string? ShaftId;
}
