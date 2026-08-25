using Content.Server._KS14.Packet.Base;
using Content.Server._KS14.Packet.Components;
using Content.Shared.Research.Prototypes;

namespace Content.Server._KS14.Packet.Modules.Base;

[ModuleMethod("NetworkModule")]
public sealed class GetNetworkMethod : ModuleMethod
{
    public override Module? Module { get; set; }

    public override Func<string, string[]> ModuleExec { get; }

    private string[] Func(string address)
    {
        if (Module is not BaseModule module)
            return [];

        if (!module.PacketSystem.TryGetNetwork(address, out var network))
            return [];

        return network.Addresses;
    }

    public GetNetworkMethod(Module? module) : base(module)
    {
        Id = "get_network";
        Module = module;
        ModuleExec = Func;
    }
}
