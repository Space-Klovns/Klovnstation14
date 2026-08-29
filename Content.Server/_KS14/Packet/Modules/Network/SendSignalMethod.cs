using Content.Server._KS14.Packet.Base;
using Content.Server._KS14.Packet.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Research.Prototypes;

namespace Content.Server._KS14.Packet.Modules.Base;

[ModuleMethod("NetworkModule")]
public sealed class SendSignalMethod : ModuleMethod
{
    public override Module? Module { get; set; }

    public override Action<int, string, string> ModuleExec { get; }

    private void Func(int frequency, string address, string portId)
    {
        if (Module is not NetworkModule module)
            return;

        var packetSys = module.PacketSystem;

        if (!packetSys.TryGetReceiver((module.Executor.Owner, module.Executor.Comp2), frequency, address, out var receiver))
        {
            module.Log(LogState.Error, "executor-log-no-device");
            return;
        }

        module.DeviceLinkSystem.InvokePort(receiver, portId);
    }

    public SendSignalMethod(Module? module) : base(module)
    {
        Id = "listen";
        Module = module;
        ModuleExec = Func;
    }
}
