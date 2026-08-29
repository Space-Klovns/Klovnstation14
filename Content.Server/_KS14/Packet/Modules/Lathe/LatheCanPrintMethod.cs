using System.Threading.Channels;
using System.Threading.Tasks;
using Content.Server._KS14.Packet.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;

namespace Content.Server._KS14.Packet.Modules.Lathe;

[ModuleMethod("LatheModule")]
public sealed class LatheCanPrintMethod : ModuleMethod
{
    public override Module? Module { get; set; }
    public override Func<int, string, string, int, Task<bool>> ModuleExec { get; }

    private async Task<bool> Func(int frequency, string address, string item, int quantity)
    {
        if (Module is not LatheModule module)
            return false;

        var latheSys = module.LatheSystem;
        var protoMan = module.PrototypeManager;
        var packetSys = module.PacketSystem;

        if (!protoMan.TryIndex<LatheRecipePrototype>(item, out var recipe))
        {
            module.Log(LogState.Error, "executor-log-invalid-lathe-recipe");
            return false;
        }

        if (!packetSys.TryGetReceiver((module.Executor.Owner, module.Executor.Comp2), frequency, address, out var receiver))
        {
            module.Log(LogState.Error, "executor-log-no-device");
            return false;
        }

        if (Environment.CurrentManagedThreadId == packetSys._mainThreadId)
            return latheSys.CanProduce(receiver, recipe, quantity);

        packetSys.WrapSystemCall(async void () =>
        {
            await _channel.Writer.WriteAsync(latheSys.CanProduce(receiver, recipe, quantity));
        });

        var rVal = await _channel.Reader.ReadAsync();

        return (bool)rVal;
    }

    public LatheCanPrintMethod(Module? module) : base(module)
    {
        Id = "lathe_can_print";
        Module = module;
        ModuleExec = Func;
    }
}
