using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.ZLevel.Auto;

// For mapping ig
/// <summary>
///     When a map starts up unpaused/becomes unpaused with this component, will be added above/under
///         any other unpaused auto-zlevel-map with the same ID (if any) and the component will then be removed.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(KsZLevelSystem))]
public sealed partial class KsAutoZLevelComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField(required: true)]
    public string Id = "";

    /// <summary>
    ///     Whether to spawn the z-level above or under the other z-level.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public KsAutoZLevelType Location = KsAutoZLevelType.Above;
}

[Serializable, NetSerializable]
public enum KsAutoZLevelType : byte
{
    /// <summary>
    ///     This z-level will be spawned above the other one.
    /// </summary>
    Above,

    /// <summary>
    ///     This z-level will be spawned under the other one.
    /// </summary>
    Under
}
