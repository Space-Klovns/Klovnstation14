using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.DeviceLinkVisuals;

[RegisterComponent, NetworkedComponent]
public sealed partial class DeviceLinkVisualsComponent : Component;

[Serializable, NetSerializable]
public enum DeviceLinkVisuals
{
    /// <summary>
    ///     Bool - true if anything is connected via devicelink, false otherwise.
    /// </summary>
    Connected
}
