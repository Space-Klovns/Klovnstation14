using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.Base;

public sealed class NetworkModule : Module
{
    public NetworkModule(EntityManager manager, PrototypeManager protoMan, PacketSystem packet) : base(manager, protoMan, packet)
    {
        ModuleId = "NetworkModule";
    }
}
