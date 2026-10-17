using Content.Shared.Power;
using Robust.Shared.Collections;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.EntityProcessor;

public sealed partial class KsEntityProcessorSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;

    private const string ContainerId = "object-processor-container";

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var eqe = EntityQueryEnumerator<KsActiveEntityProcessorComponent, KsEntityProcessorComponent>();
        while (eqe.MoveNext(out var uid, out var activeComponent, out var processorComponent))
        {
            // Everything was taken out (OnEntRemovedFromActiveContainer leaves the component for us to tidy,
            //      so a new item inserted before this point reuses it instead of a dying component)
            if (activeComponent.Processing.Count == 0)
            {
                FinishEverything((uid, activeComponent));
                continue;
            }

            if (!processorComponent.Powered)
                continue;

            // Collected first: a finish handler may take things out of the processor, changing Processing
            var dueUids = new ValueList<EntityUid>();
            foreach (var (processingUid, processingTime) in activeComponent.Processing)
            {
                if (_gameTiming.CurTime >= processingTime)
                    dueUids.Add(processingUid);
            }

            if (dueUids.Count == 0)
                continue;

            foreach (var dueUid in dueUids)
            {
                if (!activeComponent.Processing.ContainsKey(dueUid))
                    continue; // taken out by an earlier finish handler

                FinishProcessing((uid, processorComponent, activeComponent), dueUid);
                activeComponent.Processing.Remove(dueUid);
            }

            // Nothing left, so remove the component. Not by the container handler: this is the one place that does
            if (activeComponent.Processing.Count == 0)
            {
                FinishEverything((uid, activeComponent));
                continue;
            }

            Dirty(uid, activeComponent);
        }
    }

    private void FinishEverything(Entity<KsActiveEntityProcessorComponent> entity)
    {
        RemComp(entity.Owner, entity.Comp);

        var finishedAllEv = new KsFinishedProcessingEverythingEvent();
        RaiseLocalEvent(entity.Owner, ref finishedAllEv);
    }

    private void FinishProcessing(in Entity<KsEntityProcessorComponent, KsActiveEntityProcessorComponent?> processorEntity, EntityUid processedUid)
    {
        // TODO LCDC: debug this and check if its raised on client, i think it isnt

        var ev = new KsFinishedProcessingEntityEvent(processorEntity, processedUid);
        RaiseLocalEvent(processorEntity, ref ev);
    }

    /// <summary>
    ///     Starts processing an entity, or atleast tries to.
    /// </summary>
    public bool TryStartProcessing(Entity<KsEntityProcessorComponent?> processorEntity, Entity<PhysicsComponent?> processedEntity)
    {
        if (!Resolve(processorEntity.Owner, ref processorEntity.Comp) ||
            !processorEntity.Comp.Powered)
            return false;

        var attemptEv = new KsAttemptProcessEntityEvent(false, processorEntity!, processedEntity, _gameTiming.CurTime);
        RaiseLocalEvent(processorEntity, ref attemptEv);

        var processorTransformComponent = Transform(processorEntity.Owner);

        if (attemptEv.Cancelled ||
            !_containerSystem.CanInsert(processedEntity.Owner, processorEntity.Comp.Container, containerXform: processorTransformComponent))
            return false;

        var startEv = new KsStartedProcessingEntityEvent((processorEntity.Owner, processorEntity.Comp, processorTransformComponent), processedEntity);
        RaiseLocalEvent(processorEntity, ref startEv);

        _containerSystem.Insert(
            (processedEntity, null, null, processedEntity),
            processorEntity.Comp.Container,
            containerXform: processorTransformComponent,
            force: true /* already checked Caninsert */
        );

        if (attemptEv.ProcessingFinishTime == _gameTiming.CurTime)
        {
            // Immediately process
            FinishProcessing(processorEntity!, processedEntity);

            if (!HasComp<KsActiveEntityProcessorComponent>(processorEntity))
            {
                var finishedAllEv = new KsFinishedProcessingEverythingEvent();
                RaiseLocalEvent(processorEntity, ref finishedAllEv);
            }
        }
        else
        {
            var activeComponent = EnsureComp<KsActiveEntityProcessorComponent>(processorEntity);
            activeComponent.Processing[processedEntity] = attemptEv.ProcessingFinishTime;
            Dirty(processorEntity!.Owner, activeComponent);
        }

        return true;
    }

    [SubscribeLocalEvent]
    private void OnEntRemovedFromActiveContainer(Entity<KsActiveEntityProcessorComponent> entity, ref EntRemovedFromContainerMessage args)
    {
        if (_gameTiming.ApplyingState)
            return;

        if (args.Container.ID != ContainerId)
            return;

        // The thing that left, not the processor: else it would still be "processing" and get consumed when its time is up
        entity.Comp.Processing.Remove(args.Entity);
        Dirty(entity);

        var ev = new KsEntityRemovedFromActiveProcessorEvent(entity, args.Entity);
        RaiseLocalEvent(entity.Owner, ref ev);
    }

    [SubscribeLocalEvent]
    private void OnPowerChanged(Entity<KsEntityProcessorComponent> entity, ref PowerChangedEvent args)
    {
        if (entity.Comp.Powered == args.Powered)
            return;

        entity.Comp.Powered = args.Powered;
        Dirty(entity);
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsEntityProcessorComponent> entity, ref ComponentStartup args)
        => entity.Comp.Container = _containerSystem.EnsureContainer<Container>(entity.Owner, ContainerId);

    [SubscribeLocalEvent]
    private void OnStartCollide(Entity<KsEntityProcessorComponent> entity, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != entity.Comp.FixtureId)
            return;

        TryStartProcessing(entity!, (args.OtherEntity, args.OtherBody));
    }
}
