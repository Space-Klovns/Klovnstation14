using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ZLevel;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.ZLevel;

public sealed partial class KsZLevelPvsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;
    [Dependency] private ViewSubscriberSystem _viewSubscriberSystem = default!;

    private TimeSpan _updateInterval = TimeSpan.FromSeconds(0.25d);
    private TimeSpan _nextUpdate = TimeSpan.MinValue;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(
            _configurationManager,
            KsCCVars.ZLevelPvsUpdateInterval,
            value => _updateInterval = TimeSpan.FromSeconds(value),
            true
        );
    }

    [SubscribeLocalEvent]
    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        var component = EntityManager.ComponentFactory.GetComponent<KsZLevelViewerComponent>();
        component.Session = args.Player;
        component.Active = false;
        AddComp(args.Entity, component);
    }

    [SubscribeLocalEvent]
    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        RemComp<KsZLevelViewerComponent>(args.Entity);
    }

    [SubscribeLocalEvent]
    private void OnViewerShutdown(Entity<KsZLevelViewerComponent> entity, ref ComponentShutdown args)
    {
        // The subscriber may already be gone - its z-level deleted out from under it, say - and deleting a
        //      dead uid logs an error.
        if (entity.Comp.ViewSubscriberUid == EntityUid.Invalid ||
            TerminatingOrDeleted(entity.Comp.ViewSubscriberUid))
            return;

        Del(entity.Comp.ViewSubscriberUid);
    }

    [SubscribeLocalEvent]
    private void OnSubscriberShutdown(Entity<KsZLevelViewSubscriberComponent> entity, ref ComponentShutdown args)
    {
        if (!TryComp<KsZLevelViewerComponent>(entity.Comp.ViewerUid, out var viewerComponent) ||
            viewerComponent.ViewSubscriberUid != entity.Owner)
            return;

        // Deleting the entity takes its subscriptions with it, so only unsubscribe while it is still alive.
        if (!Terminating(entity.Owner))
            _viewSubscriberSystem.RemoveViewSubscriber(entity.Owner, viewerComponent.Session);

        // Clear the now-dangling reference rather than removing the viewer component. Update spawns a fresh
        //      subscriber next tick; removing the component instead would cost the player z-level visibility
        //      for the rest of the round, and would recurse straight back into OnViewerShutdown.
        viewerComponent.Active = false;
        viewerComponent.ViewSubscriberUid = EntityUid.Invalid;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameTiming.CurTime < _nextUpdate)
            return;

        _nextUpdate = _gameTiming.CurTime + _updateInterval;

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

        // Without this the shutdown hook below never fires, and a subscriber deleted by anything other than us
        //      - its z-level being deleted, most likely - leaves the viewer pointing at a dead entity forever.
        var subscriberComponent = EnsureComp<KsZLevelViewSubscriberComponent>(subscriberUid);
        subscriberComponent.ViewerUid = entity.Owner;

        _viewSubscriberSystem.AddViewSubscriber(subscriberUid, session);

        entity.Comp.Active = true;
        entity.Comp.ViewSubscriberUid = subscriberUid;
    }

    private void RemoveActiveViewer(Entity<KsZLevelViewerComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, logMissing: false))
            return;

        entity.Comp.Active = false;

        if (entity.Comp.ViewSubscriberUid != EntityUid.Invalid &&
            !TerminatingOrDeleted(entity.Comp.ViewSubscriberUid))
            Del(entity.Comp.ViewSubscriberUid);

        entity.Comp.ViewSubscriberUid = EntityUid.Invalid;
    }
}
