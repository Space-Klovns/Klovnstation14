using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.OreVent.Drone;

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
    public EntityUid? VentUid = null;

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

    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public OreVentDroneMovement LastMovementState = OreVentDroneMovement.None;

    /// <summary>
    ///     Chance, from 0 to 1, that an escaping drone says it has to go home, then never makes it.
    /// </summary>
    [DataField]
    public float FunnyEndingChance = 0.01f;

    /// <summary>
    ///     Whether this drone, escaping, is going to blow up as it leaves.
    /// </summary>
    [ViewVariables]
    public bool FunnyEnding;

    [DataField]
    public SoundSpecifier FunnyEndingSound = new SoundPathSpecifier("/Audio/Effects/explosion3.ogg", AudioParams.Default.WithVolume(-6f));
}

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
    Movement
}

[Serializable, NetSerializable]
public enum OreVentDroneMovement : byte
{
    None = 0,
    Arriving = 1,
    Dipping = 2
}

/// <summary>
///     Raised on a vent associated with a <see cref="OreVentDroneComponent"/>
///         when the drone gets destroyed.
/// </summary>
[ByRefEvent]
public readonly record struct OreVentDroneDestroyedEvent(EntityUid DroneUid);
