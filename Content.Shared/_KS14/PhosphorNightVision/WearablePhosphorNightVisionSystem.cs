using Content.Shared._Mono.Overlays;
using Content.Shared.Inventory.Events;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.PhosphorNightVision;

public sealed partial class WearablePhosphorNightVisionSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WearablePhosphorNightVisionComponent, GotEquippedEvent>(OnGotEquipped);
        SubscribeLocalEvent<WearablePhosphorNightVisionComponent, GotUnequippedEvent>(OnGotUnequipped);
    }

    private void OnGotEquipped(Entity<WearablePhosphorNightVisionComponent> entity, ref GotEquippedEvent args)
    {
        if (_gameTiming.ApplyingState)
            return;

        DebugTools.Assert(HasComp<PhosphorNightVisionComponent>(entity), $"{ToPrettyString(entity)} has {nameof(WearablePhosphorNightVisionComponent)} but no {nameof(PhosphorNightVisionComponent)}");

        var recipientComponent = EntityManager.ComponentFactory.GetComponent<PhosphorNightVisionRecipientComponent>();
        recipientComponent.NightVisionSourceUid = entity;

        AddComp(args.EquipTarget, recipientComponent);
        Dirty(args.EquipTarget, recipientComponent);
    }

    private void OnGotUnequipped(Entity<WearablePhosphorNightVisionComponent> entity, ref GotUnequippedEvent args)
    {
        if (_gameTiming.ApplyingState)
            return;

        DebugTools.Assert(HasComp<PhosphorNightVisionComponent>(entity), $"{ToPrettyString(entity)} has {nameof(WearablePhosphorNightVisionComponent)} but no {nameof(PhosphorNightVisionComponent)}");
        RemComp<PhosphorNightVisionRecipientComponent>(args.EquipTarget);
    }
}
