using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.GenericSpriteFlick;

/// <summary>
///     Allows easy sprite flicks on entities. Sprite flicks will not be networked if the
///         entity just entered PVS while the flick was ongoing.
///
///     Optionally also allows a 'finish-state' for a flick: the state an entity should be in after an animation.
///         This only really works for entities with one flick state ever. When the finish-state is used,
///         ResetFlickFinishState should be called on the entity when the finish state should no longer be displayed.
///
///     TODO LCDC: TODO KS14: Remove all spriteflick finish-state and all uses of it
/// </summary>
public sealed class KsGenericSpriteFlickSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsGenericSpriteFlickFinishStateComponent, ComponentGetState>(OnFinishStateGetState);
    }

    private void OnFinishStateGetState(Entity<KsGenericSpriteFlickFinishStateComponent> entity, ref ComponentGetState args)
    {
        var state = new KsGenericSpriteFlickFinishStateComponentState(entity.Comp.FinishStates);
        args.State = state;
    }

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
        => Flick(uid, data.LayerKey, data.FlickState, finishState: data.FinishState, serverFilter: serverFilter);

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

        if (finishState is { })
        {
            var finishStateComponent = EnsureComp<KsGenericSpriteFlickFinishStateComponent>(uid);
            finishStateComponent.FinishStates[(GetAnimationId(flickState, layerKey), layerKey)] = finishState;

            Dirty(uid, finishStateComponent);
        }
    }

    [Obsolete("PLEASE DONT USE THIS ITS ASS")]
    public void ResetFlickFinishState(EntityUid uid, KsSpriteFlickData data)
        => ResetFlickFinishState(uid, data.LayerKey, data.FlickState);

    [Obsolete("PLEASE DONT USE THIS ITS ASS")]
    public void ResetFlickFinishState(EntityUid uid, string layerKey, string flickState)
    {
        if (!TryComp<KsGenericSpriteFlickFinishStateComponent>(uid, out var component))
            return;

        component.FinishStates.Remove((GetAnimationId(flickState, layerKey), layerKey));

        if (component.FinishStates.Count == 0)
            RemComp(uid, component);
        else
            Dirty(uid, component);
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

    [Obsolete("PLEASE DONT USE THIS ITS ASS")]
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public string? FinishState = null;
}

[Serializable, NetSerializable]
public sealed class KsSpriteFlickEvent(NetEntity entity, string layerKey, string flickState, string? finishState) : EntityEventArgs
{
    public NetEntity Entity = entity;
    public string LayerKey = layerKey;
    public string FlickState = flickState;
    public string? FinishState = finishState;
}
