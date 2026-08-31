using Content.Server.DeviceLinking.Systems;
using Content.Shared.DeviceLinking;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.Base;

public sealed class NetworkPacketModule : PacketModule
{
    public SharedDeviceLinkSystem DeviceLinkSystem;

    public NetworkPacketModule(EntityManager manager, PrototypeManager protoMan, PacketSystem packet) : base(manager, protoMan, packet)
    {
        ModuleId = "NetworkPacketModule";
        DeviceLinkSystem = manager.System<SharedDeviceLinkSystem>();
    }
}
