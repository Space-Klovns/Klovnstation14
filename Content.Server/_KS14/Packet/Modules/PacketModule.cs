using Content.Server._KS14.Packet.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet;

/// <summary>
/// Modules. They contain methods for specific machinery (i.e Lathes).
/// Fully hardcoded.
/// </summary>
public abstract class PacketModule
{
    public string ModuleId = "Default";

    public LocId ModuleName = "packets-module-default";

    public Entity<ExecutorComponent, PacketNetworkComponent?> Executor;

    public EntityManager EntityManager;

    public IPrototypeManager PrototypeManager;

    public PacketSystem PacketSystem;

    public PacketModule(EntityManager manager, IPrototypeManager protoMan, PacketSystem packet)
    {
        EntityManager = manager;
        PrototypeManager = protoMan;
        PacketSystem = packet;
    }

    #region Log system.

    public void Log(LogState state, object msg)
    {
        var stringState = "";

        switch (state)
        {
            case LogState.Info:
                stringState = "[@]: ";
                break;
            case LogState.Warning:
                stringState = "[?]: ";
                break;
            case LogState.Error:
                stringState = "[!]: ";
                break;
            case LogState.Debug:
                stringState = "[>]: ";
                break;
        }

        if (state == LogState.Debug && !Executor.Comp1.DebugState)
            return;

        Executor.Comp1.Log += stringState + Loc.GetString(msg.ToString() ?? "executor-log-invalid") + "\n";
    }

}

public enum LogState
{
    Info,
    Warning,
    Error,
    Debug
}
    #endregion
