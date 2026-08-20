using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.Base;

public sealed class BaseModule : Module
{
    public BaseModule(EntityManager manager, PrototypeManager protoMan, PacketSystem packet) : base(manager, protoMan, packet)
    {
        ModuleId = "BaseModule";
    }
}
