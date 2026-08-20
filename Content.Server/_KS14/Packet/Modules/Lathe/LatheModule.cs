using Content.Server.Lathe;
using Content.Server.Materials;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.Lathe;

public sealed class LatheModule : Module
{
    public LatheSystem LatheSystem;
    public MaterialStorageSystem MaterialStorageSystem;

    public LatheModule(EntityManager manager, PrototypeManager protoMan, PacketSystem packet) : base(manager, protoMan, packet)
    {
        ModuleId = "LatheModule";

        LatheSystem = manager.System<LatheSystem>();
        MaterialStorageSystem = manager.System<MaterialStorageSystem>();
    }
}
