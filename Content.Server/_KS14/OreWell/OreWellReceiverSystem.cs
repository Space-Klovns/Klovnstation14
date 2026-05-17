using Content.Server.Stack;
using Content.Shared._KS14.GenericSpriteFlick;
using Content.Shared._KS14.OreWell;
using Content.Shared.Power;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.OreWell;

public sealed class OreWellReceiverSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly OreWellSystem _oreWellSystem = default!;
    [Dependency] private readonly StackSystem _stackSystem = default!;
    [Dependency] private readonly KsGenericSpriteFlickSystem _spriteFlickSystem = default!;

    private readonly HashSet<Entity<OreWellReceiverComponent>> _activeEntities = [];

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15d);
    private TimeSpan _nextUpdate = TimeSpan.MinValue;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OreWellReceiverComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<OreWellReceiverComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<OreWellReceiverComponent, EntityPausedEvent>(OnPaused);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameTiming.CurTime < _nextUpdate)
            return;

        _nextUpdate = _gameTiming.CurTime - TimeSpan.FromSeconds(frameTime) + Interval;

        var individualGenerated = _oreWellSystem.GenerateResourcesAndTake((float)Interval.TotalSeconds / _activeEntities.Count);
        if (individualGenerated.Count == 0)
            return;

        foreach (var entity in _activeEntities)
        {
            var transformComponent = Transform(entity);
            var spawnCoordinates = new EntityCoordinates(transformComponent.ParentUid, transformComponent.LocalPosition);

            foreach (var (resourceId, amount) in individualGenerated)
            {
                var resource = _prototypeManager.Index(resourceId);
                var resourceUid = Spawn(resource.Spawn, spawnCoordinates);
                var stackComponent = Comp<StackComponent>(resourceUid);

                // Debt: put off what we can't spawn (when left with decimals, or when stack cant get any bigger)
                var paidAmount = amount + entity.Comp.Debt.GetValueOrDefault(resourceId);
                var spawnedAmount = Math.Min((int)paidAmount, _stackSystem.GetMaxCount(stackComponent));

                var debt = paidAmount - spawnedAmount;
                entity.Comp.Debt[resourceId] = debt;

                _stackSystem.SetCount((resourceUid, null), spawnedAmount);
            }

            if (entity.Comp.FlickLayerKey is { } layerKey)
                _spriteFlickSystem.Flick(entity.Owner, layerKey, entity.Comp.FlickState);
        }
    }

    private void OnPowerChanged(Entity<OreWellReceiverComponent> entity, ref PowerChangedEvent args)
    {
        if (args.Powered)
            _activeEntities.Add(entity);
        else
            _activeEntities.Remove(entity);
    }

    private void OnShutdown(Entity<OreWellReceiverComponent> entity, ref ComponentShutdown args)
    {
        _activeEntities.Remove(entity);
    }

    private void OnPaused(Entity<OreWellReceiverComponent> entity, ref EntityPausedEvent args)
    {
        _activeEntities.Remove(entity);
    }
}
