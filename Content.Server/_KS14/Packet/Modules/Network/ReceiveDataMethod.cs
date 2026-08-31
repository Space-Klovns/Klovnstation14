using System.Threading.Tasks;
using Content.Server._KS14.Packet.Base;

namespace Content.Server._KS14.Packet.Modules.Base;

[ModuleMethod("NetworkModule")]
public sealed class ReceiveDataMethod : ModuleMethod
{
    public override Module? Module { get; set; }

    public override Func<Task<object>> ModuleExec { get; }

    private async Task<object> Func()
    {
        return await Channel.Reader.ReadAsync();
    }

    public ReceiveDataMethod(Module? module) : base(module)
    {
        Id = "data_receive";
        Module = module;
        ModuleExec = Func;
    }
}
