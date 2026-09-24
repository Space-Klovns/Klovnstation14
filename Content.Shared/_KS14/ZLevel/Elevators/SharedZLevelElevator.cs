using Robust.Shared.Serialization;

namespace Content.Shared._KS14.ZLevel.Elevators;

/// <summary>
///     Which way an elevator is sweeping through its stack.
/// </summary>
/// <remarks>
///     Idle is not "stopped": an elevator dwelling at a floor on its way up is still <see cref="Up"/>, which
///         is what makes it pick up a call further up rather than turning around for a nearer one behind it.
///     Direction is only cleared once there is nothing left to serve in either direction.
/// </remarks>
[Serializable, NetSerializable]
public enum ZLevelElevatorDirection : byte
{
    Idle,
    Up,
    Down,
}

/// <summary>
///     What an elevator with an <see cref="ActiveZLevelElevatorComponent"/> is currently doing.
/// </summary>
[Serializable, NetSerializable]
public enum ZLevelElevatorState : byte
{
    /// <summary>Crossing the gap between two z-levels.</summary>
    Travelling,

    /// <summary>Stopped at a floor, waiting out its dwell time before deciding where to go next.</summary>
    Dwelling,
}

[Serializable, NetSerializable]
public enum ZLevelElevatorControllerUiKey : byte
{
    Key,
}

/// <summary>
///     One floor as the controller window shows it.
/// </summary>
/// <param name="ZLevel">The z-level map this floor is.</param>
/// <param name="Number">Position in the stack, counting the bottom-most as one.</param>
/// <param name="Called">Whether the elevator has already been called here.</param>
/// <param name="Obstructed">
///     Whether the elevator's footprint on this z-level is currently covered by solid tiles. The floor is
///         still selectable - the elevator shears through - but an operator ought to know first.
/// </param>
[Serializable, NetSerializable]
public readonly record struct ZLevelElevatorFloor(NetEntity ZLevel, int Number, bool Called, bool Obstructed);

[Serializable, NetSerializable]
public sealed class ZLevelElevatorControllerState(
    List<ZLevelElevatorFloor> floors,
    NetEntity? currentFloor,
    ZLevelElevatorDirection direction,
    bool moving) : BoundUserInterfaceState
{
    /// <summary>Ascending, so the window has to draw it in reverse to read like a real lift panel.</summary>
    public List<ZLevelElevatorFloor> Floors = floors;

    /// <summary>The z-level the elevator's grid is on right now, even if it is between floors.</summary>
    public NetEntity? CurrentFloor = currentFloor;

    public ZLevelElevatorDirection Direction = direction;

    public bool Moving = moving;
}

/// <summary>
///     A floor button on the controller window was pressed.
/// </summary>
[Serializable, NetSerializable]
public sealed class ZLevelElevatorSelectFloorMessage(NetEntity zLevel) : BoundUserInterfaceMessage
{
    public NetEntity ZLevel = zLevel;
}
