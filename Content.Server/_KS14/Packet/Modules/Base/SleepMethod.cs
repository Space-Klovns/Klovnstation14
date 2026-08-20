using System.Threading.Tasks;
using Content.Server._KS14.Packet.Base;
using Content.Shared.Research.Prototypes;

namespace Content.Server._KS14.Packet.Modules.Base;

[ModuleMethod("BaseModule")]
public sealed class SleepMethod : ModuleMethod
{
    public override Module? Module { get; set; }

    public override Func<int, Task> ModuleExec { get; }

    private async Task Func(int ms)
    {
        await Task.Delay(ms);
    }

    public SleepMethod(Module? module) : base(module)
    {
        Id = "sleep";
        Module = module;
        ModuleExec = Func;
    }
}
