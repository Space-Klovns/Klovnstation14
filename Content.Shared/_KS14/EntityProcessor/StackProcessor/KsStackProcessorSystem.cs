using Content.Shared.Stacks;
using Robust.Shared.Network;
using Robust.Shared.Physics.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.EntityProcessor.StackProcessor;

public sealed class KsStackProcessorSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedStackSystem _stackSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsStackProcessorComponent, KsAttemptProcessEntityEvent>(OnAttemptProcess);
        SubscribeLocalEvent<KsStackProcessorComponent, KsStartedProcessingEntityEvent>(OnStartedProcessing);
        SubscribeLocalEvent<KsStackProcessorComponent, KsFinishedProcessingEntityEvent>(OnFinishedProcessing);
        SubscribeLocalEvent<KsStackProcessorComponent, KsEntityRemovedFromActiveProcessorEvent>(OnEntityRemovedFromProcessor);
        SubscribeLocalEvent<KsStackProcessorComponent, KsFinishedProcessingEverythingEvent>(OnFinishedProcessingEverything);
    }

    private void OnAttemptProcess(Entity<KsStackProcessorComponent> entity, ref KsAttemptProcessEntityEvent args)
    {
        if (args.Cancelled)
            return;

        if (TryComp<StackComponent>(args.ProcessedUid, out var stackComponent) &&
            entity.Comp.Conversions.ContainsKey(stackComponent.StackTypeId))
        {
            args.ProcessingFinishTime += entity.Comp.ProcessingTime;
            return;
        }

        args.Cancelled = true;
    }

    private void OnStartedProcessing(Entity<KsStackProcessorComponent> entity, ref KsStartedProcessingEntityEvent args)
    {
        _appearanceSystem.SetData(entity.Owner, KsStackProcessorVisuals.Active, true);

        var delta = _transformSystem.GetWorldPosition(args.ProcessedUid) - _transformSystem.GetWorldPosition(args.ProcessorEntity.Comp2);
        entity.Comp.OutputOffsets[args.ProcessedUid] = -delta;
        Dirty(entity);
    }

    private void OnFinishedProcessing(Entity<KsStackProcessorComponent> entity, ref KsFinishedProcessingEntityEvent args)
    {
        if (_netManager.IsClient)
            return;

        PredictedQueueDel(args.ProcessedUid);
        if (!TryComp<StackComponent>(args.ProcessedUid, out var stackComponent) ||
            !entity.Comp.Conversions.TryGetValue(stackComponent.StackTypeId, out var convertedProto))
            return;

        var spawnedCount = (int)(stackComponent.Count * entity.Comp.Multiplier);
        if (spawnedCount < 0)
            return;

        var maxCount = _prototypeManager.Index(stackComponent.StackTypeId).MaxCount ?? int.MaxValue;

        var spawnCoordinates = _transformSystem.GetMoverCoordinates(entity.Owner);
        spawnCoordinates = spawnCoordinates.WithPosition(spawnCoordinates.Position + entity.Comp.OutputOffsets[args.ProcessedUid]);

        if (spawnedCount <= maxCount)
        {
            var convertedUid = PredictedSpawnAtPosition(convertedProto, spawnCoordinates);
            _stackSystem.SetCount((convertedUid, null), spawnedCount);
        }
        else
        {
            // Keep spawning until everything is spawned

            for (var i = spawnedCount / maxCount; i > 0; i--)
                _stackSystem.SetCount((PredictedSpawnAtPosition(convertedProto, spawnCoordinates), null), maxCount);

            _stackSystem.SetCount((PredictedSpawnAtPosition(convertedProto, spawnCoordinates), null), spawnedCount % maxCount);
        }
    }

    private void OnEntityRemovedFromProcessor(Entity<KsStackProcessorComponent> entity, ref KsEntityRemovedFromActiveProcessorEvent args)
    {
        if (!entity.Comp.OutputOffsets.Remove(args.ProcessedUid))
            return;

        Dirty(entity);
    }

    private void OnFinishedProcessingEverything(Entity<KsStackProcessorComponent> entity, ref KsFinishedProcessingEverythingEvent args)
    {
        _appearanceSystem.SetData(entity.Owner, KsStackProcessorVisuals.Active, false);
    }
}
