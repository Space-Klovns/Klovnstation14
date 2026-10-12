using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Portal;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Portal;

/// <summary>
///     Sends players near a view portal the area around the portal it is linked to, by view-subscribing their
///         session to a dedicated viewer entity at the linked portal. The viewer's eye has a reduced PVS scale,
///         which is what keeps the cost down.
/// </summary>
/// <remarks>
///     A portal with more than one link uses the first, as <see cref="LinkedEntitySystem.GetLink"/> does.
/// </remarks>
public sealed partial class KsPortalPvsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private LinkedEntitySystem _linkedEntitySystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;
    [Dependency] private SharedEyeSystem _eyeSystem = default!;
    [Dependency] private ViewSubscriberSystem _viewSubscriberSystem = default!;
    [Dependency] private EntityQuery<KsPortalViewSubscriptionsComponent> _subscriptionsQuery = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5d);

    private TimeSpan _nextUpdate = TimeSpan.Zero;
    private float _pvsRange;
    private float _viewPvsScale = 0.5f;

    private readonly List<(ICommonSession Session, MapCoordinates Coordinates)> _viewers = new();
    private readonly HashSet<ICommonSession> _wanted = new();
    private readonly List<ICommonSession> _scratch = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.PortalPvsRange, value => _pvsRange = value, true);
        Subs.CVar(_configurationManager, KsCCVars.PortalViewPvsScale, value => _viewPvsScale = value, true);
    }

    [SubscribeLocalEvent]
    private void OnViewShutdown(Entity<KsPortalViewComponent> entity, ref ComponentShutdown args)
    {
        RemCompDeferred<KsPortalViewSubscriptionsComponent>(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnSubscriptionsShutdown(Entity<KsPortalViewSubscriptionsComponent> entity, ref ComponentShutdown args)
    {
        UnsubscribeAll(entity.Comp);
        DeleteViewer(entity.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameTiming.CurTime < _nextUpdate)
            return;

        _nextUpdate = _gameTiming.CurTime + UpdateInterval;

        _viewers.Clear();
        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { } attachedUid || TerminatingOrDeleted(attachedUid))
                continue;

            var coordinates = _transformSystem.GetMapCoordinates(attachedUid);
            if (coordinates.MapId == MapId.Nullspace)
                continue;

            _viewers.Add((session, coordinates));
        }

        var query = EntityQueryEnumerator<KsPortalViewComponent>();
        while (query.MoveNext(out var portalUid, out var viewComponent))
        {
            _wanted.Clear();
            var targetUid = EntityUid.Invalid;

            if (viewComponent.Enabled
                && _linkedEntitySystem.GetLink(portalUid, out var linkedUid)
                && !TerminatingOrDeleted(linkedUid.Value))
            {
                targetUid = linkedUid.Value;

                var portalCoordinates = _transformSystem.GetMapCoordinates(portalUid);
                foreach (var (session, coordinates) in _viewers)
                {
                    if (coordinates.MapId == portalCoordinates.MapId
                        && (coordinates.Position - portalCoordinates.Position).Length() <= _pvsRange)
                        _wanted.Add(session);
                }
            }

            Apply(portalUid, targetUid);
        }
    }

    private void Apply(EntityUid portalUid, EntityUid targetUid)
    {
        if (!_subscriptionsQuery.TryComp(portalUid, out var subscriptionsComponent))
        {
            if (_wanted.Count == 0)
                return;

            subscriptionsComponent = EnsureComp<KsPortalViewSubscriptionsComponent>(portalUid);
        }

        // Linked to something else now (or nothing): everything on the old target goes.
        if (subscriptionsComponent.TargetUid != targetUid)
        {
            UnsubscribeAll(subscriptionsComponent);
            DeleteViewer(subscriptionsComponent);
            subscriptionsComponent.TargetUid = targetUid;
        }

        if (targetUid == EntityUid.Invalid)
            return;

        _scratch.Clear();
        foreach (var session in subscriptionsComponent.Sessions)
        {
            if (!_wanted.Contains(session))
                _scratch.Add(session);
        }

        Unsubscribe(subscriptionsComponent, _scratch);

        // Nobody near enough: no viewer either, so an idle portal costs nothing.
        if (_wanted.Count == 0)
        {
            DeleteViewer(subscriptionsComponent);
            return;
        }

        if (subscriptionsComponent.ViewerUid == EntityUid.Invalid || TerminatingOrDeleted(subscriptionsComponent.ViewerUid))
        {
            // Sessions subscribed to a dead viewer are not subscribed to anything any more.
            subscriptionsComponent.Sessions.Clear();
            subscriptionsComponent.ViewerUid = SpawnViewer(targetUid);
        }

        foreach (var session in _wanted)
        {
            if (subscriptionsComponent.Sessions.Add(session))
                _viewSubscriberSystem.AddViewSubscriber(subscriptionsComponent.ViewerUid, session);
        }
    }

    private EntityUid SpawnViewer(EntityUid targetUid)
    {
        var viewerUid = Spawn(null, Transform(targetUid).Coordinates);
        _transformSystem.SetParent(viewerUid, targetUid);

        // Only the PVS range of the eye is wanted; the entity itself never needs to be seen.
        EnsureComp<EyeComponent>(viewerUid);
        _eyeSystem.SetPvsScale(viewerUid, _viewPvsScale);

        return viewerUid;
    }

    private void DeleteViewer(KsPortalViewSubscriptionsComponent component)
    {
        if (component.ViewerUid != EntityUid.Invalid)
            QueueDel(component.ViewerUid);

        component.ViewerUid = EntityUid.Invalid;
    }

    /// <summary>
    ///     Takes the given sessions off the component's viewer. Not for the component's own set, which this
    ///         changes: use <see cref="UnsubscribeAll"/> for that.
    /// </summary>
    private void Unsubscribe(KsPortalViewSubscriptionsComponent component, List<ICommonSession> sessions)
    {
        foreach (var session in sessions)
        {
            component.Sessions.Remove(session);
            RemoveViewSubscriber(component, session);
        }
    }

    private void UnsubscribeAll(KsPortalViewSubscriptionsComponent component)
    {
        foreach (var session in component.Sessions)
            RemoveViewSubscriber(component, session);

        component.Sessions.Clear();
    }

    private void RemoveViewSubscriber(KsPortalViewSubscriptionsComponent component, ICommonSession session)
    {
        if (component.ViewerUid != EntityUid.Invalid && !TerminatingOrDeleted(component.ViewerUid))
            _viewSubscriberSystem.RemoveViewSubscriber(component.ViewerUid, session);
    }
}
