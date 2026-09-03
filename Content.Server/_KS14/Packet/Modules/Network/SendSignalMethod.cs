namespace Content.Server._KS14.Packet.Modules.Network;

[ModuleMethod("NetworkPacketModule")]
public sealed class SendSignalMethod : ModuleMethod
{
    public override PacketModule? Module { get; set; }

    public override Action<int, string, string> ModuleExec { get; }

    private void Func(int frequency, string address, string portId)
    {
        if (Module is not NetworkPacketModule module)
            return;

        var packetSys = module.PacketSystem;

        if (!packetSys.TryGetReceiver((module.Executor.Owner, module.Executor.Comp2), frequency, address, out var receiver))
        {
            module.Log(LogState.Error, "executor-log-no-device");
            return;
        }

        packetSys.TryWrapSystemCall(() => module.DeviceLinkSystem.InvokePort(receiver, portId));
    }

    public SendSignalMethod(PacketModule? module) : base(module)
    {
        Id = "listen";
        Module = module;
        ModuleExec = Func;
    }
}
