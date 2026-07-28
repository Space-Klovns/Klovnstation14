using Content.Shared._Mono.Overlays;
using Content.Shared._Mono.PhosphorNightVision;
using Content.Shared.Inventory.Events;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.PhosphorNightVision;

public sealed partial class WearablePhosphorNightVisionSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedPhosphorNightVisionSystem _phosphorNightVisionSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WearablePhosphorNightVisionComponent, GotEquippedEvent>(OnGotEquipped);
        SubscribeLocalEvent<WearablePhosphorNightVisionComponent, GotUnequippedEvent>(OnGotUnequipped);
    }

    private void OnGotEquipped(Entity<WearablePhosphorNightVisionComponent> entity, ref GotEquippedEvent args)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return;

        if (TryComp<PhosphorNightVisionRecipientComponent>(args.EquipTarget, out var existingRecipientComponent))
        {
            existingRecipientComponent.SourceUids.Add(entity);
            _phosphorNightVisionSystem.RefreshOverlay(args.EquipTarget, activeNvEntity: (entity, Comp<PhosphorNightVisionComponent>(entity)));

            return;
        }

        if (_gameTiming.ApplyingState)
            return;

        var recipientComponent = EntityManager.ComponentFactory.GetComponent<PhosphorNightVisionRecipientComponent>();
        recipientComponent.SourceUids.Add(entity);

        AddComp(args.EquipTarget, recipientComponent);
        Dirty(args.EquipTarget, recipientComponent);
    }

    private void OnGotUnequipped(Entity<WearablePhosphorNightVisionComponent> entity, ref GotUnequippedEvent args)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return;

        if (TryComp<PhosphorNightVisionRecipientComponent>(args.EquipTarget, out var recipientComponent) &&
            recipientComponent.SourceUids.Remove(entity) &&
            recipientComponent.SourceUids.Count == 0)
        {
            RemComp(args.EquipTarget, recipientComponent);
        }

        _phosphorNightVisionSystem.RefreshOverlay(args.EquipTarget, activeNvEntity: (entity, Comp<PhosphorNightVisionComponent>(entity)));
    }
}
