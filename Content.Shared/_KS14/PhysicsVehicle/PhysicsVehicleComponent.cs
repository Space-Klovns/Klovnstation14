using Robust.Shared.GameStates;

namespace Content.Shared._KS14.PhysicsVehicle;

[RegisterComponent, NetworkedComponent]
public sealed partial class PhysicsVehicleComponent : Component
{
    /// <summary>
    ///     Angular velocity added to entity when turning.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float TurnSpeed = 2.5f;

    /// <summary>
    ///     Angular velocity added to entity when turning.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public PhysicsVehicleTurnDirection TurnDirection = PhysicsVehicleTurnDirection.None;
}

public enum PhysicsVehicleTurnDirection : byte
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
}
