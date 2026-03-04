using Content.Shared._KS14.Clothing.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Mobs;

namespace Content.Shared._KS14.Clothing.EntitySystems;

/// <summary>
/// Relays events from wearers to their worn clothing with WornRelayEventComponent.
/// This system is only active on entities that have ClothingRelayEventRequiredComponent,
/// which is granted by clothing that needs event relaying.
/// </summary>
public sealed class RelayEventClothingSystem : EntitySystem
{
    public override void Initialize()
    {
        // Only subscribe to events on entities that have the relay requirement marker
        SubscribeLocalEvent<ClothingRelayEventRequiredComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    /// <summary>
    /// Relays MobStateChanged events from the wearer to their worn clothing items.
    /// </summary>
    private void OnMobStateChanged(EntityUid uid, ClothingRelayEventRequiredComponent component, MobStateChangedEvent args)
    {
        RelayEventToWornClothing(uid, args);
    }

    /// <summary>
    /// Finds all worn clothing with WornRelayEventComponent and relays the event to them.
    /// </summary>
    private void RelayEventToWornClothing<T>(EntityUid uid, T args) where T : notnull
    {
        // Get the inventory component to access clothing slots
        if (!TryComp<InventoryComponent>(uid, out var inventory))
            return;

        // Wrap the event in InventoryRelayedEvent for consistency with the inventory relay system
        var wrappedEv = new InventoryRelayedEvent<T>(args, uid);

        // Iterate through inventory containers (all clothing slots)
        foreach (var container in inventory.Containers)
        {
            foreach (var item in container.ContainedEntities)
            {
                // Check if this item has WornRelayEventComponent
                if (!HasComp<WornRelayEventComponent>(item))
                    continue;

                // Raise the wrapped event to this clothing item
                RaiseLocalEvent(item, wrappedEv);

                // If the event was handled, stop propagating
                if (args is HandledEntityEventArgs { Handled: true })
                    return;
            }
        }
    }
}
