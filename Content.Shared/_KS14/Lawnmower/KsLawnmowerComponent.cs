using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Lawnmower;

[RegisterComponent, NetworkedComponent]
public sealed partial class KsLawnmowerComponent : Component;

[Serializable, NetSerializable]
public enum KsLawnmowerVisuals : byte
{
    SafetyDisabled,
}
