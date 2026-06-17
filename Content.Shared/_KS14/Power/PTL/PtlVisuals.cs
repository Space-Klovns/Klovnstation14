using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Power.PTL;

[Serializable, NetSerializable]
public enum PtlVisuals : byte
{
    ChargeLevel,
    Active
}

[Serializable, NetSerializable]
public enum PtlVisualLayers : byte
{
    Base,
    Unpowered,
    Charge
}
