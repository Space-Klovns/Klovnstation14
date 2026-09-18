using System.Numerics;
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
    private bool _sendAbove;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(
            _configurationManager,
            KsCCVars.ZLevelPvsUpdateInterval,
            value => _updateInterval = TimeSpan.FromSeconds(value),
            true
        );

        Subs.CVar(_configurationManager, KsCCVars.ZLevelPvsSendAbove, value => _sendAbove = value, true);
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
        DeleteSubscriber(entity.Comp.ViewSubscriberUid);
        DeleteSubscriber(entity.Comp.AboveViewSubscriberUid);
    }

    private void DeleteSubscriber(EntityUid subscriberUid)
    {
        if (subscriberUid == EntityUid.Invalid || TerminatingOrDeleted(subscriberUid))
            return;

        Del(subscriberUid);
    }

    [SubscribeLocalEvent]
    private void OnSubscriberShutdown(Entity<KsZLevelViewSubscriberComponent> entity, ref ComponentShutdown args)
    {
        if (!TryComp<KsZLevelViewerComponent>(entity.Comp.ViewerUid, out var viewerComponent))
            return;

        var slotUid = entity.Comp.Above
            ? viewerComponent.AboveViewSubscriberUid
            : viewerComponent.ViewSubscriberUid;

        if (slotUid != entity.Owner)
            return;

        // Deleting the entity takes its subscriptions with it, so only unsubscribe while it is still alive.
        if (!Terminating(entity.Owner))
            _viewSubscriberSystem.RemoveViewSubscriber(entity.Owner, viewerComponent.Session);

        // Clear the now-dangling reference rather than removing the viewer component. Update spawns a fresh
        //      subscriber next tick; removing the component instead would cost the player z-level visibility
        //      for the rest of the round, and would recurse straight back into OnViewerShutdown.
        if (entity.Comp.Above)
        {
            viewerComponent.AboveActive = false;
            viewerComponent.AboveViewSubscriberUid = EntityUid.Invalid;
            return;
        }

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
            // Off the stack entirely, so neither of them has anything to point at.
            if (!_zLevelSystem.TryGetZLevel(viewerUid, out var zLevelEntity))
            {
                if (viewerComponent.Active)
                    RemoveActiveViewer((viewerUid, viewerComponent));

                if (viewerComponent.AboveActive)
                    RemoveAboveViewer((viewerUid, viewerComponent));

                continue;
            }

            var position = _transformSystem.GetWorldPosition(viewerTransformComponent);

            // Handled before the one below and independently of it: somebody standing on the bottom z-level
            //      of a stack has nothing under them and can still have a lit one over their head.
            UpdateAboveViewer((viewerUid, viewerComponent), zLevelEntity.Value, position);

            if (zLevelEntity.Value.Comp.Node.Previous is not { } previousZLevelNode)
            {
                if (viewerComponent.Active)
                    RemoveActiveViewer((viewerUid, viewerComponent));

                continue;
            }

            if (!viewerComponent.Active)
                AddActiveViewer((viewerUid, viewerComponent), viewerComponent.Session);

            if (viewerComponent.ViewSubscriberUid == EntityUid.Invalid)
                continue;

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

    /// <summary>
    ///     Keeps the second subscriber, the one loading the z-level above, in step with the viewer.
    /// </summary>
    /// <remarks>
    ///     Nothing up there is ever drawn - the stack renders downwards only - so this exists purely so that
    ///         the z-level above has lights and occluders for its light map to be built from, which is what
    ///         lets light fall onto the viewer from it. Off unless asked for, because it is a whole extra
    ///         z-level of entities per player.
    /// </remarks>
    private void UpdateAboveViewer(
        Entity<KsZLevelViewerComponent> entity,
        Entity<KsZLevelComponent> zLevelEntity,
        Vector2 position)
    {
        if (!_sendAbove || !_zLevelSystem.TryGetZLevelAbove(zLevelEntity.Owner, out var aboveEntity))
        {
            if (entity.Comp.AboveActive)
                RemoveAboveViewer(entity);

            return;
        }

        if (!entity.Comp.AboveActive)
            AddAboveViewer(entity);

        if (entity.Comp.AboveViewSubscriberUid == EntityUid.Invalid ||
            !TryComp<MapComponent>(aboveEntity.Value.Owner, out var aboveMapComponent))
            return;

        _transformSystem.SetMapCoordinates(
            entity.Comp.AboveViewSubscriberUid,
            new MapCoordinates(position, aboveMapComponent.MapId)
        );
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

    private void AddAboveViewer(Entity<KsZLevelViewerComponent> entity)
    {
        var subscriberUid = Spawn(null);
        Transform(subscriberUid).GridTraversal = false;

        var subscriberComponent = EnsureComp<KsZLevelViewSubscriberComponent>(subscriberUid);
        subscriberComponent.ViewerUid = entity.Owner;
        subscriberComponent.Above = true;

        _viewSubscriberSystem.AddViewSubscriber(subscriberUid, entity.Comp.Session);

        // Loud on purpose, and only on the transition. This is the one thing about the feature that is
        //      invisible from the client - there is no way to tell "the z-level above was never sent" apart
        //      from "it was sent and is unlit" by looking at it.
        Log.Info($"Now sending the z-level above {ToPrettyString(entity.Owner)} to {entity.Comp.Session.Name}.");

        entity.Comp.AboveActive = true;
        entity.Comp.AboveViewSubscriberUid = subscriberUid;
    }

    private void RemoveAboveViewer(Entity<KsZLevelViewerComponent> entity)
    {
        Log.Info($"No longer sending the z-level above {ToPrettyString(entity.Owner)} to {entity.Comp.Session.Name}.");

        entity.Comp.AboveActive = false;

        DeleteSubscriber(entity.Comp.AboveViewSubscriberUid);
        entity.Comp.AboveViewSubscriberUid = EntityUid.Invalid;
    }
}
