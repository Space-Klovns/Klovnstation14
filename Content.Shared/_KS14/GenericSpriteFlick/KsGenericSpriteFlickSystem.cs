using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.GenericSpriteFlick;

public sealed class KsGenericSpriteFlickSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netManager = default!;

    public void Flick(EntityUid uid, string layerKey, string flickState, string? finishState = null, Filter? serverFilter = null)
    {
        var netEntity = GetNetEntity(uid);
        var ev = new KsSpriteFlickEvent(netEntity, layerKey, flickState, finishState);

        if (_netManager.IsServer)
        {
            serverFilter ??= Filter.Pvs(uid, entityManager: EntityManager);
            RaiseNetworkEvent(ev, serverFilter);
        }
        else
            RaiseLocalEvent(ev);
    }
}

[Serializable, NetSerializable]
public sealed class KsSpriteFlickEvent(NetEntity entity, string layerKey, string flickState, string? finishState) : EntityEventArgs
{
    public NetEntity Entity = entity;
    public string LayerKey = layerKey;
    public string FlickState = flickState;
    public string? FinishState = finishState;
}
