using System.Numerics;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.RadarInterest;

/// <summary>
///     Sends clients the things that should be shown on radar and will probably never locally move.
/// </summary>
/// <remarks>
///     Everything is keyed by <see cref="NetEntity"/>, not <see cref="EntityUid"/>: the removal of a deleted interest
///         reaches a client after the entity is already gone there, and could not be resolved to a uid any more.
/// </remarks>
public sealed partial class KsRadarInterestSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;

    private readonly Dictionary<NetEntity, KsStaticRadarInterest> _staticInterests = [];

    /// <summary>
    ///     Every interest, keyed by the interest entity.
    /// </summary>
    public IReadOnlyDictionary<NetEntity, KsStaticRadarInterest> StaticInterests => _staticInterests;

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

    public override void Shutdown()
    {
        base.Shutdown();

        if (_netManager.IsServer)
            _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public void AddInterest(EntityUid uid, KsRadarInterestData data)
    {
        if (HasComp<KsRadarInterestComponent>(uid))
            return;

        var component = Factory.GetComponent<KsRadarInterestComponent>();
        component.Data = data;

        AddComp(uid, component);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.InGame)
            return;

        RaiseNetworkEvent(new KsStaticRadarInterestResetMessage(new(_staticInterests)), args.Session);
    }

    private void OnInterestParentChanged(Entity<KsRadarInterestComponent> entity, ref EntParentChangedMessage args)
    {
        if (TerminatingOrDeleted(entity)) // shutdown handles this
            return;

        SetInterest(entity, args.Transform);
    }

    private void OnInterestStartup(Entity<KsRadarInterestComponent> entity, ref ComponentStartup args)
    {
        entity.Comp.Data.Text ??= MetaData(entity.Owner).EntityName;

        SetInterest(entity, Transform(entity));
    }

    private void SetInterest(Entity<KsRadarInterestComponent> entity, TransformComponent transformComponent)
    {
        var interestNetEntity = GetNetEntity(entity.Owner);
        var interest = new KsStaticRadarInterest(entity.Comp.Data, GetNetEntity(transformComponent.ParentUid), transformComponent.LocalPosition);

        _staticInterests[interestNetEntity] = interest;
        RaiseNetworkEvent(new KsStaticRadarInterestDeltaMessage(interestNetEntity, interest));
    }

    private void OnInterestShutdown(Entity<KsRadarInterestComponent> entity, ref ComponentShutdown args)
    {
        var interestNetEntity = GetNetEntity(entity.Owner);

        _staticInterests.Remove(interestNetEntity);
        RaiseNetworkEvent(new KsStaticRadarInterestDeltaMessage(interestNetEntity, null));
    }

    private void OnResetMessage(KsStaticRadarInterestResetMessage msg)
    {
        _staticInterests.Clear();
        foreach (var (interestNetEntity, interest) in msg.AllInterests)
            _staticInterests[interestNetEntity] = interest;
    }

    private void OnDeltaMessage(KsStaticRadarInterestDeltaMessage msg)
    {
        if (msg.Interest is { } interest)
            _staticInterests[msg.InterestEntity] = interest;
        else
            _staticInterests.Remove(msg.InterestEntity);
    }
}

/// <param name="Owner">The entity <paramref name="Position"/> is relative to: the grid or map the interest is on.</param>
[Serializable, NetSerializable]
public readonly record struct KsStaticRadarInterest(KsRadarInterestData Data, NetEntity Owner, Vector2 Position);

[Serializable, NetSerializable]
public sealed class KsStaticRadarInterestResetMessage(Dictionary<NetEntity, KsStaticRadarInterest> allInterests) : EntityEventArgs
{
    public Dictionary<NetEntity, KsStaticRadarInterest> AllInterests = allInterests;
}

/// <param name="Interest">Null if the interest was removed.</param>
[Serializable, NetSerializable]
public sealed class KsStaticRadarInterestDeltaMessage(NetEntity interestEntity, KsStaticRadarInterest? interest) : EntityEventArgs
{
    public NetEntity InterestEntity = interestEntity;
    public KsStaticRadarInterest? Interest = interest;
}
