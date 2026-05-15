using System.Numerics;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.RadarInterest;

// Shoddy system for getting data for things that should be shown on radar and will probably never locally move
// TODO LCDC: Handle deletion of interests, currently it does epic fails

public sealed class KsRadarInterestSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly ISharedPlayerManager _playerManager = default!;

    /// <summary>
    ///     Networked dictionary of interests, with their respective data+parent+localpos.
    ///         Only populated clientside.
    /// </summary>
    public readonly Dictionary<EntityUid, (KsRadarInterestData, EntityUid, Vector2)> StaticInterests = [];

    public override void Initialize()
    {
        base.Initialize();

        if (_netManager.IsServer)
        {
            _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;

            SubscribeLocalEvent<KsRadarInterestComponent, ComponentStartup>(OnInterestStartup);
            SubscribeLocalEvent<KsRadarInterestComponent, ComponentShutdown>(OnInterestShutdown);

            SubscribeLocalEvent<KsRadarInterestComponent, EntParentChangedMessage>(OnInterestParentChanged);
        }
        else
        {
            SubscribeNetworkEvent<KsStaticRadarInterestResetMessage>(OnResetMessage);
            SubscribeNetworkEvent<KsStaticRadarInterestDeltaMessage>(OnDeltaMessage);
        }
    }

    public void AddInterest(EntityUid uid, KsRadarInterestData data)
    {
        if (HasComp<KsRadarInterestComponent>(uid))
            return;

        var component = EntityManager.ComponentFactory.GetComponent<KsRadarInterestComponent>();
        component.Data = data;

        AddComp(uid, component);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.InGame)
            return;

        var netState = new Dictionary<NetEntity, (KsRadarInterestData, NetEntity, Vector2)>();
        foreach (var (ourKey, ourVal) in StaticInterests)
            netState[GetNetEntity(ourKey)] = (ourVal.Item1, GetNetEntity(ourVal.Item2), ourVal.Item3);

        RaiseNetworkEvent(new KsStaticRadarInterestResetMessage(netState), args.Session);
    }

    private void OnInterestParentChanged(Entity<KsRadarInterestComponent> entity, ref EntParentChangedMessage args)
    {
        if (TerminatingOrDeleted(entity)) // shutdown should handle this idk
            return;

        StaticInterests[entity.Owner] = (entity.Comp.Data, args.Transform.ParentUid, args.Transform.LocalPosition);

        RaiseNetworkEvent(new KsStaticRadarInterestDeltaMessage(
            GetNetEntity(args.Transform.ParentUid),
            GetNetEntity(entity.Owner),
            entity.Comp.Data,
            args.Transform.LocalPosition
         ));
    }

    private void OnInterestStartup(Entity<KsRadarInterestComponent> entity, ref ComponentStartup args)
    {
        if (entity.Comp.Data.Text.IsWhiteSpace())
            entity.Comp.Data.Text = MetaData(entity.Owner).EntityName;

        var transformComponent = Transform(entity);
        StaticInterests[entity.Owner] = (entity.Comp.Data, transformComponent.ParentUid, transformComponent.LocalPosition);

        RaiseNetworkEvent(new KsStaticRadarInterestDeltaMessage(
            GetNetEntity(transformComponent.ParentUid),
            GetNetEntity(entity.Owner),
            entity.Comp.Data,
            transformComponent.LocalPosition
         ));
    }

    private void OnInterestShutdown(Entity<KsRadarInterestComponent> entity, ref ComponentShutdown args)
    {
        StaticInterests.Remove(entity.Owner);

        RaiseNetworkEvent(new KsStaticRadarInterestDeltaMessage(
            NetEntity.Invalid,
            GetNetEntity(entity.Owner),
            null,
            Vector2.Zero
         ));
    }

    private void OnResetMessage(KsStaticRadarInterestResetMessage msg)
    {
        foreach (var (theirKey, theirVal) in msg.AllInterests)
        {
            if (!TryGetEntity(theirKey, out var dataEntity) ||
                !TryGetEntity(theirVal.Item2, out var ownerEntity))
                return;

            StaticInterests[dataEntity.Value] = (theirVal.Item1, ownerEntity.Value, theirVal.Item3);
        }
    }

    private void OnDeltaMessage(KsStaticRadarInterestDeltaMessage msg)
    {
        if (!TryGetEntity(msg.DataEntity, out var dataEntity))
            return;

        if (!TryGetEntity(msg.NewOwnerEntity, out var ownerEntity) ||
            msg.Data == null)
        {

            StaticInterests.Remove(dataEntity.Value);
            return;
        }

        StaticInterests[dataEntity.Value] = (msg.Data, ownerEntity.Value, msg.StaticPosition)!;
    }
}

[Serializable, NetSerializable]
public sealed class KsStaticRadarInterestResetMessage(Dictionary<NetEntity, (KsRadarInterestData, NetEntity, Vector2)> allInterests) : EntityEventArgs
{
    public Dictionary<NetEntity, (KsRadarInterestData, NetEntity, Vector2)> AllInterests = allInterests;
}

[Serializable, NetSerializable]
public sealed class KsStaticRadarInterestDeltaMessage(NetEntity newOwnerEntity, NetEntity dataEntity, KsRadarInterestData? data, Vector2 staticPosition) : EntityEventArgs
{
    public NetEntity NewOwnerEntity = newOwnerEntity;

    public NetEntity DataEntity = dataEntity;
    public KsRadarInterestData? Data = data;
    public Vector2 StaticPosition = staticPosition;
}
