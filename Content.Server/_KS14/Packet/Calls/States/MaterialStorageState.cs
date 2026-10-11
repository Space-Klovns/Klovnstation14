using Content.Server._KS14.Packet.Components;
using Content.Server.Materials;
using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.States;

public sealed class MaterialStorageState : DeviceCall
{
    public ProtoId<MaterialPrototype>[] Materials = [];
    public int[] Amount = [];

    private static MaterialStorageSystem _matStorage = _entityManager.System<MaterialStorageSystem>();

    protected override void GetCall(EntityUid entity, PacketSystem packetSystem, PacketExecutorComponent executor)
    {
        var mats = _matStorage.GetStoredMaterials(entity);

        Materials = [..mats.Keys];
        Amount = [..mats.Values];
    }
}
