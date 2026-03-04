using Content.Shared._KS14.Clothing.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.Implants;
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
    [Dependency] private readonly InventorySystem _inventory = default!;

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
        RelayEventToWornClothing<MobStateChangedEvent>(uid, args);
    }

    /// <summary>
    /// Finds all worn clothing with WornRelayEventComponent and relays the event to them.
    /// Only iterates through inventory containers, not special ones like implants.
    /// </summary>
    private void RelayEventToWornClothing<T>(EntityUid uid, T args) where T : notnull
    {
        // Get the inventory component to access clothing slots
        if (!TryComp<InventoryComponent>(uid, out var inventory))
            return;

        // Iterate through inventory containers (clothing slot containers)
        // This avoids iterating through special containers like implants
        foreach (var container in inventory.Containers)
        {
            foreach (var item in container.ContainedEntities)
            {
                // Check if this item has WornRelayEventComponent
                if (!HasComp<WornRelayEventComponent>(item))
                    continue;

                // Also check if it has ClothingComponent (should always be true in inventory, but verify)
                if (!HasComp<ClothingComponent>(item))
                    continue;

                // Relay the event to this clothing item
                var relayEv = new ClothingRelayEvent<T>(args, uid);
                RaiseLocalEvent(item, relayEv);

                // If the event was handled, stop propagating
                if (args is HandledEntityEventArgs { Handled: true })
                    return;
            }
        }
    }
}

/// <summary>
/// Wrapper for relaying events from the wearer to the clothing.
/// </summary>
public sealed class ClothingRelayEvent<T> where T : notnull
{
    public readonly T Event;

    public readonly EntityUid WearerEntity;

    public ClothingRelayEvent(T ev, EntityUid wearerEntity)
    {
        Event = ev;
        WearerEntity = wearerEntity;
    }
}
