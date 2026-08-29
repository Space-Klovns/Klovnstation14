using Content.Server.DeviceLinking.Systems;
using Content.Shared.DeviceLinking;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.Base;

public sealed class NetworkModule : Module
{
    public SharedDeviceLinkSystem DeviceLinkSystem;

    public NetworkModule(EntityManager manager, PrototypeManager protoMan, PacketSystem packet) : base(manager, protoMan, packet)
    {
        ModuleId = "NetworkModule";
        DeviceLinkSystem = manager.System<SharedDeviceLinkSystem>();
    }
}
