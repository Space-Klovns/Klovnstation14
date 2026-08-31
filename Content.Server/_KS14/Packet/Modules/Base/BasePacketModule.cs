using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.Base;

public sealed class BasePacketModule : PacketModule
{
    public BasePacketModule(EntityManager manager, PrototypeManager protoMan, PacketSystem packet) : base(manager, protoMan, packet)
    {
        ModuleId = "BasePacketModule";
    }
}
