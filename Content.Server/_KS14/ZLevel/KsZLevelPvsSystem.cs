using Content.Shared._KS14.ZLevel;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.ZLevel;

public sealed class KsZLevelPvsSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly ViewSubscriberSystem _viewSubscriberSystem = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1d);
    private TimeSpan _nextUpdate = TimeSpan.MinValue;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);

        SubscribeLocalEvent<KsZLevelViewerComponent, ComponentShutdown>(OnViewerShutdown);

        SubscribeLocalEvent<KsZLevelViewSubscriberComponent, ComponentShutdown>(OnSubscriberShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameTiming.CurTime < _nextUpdate)
            return;

        _nextUpdate = _gameTiming.CurTime + UpdateInterval;

        var eqe = EntityQueryEnumerator<KsZLevelViewerComponent, TransformComponent>();
        while (eqe.MoveNext(out var viewerUid, out var viewerComponent, out var viewerTransformComponent))
        {
            if (viewerComponent.ViewSubscriberUid == EntityUid.Invalid ||
                !_zLevelSystem.TryGetStackFromDescendant(viewerUid, out var zLevelEntity, out _) ||
                zLevelEntity.Comp.Node.Previous is not { } previousZLevelNode)
                continue;

            var position = _transformSystem.GetWorldPosition(viewerTransformComponent);

            // lol
            _transformSystem.SetMapCoordinates(
                viewerComponent.ViewSubscriberUid,
                new MapCoordinates(
                    position,
                    Comp<MapComponent>(previousZLevelNode.Value).MapId
                )
            );
        }
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        AddViewer(args.Entity, args.Player);
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        RemoveViewer(args.Entity);
    }

    private void OnViewerShutdown(Entity<KsZLevelViewerComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.ViewSubscriberUid == EntityUid.Invalid)
            return;

        Del(entity.Comp.ViewSubscriberUid);
    }

    private void OnSubscriberShutdown(Entity<KsZLevelViewSubscriberComponent> entity, ref ComponentShutdown args)
    {
        if (!TryComp<KsZLevelViewerComponent>(entity.Comp.ViewerUid, out var viewerComponent))
            return;

        _viewSubscriberSystem.RemoveViewSubscriber(entity.Owner, viewerComponent.Session);
        viewerComponent.ViewSubscriberUid = EntityUid.Invalid;

        RemComp(entity.Comp.ViewerUid, viewerComponent);
    }

    private void AddViewer(EntityUid uid, ICommonSession session)
    {
        var subscriberUid = Spawn(null);
        Transform(subscriberUid).GridTraversal = false; // You know exactly where this is from
        _viewSubscriberSystem.AddViewSubscriber(subscriberUid, session);

        var component = EntityManager.ComponentFactory.GetComponent<KsZLevelViewerComponent>();
        component.Session = session;
        component.ViewSubscriberUid = subscriberUid;

        AddComp(uid, component);
    }

    private void RemoveViewer(EntityUid uid)
    {
        // By extension also cleans up viewsubscriber
        RemComp<KsZLevelViewerComponent>(uid);
    }
}
