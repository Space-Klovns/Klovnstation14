using Content.Shared.Body;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.InventoryRequiresOrgan;

// TODO LCDC: optimise somehow

public sealed class InventoryRequiresOrganSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventorySystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InventoryRequiresOrganComponent, IsEquippingTargetAttemptEvent>(OnTargetEquippingAttempt);
        SubscribeLocalEvent<InventoryRequiresOrganComponent, OrganRemovedFromEvent>(OnOrganRemoved);
    }

    /// <returns>True if the inventory slot can continue being active.</returns>
    public static bool ShouldDisableSlot(List<ProtoId<OrganCategoryPrototype>> requiredCategories, Dictionary<ProtoId<OrganCategoryPrototype>, Entity<OrganComponent>> presentOrganCategoryCounts)
    {
        // actually a lie because ALL of the required categories must be missing, for this to cancel

        var missingCategories = 0;
        foreach (var requiredCategory in requiredCategories)
        {
            if (presentOrganCategoryCounts.ContainsKey(requiredCategory))
                continue;

            missingCategories++;
        }

        return missingCategories == requiredCategories.Count;
    }

    private void OnTargetEquippingAttempt(Entity<InventoryRequiresOrganComponent> entity, ref IsEquippingTargetAttemptEvent args)
    {
        if (!entity.Comp.Categories.TryGetValue(args.Slot, out var requiredCategories))
            return;

        if (!TryComp<BodyComponent>(entity, out var bodyComponent))
        {
            args.Cancel();
            return;
        }

        if (ShouldDisableSlot(requiredCategories, bodyComponent.PresentOrganCategories))
        {
            args.Cancel();
            args.Reason = "ks-inventory-component-reason-dismembered";
        }
    }

    private void OnOrganRemoved(Entity<InventoryRequiresOrganComponent> entity, ref OrganRemovedFromEvent args)
    {
        if (TerminatingOrDeleted(entity) ||
            !Transform(entity.Owner).ParentUid.IsValid())
            return;

        foreach (var (slotId, requiredCategories) in entity.Comp.Categories)
        {
            if (!ShouldDisableSlot(requiredCategories, args.BodyComponent.PresentOrganCategories))
                continue;

            _inventorySystem.TryUnequip(entity.Owner, slotId, force: true, predicted: true);
        }
    }
}
