using Content.Server._KS14.Packet.Base;
using Content.Server._KS14.Packet.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Research.Prototypes;

namespace Content.Server._KS14.Packet.Modules.Base;

[ModuleMethod("NetworkModule")]
public sealed class SinkListenMethod : ModuleMethod
{
    public override Module? Module { get; set; }

    public override Action<int, string> ModuleExec { get; }

    private void Func(int portId, string funcName)
    {
        if (Module is not NetworkModule module)
            return;

        if (!module.PrototypeManager.TryIndex<SinkPortPrototype>(module.Executor.Comp1.SignalPortNaming + portId, out var port))
        {
            module.Log(LogState.Error, "executor-log-invalid-sink");
            return;
        }

        module.PacketSystem.RegisterSignalMethod(port, funcName, module.Executor);
    }

    public SinkListenMethod(Module? module) : base(module)
    {
        Id = "listen";
        Module = module;
        ModuleExec = Func;
    }
}
