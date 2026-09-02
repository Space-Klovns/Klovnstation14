using Content.Server._KS14.Packet.Base;
using Content.Server._KS14.Packet.Components;
using Content.Shared.Research.Prototypes;

namespace Content.Server._KS14.Packet.Modules.Base;

[ModuleMethod("BasePacketModule")]
public sealed class LogMethod : ModuleMethod
{
    public override PacketModule? Module { get; set; }

    public override Action<object> ModuleExec { get; }

    private void Func(object obj)
    {
        if (Module is not BasePacketModule module)
            return;

        module.PacketSystem.TryWrapSystemCall(() => module.Log(LogState.Info, obj));
    }

    public LogMethod(PacketModule? module) : base(module)
    {
        Id = "log";
        Module = module;
        ModuleExec = Func;
    }
}
