using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Power.PTL;

[Serializable, NetSerializable]
public enum PTLVisuals : byte
{
    ChargeLevel,
    Active
}

[Serializable, NetSerializable]
public enum PTLVisualLayers : byte
{
    Base,
    Unpowered,
    Charge
}
