using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.OreVent;

/// <summary>
///     For drones that buckle to ore vents and do whatever.
///         Should have <see cref="Buckle.Components.BuckleComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class OreVentDroneComponent : Component
{
    /// <summary>
    ///     The vent that this drone is attached to.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid VentUid = EntityUid.Invalid;

    /// <summary>
    ///     How many progress states this has.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public int ProgressStates = 4;

    /// <summary>
    ///     Last progress state, -1 for no state. Client-only.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public int LastActiveProgressState = -1;

    /// <summary>
    ///     How many progress states this has.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public OreVentDroneMovement LastMovementState = OreVentDroneMovement.None;
}

[Serializable, NetSerializable]
public enum OreVentDroneVisualLayers : byte { ProgressBar }

[Serializable, NetSerializable]
public enum OreVentDroneVisuals : byte
{
    /// <summary>
    ///     Int or whatever
    /// </summary>
    Progress,

    /// <summary>
    ///     Boolean
    /// </summary>
    Flying,

    /// <summary>
    ///     Boolean
    /// </summary>
    Movement
}

[Serializable, NetSerializable]
public enum OreVentDroneMovement : byte
{
    None = 0,
    Arriving = 1,
    StartingUp = 2,
    Dipping = 3
}

/// <summary>
///     Raised on a vent associated with a <see cref="OreVentDroneComponent"/>
///         when the drone gets destroyed.
/// </summary>
[ByRefEvent]
public readonly record struct OreVentDroneDestroyedEvent(EntityUid DroneUid);
