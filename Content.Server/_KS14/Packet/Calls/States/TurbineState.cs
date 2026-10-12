using Content.Server._KS14.Packet.Components;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.States;

public sealed class TurbineState : DeviceCall
{
    public float StatorLoad { get; set; }
    public float FlowRate { get; set; }
    public float Rpm { get; set; }

    protected override void GetCall(EntityUid entity, PacketSystem packetSystem, EntityManager entityMan, IPrototypeManager protoMan, PacketExecutorComponent executor)
    {
        if (!entityMan.TryGetComponent<TurbineComponent>(entity, out var reactor))
            return;

        StatorLoad = reactor.StatorLoad;
        FlowRate = reactor.FlowRate;
        Rpm = reactor.RPM;
    }
}
