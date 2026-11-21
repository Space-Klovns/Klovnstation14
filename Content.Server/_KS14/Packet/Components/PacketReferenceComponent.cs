namespace Content.Server._KS14.Packet.Components;

[RegisterComponent]
public sealed partial class PacketReferenceComponent : Component
{
    [DataField]
    public string RefName = "reference";
}
