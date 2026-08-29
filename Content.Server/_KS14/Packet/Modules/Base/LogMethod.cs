using Content.Server._KS14.Packet.Base;
using Content.Server._KS14.Packet.Components;
using Content.Shared.Research.Prototypes;

namespace Content.Server._KS14.Packet.Modules.Base;

[ModuleMethod("BaseModule")]
public sealed class LogMethod : ModuleMethod
{
    public override Module? Module { get; set; }

    public override Action<object> ModuleExec { get; }

    private void Func(object obj)
    {
        if (Module is not BaseModule module)
            return;

        module.Log(LogState.Info, obj);
    }

    public LogMethod(Module? module) : base(module)
    {
        Id = "log";
        Module = module;
        ModuleExec = Func;
    }
}
