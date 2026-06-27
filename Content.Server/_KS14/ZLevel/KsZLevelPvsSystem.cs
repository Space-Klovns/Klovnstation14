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

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        var component = EntityManager.ComponentFactory.GetComponent<KsZLevelViewerComponent>();
        component.Session = args.Player;
        component.Active = false;
        AddComp(args.Entity, component);
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        RemComp<KsZLevelViewerComponent>(args.Entity);
    }

    private void OnViewerShutdown(Entity<KsZLevelViewerComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.ViewSubscriberUid == EntityUid.Invalid)
            return;

        Del(entity.Comp.ViewSubscriberUid);
    }

    private void OnSubscriberShutdown(Entity<KsZLevelViewSubscriberComponent> entity, ref ComponentShutdown args)
    {
        if (Terminating(entity.Owner) ||
            !TryComp<KsZLevelViewerComponent>(entity.Comp.ViewerUid, out var viewerComponent))
            return;

        _viewSubscriberSystem.RemoveViewSubscriber(entity.Owner, viewerComponent.Session);
        viewerComponent.ViewSubscriberUid = EntityUid.Invalid;

        RemComp(entity.Comp.ViewerUid, viewerComponent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameTiming.CurTime < _nextUpdate)
            return;

        _nextUpdate = _gameTiming.CurTime + UpdateInterval;

        var eqe = AllEntityQuery<KsZLevelViewerComponent, TransformComponent>();
        while (eqe.MoveNext(out var viewerUid, out var viewerComponent, out var viewerTransformComponent))
        {
            if (!_zLevelSystem.TryGetZLevel(viewerUid, out var zLevelEntity) ||
                zLevelEntity.Value.Comp.Node.Previous is not { } previousZLevelNode)
            {
                if (viewerComponent.Active)
                    RemoveActiveViewer((viewerUid, viewerComponent));

                continue;
            }

            if (!viewerComponent.Active)
                AddActiveViewer((viewerUid, viewerComponent), viewerComponent.Session);

            if (viewerComponent.ViewSubscriberUid == EntityUid.Invalid)
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

    private void AddActiveViewer(Entity<KsZLevelViewerComponent?> entity, ICommonSession session)
    {
        entity.Comp ??= EnsureComp<KsZLevelViewerComponent>(entity.Owner);

        var subscriberUid = Spawn(null);
        Transform(subscriberUid).GridTraversal = false; // You know exactly where this is from
        _viewSubscriberSystem.AddViewSubscriber(subscriberUid, session);

        entity.Comp.Active = true;
        entity.Comp.ViewSubscriberUid = subscriberUid;
    }

    private void RemoveActiveViewer(Entity<KsZLevelViewerComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, logMissing: false))
            return;

        entity.Comp.Active = false;
        Del(entity.Comp.ViewSubscriberUid);

        entity.Comp.ViewSubscriberUid = EntityUid.Invalid;
    }
}
