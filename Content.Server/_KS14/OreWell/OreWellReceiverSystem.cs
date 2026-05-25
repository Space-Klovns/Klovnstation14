using Content.Server.Stack;
using Content.Shared._KS14.GenericSpriteFlick;
using Content.Shared._KS14.OreWell;
using Content.Shared.Interaction;
using Content.Shared.Power;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
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
    [Dependency] private readonly AudioSystem _audioSystem = default!;
    [Dependency] private readonly AppearanceSystem _appearanceSystem = default!;

    private readonly HashSet<Entity<OreWellReceiverComponent>> _activeEntities = [];

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30d);
    private TimeSpan _nextUpdate = TimeSpan.MinValue;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OreWellReceiverComponent, ActivateInWorldEvent>(OnActivateInWorld);

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

        foreach (var entity in _activeEntities)
        {
            if (individualGenerated.Count == 0 &&
                entity.Comp.Debt.Count == 0)
                continue;

            var transformComponent = Transform(entity);
            var spawnCoordinates = new EntityCoordinates(transformComponent.ParentUid, transformComponent.LocalPosition);

            var spawnedAnything = false;

            foreach (var (resourceId, amount) in individualGenerated)
            {
                var resource = _prototypeManager.Index(resourceId);

                // Try to pay off debt as best we can
                var paidAmount = amount + entity.Comp.Debt.GetValueOrDefault(resourceId);
                var spawnedAmount = (int)Math.Min(paidAmount, resource.MaxCount ?? int.MaxValue);

                // If we couldn't spawn anything, well fuck (can occur if we have between a 0-1 fractional paidAmount)
                if (spawnedAmount <= 0)
                    continue;

                // Debt: put off what we can't spawn (when left with decimals, or when stack cant get any bigger)
                var debt = paidAmount - spawnedAmount;
                if (debt > 0)
                    entity.Comp.Debt[resourceId] = debt;
                else
                    entity.Comp.Debt.Remove(resourceId);

                var resourceUid = Spawn(resource.Spawn, spawnCoordinates);
                _stackSystem.SetCount((resourceUid, null), spawnedAmount);

                spawnedAnything = true;
            }

            if (!spawnedAnything)
                continue;

            if (entity.Comp.FlickLayerKey is { } layerKey)
                _spriteFlickSystem.Flick(entity.Owner, layerKey, entity.Comp.FlickState);

            if (entity.Comp.Sound is { } soundSpecifier)
                _audioSystem.PlayPvs(soundSpecifier, spawnCoordinates);
        }
    }

    private void SetActive(Entity<OreWellReceiverComponent> entity, bool active)
    {
        if (active)
        {
            if (_activeEntities.Contains(entity))
                return;
        }
        else if (!_activeEntities.Contains(entity))
            return;

        if (active)
        {
            _activeEntities.Add(entity);
            _audioSystem.PlayPvs(entity.Comp.EnableSound, entity.Owner);
        }
        else
        {
            _activeEntities.Remove(entity);
            _audioSystem.PlayPvs(entity.Comp.DisableSound, entity.Owner);
        }

        _appearanceSystem.SetData(entity.Owner, OreWellReceiverVisuals.Active, active);
    }

    private void OnActivateInWorld(Entity<OreWellReceiverComponent> entity, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        entity.Comp.Enabled = !entity.Comp.Enabled;

        if (entity.Comp.Powered)
            SetActive(entity, entity.Comp.Enabled);
    }

    private void OnPowerChanged(Entity<OreWellReceiverComponent> entity, ref PowerChangedEvent args)
    {
        if (entity.Comp.Powered == args.Powered)
            return;

        entity.Comp.Powered = args.Powered;

        if (args.Powered &&
            entity.Comp.Enabled)
            SetActive(entity, true);
        else
            SetActive(entity, false);
    }

    private void OnShutdown(Entity<OreWellReceiverComponent> entity, ref ComponentShutdown args)
    {
        SetActive(entity, false);
    }

    private void OnPaused(Entity<OreWellReceiverComponent> entity, ref EntityPausedEvent args)
    {
        SetActive(entity, false);
    }
}
