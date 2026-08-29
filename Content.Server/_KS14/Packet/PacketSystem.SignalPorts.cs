using Content.Server._KS14.Packet.Components;
using Content.Shared.DeviceLinking;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet;

public sealed partial class PacketSystem
{
    private void InitializePorts(Entity<ExecutorComponent> ent)
    {
        List<ProtoId<SinkPortPrototype>> ports = [];

        for (var i = 0; i < ent.Comp.PortCount; i++)
        {
            if (!_prototypeManager.TryIndex<SinkPortPrototype>(ent.Comp.SignalPortNaming + i, out var port))
            {
                Logger.Info("couldnt," + ent.Comp.SignalPortNaming + i);
                continue;
            }

            ports.Add(port);
        }

        _deviceLinkSystem.EnsureSinkPorts(ent, ports.ToArray());
    }

    public void RegisterSignalMethod(ProtoId<SinkPortPrototype> port, string funName, Entity<ExecutorComponent> ent)
    {
        var engine = EnsureEngine(ent);
        var value = engine.GetValue(funName);

        ent.Comp.ListeningPorts.TryAdd(port, value);
    }

    private void OnSignal(ProtoId<SinkPortPrototype> port, Entity<ExecutorComponent> ent)
    {
        if (!ent.Comp.ListeningPorts.TryGetValue(port, out var func))
            return;

        var engine = EnsureEngine(ent);
        engine.Invoke(func);
    }
}
