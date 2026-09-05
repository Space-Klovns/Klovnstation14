using Content.Shared.Trigger.Components.Effects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Trigger.Components.Effects;

/// <summary>
///     Sets an entity storage to some state upon trigger.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EntityStorageOnTriggerComponent : BaseXOnTriggerComponent
{
    /// <summary>
    ///     If the trigger will open, close, or toggle the storage.
    /// </summary>
    [DataField, AutoNetworkedField]
    public StorageAction Mode = StorageAction.Toggle;

    /// <summary>
    ///     If true, will open the container no matter if its locked or not.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Force = false;

    [DataField, AutoNetworkedField]
    public bool Silent = true;
}

[Serializable, NetSerializable]
public enum StorageAction
{
    Open = 0,
    Close = 1,
    Toggle = 2,
}
