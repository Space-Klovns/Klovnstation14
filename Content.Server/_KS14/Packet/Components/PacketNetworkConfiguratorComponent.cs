using Content.Server._KS14.Packet.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.Components;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class PacketNetworkConfiguratorComponent : Component
{
    [DataField]
    public List<String> Addresses = new();

    [DataField]
    public ProtoId<PacketFrequencyPrototype>? Frequency;
}
