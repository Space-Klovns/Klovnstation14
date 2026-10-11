using Content.Server._KS14.Packet.Components;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
namespace Content.Server._KS14.Packet.States;

public sealed class TurbineState : DeviceCall
{
    public float StatorLoad { get; set; }
    public float FlowRate { get; set; }
    public float Rpm { get; set; }

    protected override void GetCall(EntityUid entity, PacketSystem packetSystem, PacketExecutorComponent executor)
    {
        if (!_entityManager.TryGetComponent<TurbineComponent>(entity, out var reactor))
            return;

        StatorLoad = reactor.StatorLoad;
        FlowRate = reactor.FlowRate;
        Rpm = reactor.RPM;
    }
}
