using System.Threading.Tasks;
using Content.Server._KS14.Packet.Base;

namespace Content.Server._KS14.Packet.Modules.Base;

[ModuleMethod("BaseModule")]
public sealed class InputMethod : ModuleMethod
{
    public override Module? Module { get; set; }

    public override Func<Task<string>> ModuleExec { get; }

    private async Task<string> Func()
    {
        if (Module is not BaseModule module)
            return "";

        return (string) await Channel.Reader.ReadAsync();
    }

    public InputMethod(Module? module) : base(module)
    {
        Id = "input";
        Module = module;
        ModuleExec = Func;
    }
}
