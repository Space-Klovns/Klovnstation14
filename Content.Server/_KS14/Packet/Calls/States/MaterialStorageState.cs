using Content.Server._KS14.Packet.Components;
using Content.Server.Materials;
using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.States;

public sealed class MaterialStorageState : DeviceCall
{
    public ProtoId<MaterialPrototype>[] Materials = [];
    public int[] Amount = [];

    protected override void GetCall(EntityUid entity, PacketSystem packetSystem, EntityManager entityMan, IPrototypeManager protoMan, PacketExecutorComponent executor)
    {
        var mats = packetSystem.MaterialStorageSystem.GetStoredMaterials(entity);

        Materials = [..mats.Keys];
        Amount = [..mats.Values];
    }
}
