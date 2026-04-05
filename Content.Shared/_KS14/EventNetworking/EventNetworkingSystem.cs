using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.EventNetworking;

public sealed class EventNetworkingSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netManager = default!;

    // Components and their handlers
    private readonly Dictionary<Type, Action<EntityUid, object>?> _handlers = [];

    public override void Initialize()
    {
        base.Initialize();

        if (_netManager.IsClient)
            SubscribeNetworkEvent<NetworkedLocalEvent>(OnNetworkedLocalEventReceived);
    }

    private void OnNetworkedLocalEventReceived(NetworkedLocalEvent args)
    {
        if (!TryGetEntity(args.TargetEntity, out var uid))
            return;

        foreach (var (handlerType, handlerDel) in _handlers)
        {
            if (handlerType.IsInstanceOfType(args.Args))
                continue;

            handlerDel!(uid.Value, args.Args);
        }
    }

    public void SubscribeNetworkedLocalEvent<TComp, TEvent>(EntityEventRefHandler<TComp, TEvent> handler)
        where TComp : IComponent
        where TEvent : notnull
    {
        var query = GetEntityQuery<TComp>();

        if (!_handlers.ContainsKey(typeof(TEvent)))
            _handlers[typeof(TEvent)] = null;

        _handlers[typeof(TEvent)] += (uid, args) =>
        {
            if (!query.TryGetComponent(uid, out var component))
                return;

            var convArgs = (TEvent)args;
            handler((uid, component), ref convArgs);
        };
    }

    /// <summary>
    ///     Automatically makes a subscription to just raise the event
    ///         back locally.
    /// </summary>
    public void SubscribeNetworkedLocalEventAutoByRef<TComp, TEvent>()
        where TComp : IComponent
        where TEvent : notnull
    {
        var query = GetEntityQuery<TComp>();

        _handlers[typeof(TEvent)] += (uid, args) =>
        {
            if (!query.TryGetComponent(uid, out var component))
                return;

            var convArgs = (TEvent)args;
            RaiseLocalEvent(uid, ref convArgs);
        };
    }

    /// <summary>
    ///     Networks the event for every session in PVS range of the given entity,
    ///         for the given entity.
    /// </summary>
    public void NetworkLocalEvent(EntityUid uid, object args)
    {
        var ev = new NetworkedLocalEvent(GetNetEntity(uid), args);
        RaiseNetworkEvent(ev, Filter.Pvs(uid));
    }

    /// <summary>
    ///     Networks the event for every session in the filter, for the given entity.
    /// </summary>
    public void NetworkLocalEvent(EntityUid uid, Filter filter, object args)
    {
        var ev = new NetworkedLocalEvent(GetNetEntity(uid), args);
        RaiseNetworkEvent(ev, filter);
    }
}

[NetSerializable, Serializable]
public sealed class NetworkedLocalEvent(NetEntity targetEntity, object args) : EntityEventArgs
{
    public NetEntity TargetEntity = targetEntity;
    public object Args = args;
}
