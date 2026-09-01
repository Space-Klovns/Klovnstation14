using System.Threading.Channels;
using System.Threading.Tasks;
using Content.Server._KS14.Packet.Base;
using Content.Server._KS14.Packet.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;

namespace Content.Server._KS14.Packet.Modules.Lathe;

[ModuleMethod("BasePacketModule")]
public sealed class PingMethod : ModuleMethod
{
    public override PacketModule? Module { get; set; }
    public override Func<int, Task<string>> ModuleExec { get; }

    private async Task<string> Func(int frequency)
    {
        if (Module is not BasePacketModule module)
            return "";

        var packetSys = module.PacketSystem;

        return await packetSys.TryWrapSystemCall(() => packetSys.TryRandomReceiver(module.Executor, frequency, 10, out var receiver) ? receiver.Comp.Address : "",
            Channel);
    }

    public PingMethod(PacketModule? module) : base(module)
    {
        Id = "ping";
        Module = module;
        ModuleExec = Func;
    }
}
