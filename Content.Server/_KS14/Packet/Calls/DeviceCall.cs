using System.Threading;
using Content.Server._KS14.Packet.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.States;

public abstract class DeviceCall
{
    internal static EntityManager _entityManager = null!;
    internal static PrototypeManager _protoMan = null!;

    public void UpdateCall(EntityUid entity, PacketSystem packetSystem, PacketExecutorComponent executor, ManualResetEventSlim pauseHandler)
    {
        packetSystem.TryWrapSystemCall(() =>
        {
            GetCall(entity, packetSystem, executor);
        },
            pauseHandler);
    }

    protected abstract void GetCall(EntityUid entity,
        PacketSystem packetSystem,
        PacketExecutorComponent executor);
}
