using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet;

/// <summary>
/// Modules. They contain methods for specific machinery (i.e Lathes).
/// Fully hardcoded.
/// </summary>
public abstract class Module
{
    public string ModuleId = "Default";

    public LocId ModuleName = "packets-module-default";

    public EntityManager EntityManager;

    public IPrototypeManager PrototypeManager;

    public PacketSystem PacketSystem;

    public Module(EntityManager manager, IPrototypeManager protoMan, PacketSystem packet)
    {
        EntityManager = manager;
        PrototypeManager = protoMan;
        PacketSystem = packet;
    }
}
