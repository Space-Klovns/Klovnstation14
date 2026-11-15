using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.LawnMower;

[RegisterComponent, NetworkedComponent]
public sealed partial class KsLawnMowerComponent : Component;

[Serializable, NetSerializable]
public enum KsLawnMowerVisuals : byte
{
    SafetyDisabled,
}
