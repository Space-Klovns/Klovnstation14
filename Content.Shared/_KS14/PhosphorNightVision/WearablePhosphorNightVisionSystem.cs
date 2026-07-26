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
        if (_gameTiming.ApplyingState ||
            HasComp<PhosphorNightVisionRecipientComponent>(args.EquipTarget))
            return;

        var recipientComponent = EntityManager.ComponentFactory.GetComponent<PhosphorNightVisionRecipientComponent>();
        recipientComponent.NightVisionSourceUid = entity;

        AddComp(args.EquipTarget, recipientComponent);
        Dirty(args.EquipTarget, recipientComponent);

        _phosphorNightVisionSystem.RefreshOverlay(args.EquipTarget, activeNvEntity: (entity, Comp<PhosphorNightVisionComponent>(entity)));
    }

    private void OnGotUnequipped(Entity<WearablePhosphorNightVisionComponent> entity, ref GotUnequippedEvent args)
    {
        RemComp<PhosphorNightVisionRecipientComponent>(args.EquipTarget);
        _phosphorNightVisionSystem.RefreshOverlay(args.EquipTarget, activeNvEntity: (entity, Comp<PhosphorNightVisionComponent>(entity)));
    }
}
