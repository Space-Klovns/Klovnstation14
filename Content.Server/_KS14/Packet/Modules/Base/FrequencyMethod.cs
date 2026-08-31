using Content.Server._KS14.Packet.Base;
using Content.Server._KS14.Packet.Components;
using Content.Shared.Research.Prototypes;
using Jint;

namespace Content.Server._KS14.Packet.Modules.Base;

[ModuleMethod("BasePacketModule")]
public sealed class FrequencyMethod : ModuleMethod
{
    public override PacketModule? Module { get; set; }

    public override Action<int, string> ModuleExec { get; }

    private void Func(int confFreq, string operation)
    {
        if (Module is not BasePacketModule module)
            return;

        if (Module.Executor.Comp2 is not { } receiver)
            return;

        switch (operation)
        {
            case "allow":
                receiver.ListeningFrequencies.Add(module.PacketSystem.GetFrequency(confFreq));
                break;
            case "deny":
                receiver.ListeningFrequencies.Remove(module.PacketSystem.GetFrequency(confFreq));
                break;
        }
    }

    public FrequencyMethod(PacketModule? module) : base(module)
    {
        Id = "frequency";
        Module = module;
        ModuleExec = Func;
    }
}
