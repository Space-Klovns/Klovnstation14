using Content.Server._KS14.Packet.Base;
using Content.Server._KS14.Packet.Components;
using Content.Shared.Research.Prototypes;

namespace Content.Server._KS14.Packet.Modules.Base;

[ModuleMethod("BaseModule")]
public sealed class LogMethod : ModuleMethod
{
    public override Module? Module { get; set; }

    public override Action<int, string, object> ModuleExec { get; }

    private void Func(int frequency, string address, object obj)
    {
        if (Module is not BaseModule module)
            return;

        if (!module.PacketSystem.TryGetReceiver(frequency, address, out var receiver)
            || !module.EntityManager.TryGetComponent<ExecutorComponent>(receiver, out var entity))
            return;

        entity.Log += obj+"\n";
    }

    public LogMethod(Module? module) : base(module)
    {
        Id = "log";
        Module = module;
        ModuleExec = Func;
    }
}
