using Content.Server._KS14.Packet.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.Components;

/// <summary>
/// Entities with this component are able to receive packets.
/// Packet will be delivered only if both address and frequency match with the sender's packet.
/// </summary>
[RegisterComponent]
public sealed partial class PacketReceiverComponent : Component
{

    /// <summary>
    /// Frequency is randomized each round based on prototype.
    /// The more dangerous device is - the longer it would take to bruteforce it.
    /// Act as a password, not used in code to find devices.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<PacketFrequencyPrototype> Frequency;

    /// <summary>
    /// Each device has different address, even if frequency is not the same.
    /// Assigned randomly upon entity initialization
    /// </summary>
    [DataField]
    public string Address = "0x000000";
}
