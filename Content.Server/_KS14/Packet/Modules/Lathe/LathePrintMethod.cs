using System.Threading.Tasks;
using Content.Server._KS14.Packet.Lathe;
using Content.Shared.Research.Prototypes;

namespace Content.Server._KS14.Packet.Modules.Lathe;

[ModuleMethod("LatheModule")]
public sealed class LathePrintMethod : ModuleMethod
{
    public override Module? Module { get; set; }

    public override Func<int, string, string, int, Task> ModuleExec { get; }

    private async Task Func(int frequency, string address, string item, int quantity)
    {
        if (Module is not LatheModule latheModule)
            return;

        var latheSys = latheModule.LatheSystem;
        var protoMan = latheModule.PrototypeManager;

        if (!protoMan.TryIndex<LatheRecipePrototype>(item, out var recipe) ||
            !latheModule.PacketSystem.TryGetReceiver(frequency, address, out var receiver))
            return;

        latheSys.TryAddToQueue(receiver, recipe, quantity);
        latheSys.TryStartProducing(receiver);

        await Task.Delay(recipe.CompleteTime * quantity);
    }

    public LathePrintMethod(Module? module) : base(module)
    {
        Id = "lathe_print";
        Module = module;
        ModuleExec = Func;
    }
}
