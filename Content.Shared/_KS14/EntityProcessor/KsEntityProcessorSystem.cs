using Content.Shared.Power;
using Robust.Shared.Collections;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.EntityProcessor;

public sealed class KsEntityProcessorSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;

    private const string ContainerId = "object-processor-container";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsActiveEntityProcessorComponent, EntRemovedFromContainerMessage>(OnEntRemovedFromActiveContainer);

        SubscribeLocalEvent<KsEntityProcessorComponent, PowerChangedEvent>(OnPowerChanged);

        SubscribeLocalEvent<KsEntityProcessorComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<KsEntityProcessorComponent, StartCollideEvent>(OnStartCollide);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var eqe = EntityQueryEnumerator<KsActiveEntityProcessorComponent, KsEntityProcessorComponent>();
        while (eqe.MoveNext(out var uid, out var activeComponent, out var processorComponent))
        {
            if (!processorComponent.Powered)
                continue;

            var processedUids = new ValueList<EntityUid>();
            foreach (var (processingUid, processingTime) in activeComponent.Processing)
            {
                if (_gameTiming.CurTime < processingTime)
                    continue;

                FinishProcessing((uid, processorComponent, activeComponent), processingUid);
                processedUids.Add(processingUid);
            }

            if (processedUids.Count == 0)
                continue;

            // If we finished processing everything, then just remove the component
            if (processedUids.Count ==
                activeComponent.Processing.Count)
            {
                RemComp(uid, activeComponent);

                var finishedAllEv = new KsFinishedProcessingEverythingEvent();
                RaiseLocalEvent(uid, ref finishedAllEv);

                continue;
            }

            // If not everything's done yet just remove the ones that were finished
            foreach (var processedUid in processedUids)
                activeComponent.Processing.Remove(processedUid);

            Dirty(uid, activeComponent);
        }
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

    private void OnEntRemovedFromActiveContainer(Entity<KsActiveEntityProcessorComponent> entity, ref EntRemovedFromContainerMessage args)
    {
        if (_gameTiming.ApplyingState)
            return;

        if (args.Container.ID != ContainerId)
            return;

        entity.Comp.Processing.Remove(entity);
        Dirty(entity);

        var ev = new KsEntityRemovedFromActiveProcessorEvent(entity, args.Entity);
        RaiseLocalEvent(entity.Owner, ref ev);
    }

    private void OnPowerChanged(Entity<KsEntityProcessorComponent> entity, ref PowerChangedEvent args)
    {
        if (entity.Comp.Powered == args.Powered)
            return;

        entity.Comp.Powered = args.Powered;
        Dirty(entity);
    }

    private void OnStartup(Entity<KsEntityProcessorComponent> entity, ref ComponentStartup args)
        => entity.Comp.Container = _containerSystem.EnsureContainer<Container>(entity.Owner, ContainerId);

    private void OnStartCollide(Entity<KsEntityProcessorComponent> entity, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != entity.Comp.FixtureId)
            return;

        TryStartProcessing(entity!, (args.OtherEntity, args.OtherBody));
    }
}
