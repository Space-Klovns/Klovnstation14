using Content.Server._KS14.Packet.Lathe;
using Content.Shared.Materials;

namespace Content.Server._KS14.Packet.Modules.Lathe;

[ModuleMethod("LatheModule")]
public sealed class LatheGetMaterialAmountMethod : ModuleMethod
{
    public override Module? Module { get; set; }
    public override Func<int, string, string, int> ModuleExec { get; }

    private int Func(int frequency, string address, string material)
    {
        if (Module is not LatheModule latheModule)
            return 0;

        var matSys = latheModule.MaterialStorageSystem;
        var protoMan = latheModule.PrototypeManager;

        if (!latheModule.PacketSystem.TryGetReceiver(frequency, address, out var receiver)
            || !protoMan.TryIndex<MaterialPrototype>(material, out var materialProto))
            return 0;

        return matSys.GetMaterialAmount(receiver, materialProto);
    }

    public LatheGetMaterialAmountMethod(Module? module) : base(module)
    {
        Id = "lathe_get_material_amount";
        Module = module;
        ModuleExec = Func;
    }
}
