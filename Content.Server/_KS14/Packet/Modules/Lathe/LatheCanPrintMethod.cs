using Content.Server._KS14.Packet.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;

namespace Content.Server._KS14.Packet.Modules.Lathe;

[ModuleMethod("LatheModule")]
public sealed class LatheCanPrintMethod : ModuleMethod
{
    public override Module? Module { get; set; }
    public override Func<int, string, string, int, bool> ModuleExec { get; }

    private bool Func(int frequency, string address, string item, int quantity)
    {
        if (Module is not LatheModule latheModule)
            return false;

        var latheSys = latheModule.LatheSystem;
        var protoMan = latheModule.PrototypeManager;

        if (!protoMan.TryIndex<LatheRecipePrototype>(item, out var recipe) ||
            !latheModule.PacketSystem.TryGetReceiver(frequency, address, out var receiver))
            return false;

        return latheSys.CanProduce(receiver, recipe, quantity);
    }

    public LatheCanPrintMethod(Module? module) : base(module)
    {
        Id = "lathe_can_print";
        Module = module;
        ModuleExec = Func;
    }
}
