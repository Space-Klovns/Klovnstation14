using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.GenericSpriteFlick;

/// <summary>
///     Allows easy sprite flicks on entities. Sprite flicks will not be networked if the
///         entity just entered PVS while the flick was ongoing.
///
///     A flicked layer goes back to the state it had before the flick. If the layer should be in a different state
///         afterwards, drive that state with appearance data (e.g. a GenericVisualizer) instead: the flick re-applies
///         appearance when it finishes.
/// </summary>
public sealed partial class KsGenericSpriteFlickSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;

    public static string GetAnimationId(string flickState, string layerKey)
        => flickState + layerKey + " ksgenericspriteflick";

    public void TryFlick(EntityUid? uid, KsSpriteFlickData? data, Filter? serverFilter = null)
    {
        if (uid is not { } ||
            data is not { })
            return;

        Flick(uid.Value, data, serverFilter: serverFilter);
    }

    public void Flick(EntityUid uid, KsSpriteFlickData data, Filter? serverFilter = null)
        => Flick(uid, data.LayerKey, data.FlickState, serverFilter: serverFilter);

    public void Flick(EntityUid uid, string layerKey, string flickState, Filter? serverFilter = null)
    {
        var netEntity = GetNetEntity(uid);
        var ev = new KsSpriteFlickEvent(netEntity, layerKey, flickState);

        if (_netManager.IsServer)
        {
            serverFilter ??= Filter.Pvs(uid, entityManager: EntityManager);
            RaiseNetworkEvent(ev, serverFilter);
        }
        else
            RaiseLocalEvent(ev);
    }
}

/// <summary>
///     Wrapper for sprite flick data
/// </summary>
[DataDefinition]
public sealed partial class KsSpriteFlickData
{
    [DataField(required: true), ViewVariables(VVAccess.ReadWrite)]
    public string LayerKey = default!;

    [DataField(required: true), ViewVariables(VVAccess.ReadWrite)]
    public string FlickState = default!;
}

[Serializable, NetSerializable]
public sealed class KsSpriteFlickEvent(NetEntity entity, string layerKey, string flickState) : EntityEventArgs
{
    public NetEntity Entity = entity;
    public string LayerKey = layerKey;
    public string FlickState = flickState;
}
