using Content.Server._KS14.Packet.Base;
using Content.Server._KS14.Packet.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Research.Prototypes;

namespace Content.Server._KS14.Packet.Modules.Base;

[ModuleMethod("NetworkPacketModule")]
public sealed class SinkListenMethod : ModuleMethod
{
    public override PacketModule? Module { get; set; }

    public override Action<int, string> ModuleExec { get; }

    private void Func(int portId, string funcName)
    {
        if (Module is not NetworkPacketModule module)
            return;

        if (!module.PrototypeManager.TryIndex<SinkPortPrototype>(module.Executor.Comp1.SignalPortNaming + portId, out var port))
        {
            module.Log(LogState.Error, "executor-log-invalid-sink");
            return;
        }

        module.PacketSystem.RegisterSignalMethod(port, funcName, module.Executor);
    }

    public SinkListenMethod(PacketModule? module) : base(module)
    {
        Id = "listen";
        Module = module;
        ModuleExec = Func;
    }
}
