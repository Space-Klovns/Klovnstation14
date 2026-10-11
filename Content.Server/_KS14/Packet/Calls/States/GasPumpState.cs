using Content.Server._KS14.Packet.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Binary.Components;

namespace Content.Server._KS14.Packet.States;

public sealed class GasPressurePumpState : DeviceCall
{
    public float Pressure { get; set; }
    public bool Enabled { get; set; }
    protected override void GetCall(EntityUid entity, PacketSystem packetSystem, PacketExecutorComponent executor)
    {
        if (!_entityManager.TryGetComponent<GasPressurePumpComponent>(entity, out var pumpComponent))
            return;

        Pressure = pumpComponent.TargetPressure;
        Enabled = pumpComponent.Enabled;
    }
}

public sealed partial class GasVolumePumpState : DeviceCall
{
    public float Volume { get; set; }
    public bool Enabled { get; set; }
    protected override void GetCall(EntityUid entity, PacketSystem packetSystem, PacketExecutorComponent executor)
    {
        if (!_entityManager.TryGetComponent<GasVolumePumpComponent>(entity, out var pumpComponent))
            return;

        Volume = pumpComponent.TransferRate;
        Enabled = pumpComponent.Enabled;
    }
}
