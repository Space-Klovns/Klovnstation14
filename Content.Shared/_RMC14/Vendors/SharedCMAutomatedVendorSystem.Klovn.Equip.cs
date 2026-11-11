using System.Numerics;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Vendors;

/// <summary>
///     Dresses a player in everything a vend hands out at once, rather than item by item in the order a kit happens to
///         list them. Clothing goes on first, each piece in the most specific free slot it fits, in as many passes as
///         slots that depend on others (an ID on a jumpsuit) need. What is left goes into worn storage it fits, then
///         into the hands, and only then on the floor.
/// </summary>
public abstract partial class SharedCMAutomatedVendorSystem
{
    /// <summary>
    ///     Catch-all slots, tried only after every slot an item fits more specifically. A PDA belongs on the ID slot
    ///         even though it would also hang from a belt.
    /// </summary>
    private const SlotFlags KsCatchAllSlots = SlotFlags.BELT | SlotFlags.POCKET | SlotFlags.SUITSTORAGE;

    /// <summary>
    ///     Worn storage that loose items are packed into, in order: a belt takes what it is made for (magazines,
    ///         tools), and the bag everything else.
    /// </summary>
    private static readonly SlotFlags[] KsPackingSlots = [SlotFlags.BELT, SlotFlags.BACK, SlotFlags.OUTERCLOTHING];

    /// <summary>
    ///     Spawns every item of one vend, then places them all together.
    /// </summary>
    private void KsVendAll(EntityUid vendorUid, EntityUid playerUid, IReadOnlyList<EntProtoId> protoIds, Vector2 offset, SlotFlags? replaceSlot)
    {
        var spawned = new List<EntityUid>(protoIds.Count);
        foreach (var protoId in protoIds)
        {
            var spawnUid = PredictedSpawnNextToOrDrop(protoId, vendorUid);
            PrepareVended(spawnUid);
            spawned.Add(spawnUid);
        }

        var placed = KsPlaceAll(playerUid, spawned, replaceSlot);

        foreach (var spawnUid in spawned)
        {
            // Merged into a stack already being carried.
            if (TerminatingOrDeleted(spawnUid))
                continue;

            if (!placed.Contains(spawnUid) && TryComp(spawnUid, out TransformComponent? transformComponent))
                _transform.SetLocalPosition(spawnUid, transformComponent.LocalPosition + offset, transformComponent);

            var ev = new RMCAutomatedVendedUserEvent(spawnUid);
            RaiseLocalEvent(playerUid, ref ev);
        }
    }

    /// <summary>
    ///     Puts on, packs away or picks up as many of <paramref name="itemUids"/> as the player has room for, and
    ///         returns those placed.
    /// </summary>
    private HashSet<EntityUid> KsPlaceAll(EntityUid playerUid, List<EntityUid> itemUids, SlotFlags? replaceSlot)
    {
        var placed = new HashSet<EntityUid>();
        var batch = new HashSet<EntityUid>(itemUids);

        if (TryComp<InventoryComponent>(playerUid, out var inventoryComponent))
        {
            // Pieces with the fewest places to go claim them first, so that one that could go elsewhere never takes
            //      the only slot another fits. Stable, so equal pieces keep the kit's order.
            var clothing = new List<(EntityUid Uid, ClothingComponent Clothing, int Options)>();
            foreach (var itemUid in itemUids)
            {
                if (!HasComp<ItemComponent>(itemUid) || !TryComp<ClothingComponent>(itemUid, out var clothingComponent))
                    continue;

                clothing.Add((itemUid, clothingComponent, KsCountSlots(playerUid, clothingComponent.Slots)));
            }

            clothing.Sort((a, b) => a.Options.CompareTo(b.Options));

            // Repeated while anything goes on, since a slot may only open up once what it depends on is worn.
            bool progress;
            do
            {
                progress = false;
                foreach (var (itemUid, clothingComponent, _) in clothing)
                {
                    if (placed.Contains(itemUid) ||
                        !KsTryWear((playerUid, inventoryComponent), itemUid, clothingComponent, replaceSlot, batch))
                        continue;

                    placed.Add(itemUid);
                    progress = true;
                }
            } while (progress);
        }

        foreach (var itemUid in itemUids)
        {
            if (placed.Contains(itemUid) || !HasComp<ItemComponent>(itemUid))
                continue;

            if (KsTryPack(playerUid, itemUid) || _hands.TryPickupAnyHand(playerUid, itemUid))
                placed.Add(itemUid);
        }

        return placed;
    }

    /// <summary>
    ///     Puts a piece of clothing on: into the most specific free slot it fits, or in place of what the player
    ///         wears in a slot the vend replaces. Never displaces anything handed out by the same vend.
    /// </summary>
    private bool KsTryWear(Entity<InventoryComponent> player,
        EntityUid itemUid,
        ClothingComponent clothingComponent,
        SlotFlags? replaceSlot,
        HashSet<EntityUid> batch)
    {
        foreach (var catchAll in new[] { false, true })
        {
            var slots = _inventory.GetSlotEnumerator(player.AsNullable(), clothingComponent.Slots);
            while (slots.MoveNext(out var slot, out var slotDefinition))
            {
                if (((slotDefinition.SlotFlags & KsCatchAllSlots) != 0) != catchAll || slot.ContainedEntity != null)
                    continue;

                if (_inventory.TryEquip(player, itemUid, slot.ID, silent: true, predicted: true, inventory: player.Comp, clothing: clothingComponent))
                    return true;
            }
        }

        if (replaceSlot is not { } replaceFlags)
            return false;

        var replaceSlots = _inventory.GetSlotEnumerator(player.AsNullable(), clothingComponent.Slots & replaceFlags);
        while (replaceSlots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } wornUid ||
                batch.Contains(wornUid) ||
                !_inventory.CanUnequip(player, slot.ID, out _, containerSlot: slot, inventory: player.Comp) ||
                !_containerSystem.CanRemove(wornUid, slot))
                continue;

            if (!_inventory.TryUnequip(player, slot.ID, out var removedUid, silent: true, force: true, predicted: true, inventory: player.Comp))
                continue;

            if (!_inventory.TryEquip(player, itemUid, slot.ID, silent: true, predicted: true, inventory: player.Comp, clothing: clothingComponent))
            {
                // Put back what it was meant to replace.
                _inventory.TryEquip(player, removedUid.Value, slot.ID, silent: true, force: true, predicted: true, inventory: player.Comp);
                continue;
            }

            // Whatever the old one held moves into the new one.
            if (TryComp<StorageComponent>(removedUid, out var sourceStorageComponent) &&
                TryComp<StorageComponent>(itemUid, out var targetStorageComponent))
            {
                _storage.TransferEntities(removedUid.Value, itemUid, sourceComp: sourceStorageComponent, targetComp: targetStorageComponent);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    ///     Packs an item into the first worn storage that takes it.
    /// </summary>
    private bool KsTryPack(EntityUid playerUid, EntityUid itemUid)
    {
        foreach (var packingSlot in KsPackingSlots)
        {
            var slots = _inventory.GetSlotEnumerator(playerUid, packingSlot);
            while (slots.MoveNext(out var slot))
            {
                if (slot.ContainedEntity is not { } storageUid ||
                    !TryComp<StorageComponent>(storageUid, out var storageComponent) ||
                    !_storage.CanInsert(storageUid, itemUid, out _, storageComponent))
                    continue;

                if (_storage.Insert(storageUid, itemUid, out _, user: playerUid, storageComp: storageComponent, playSound: false))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     What every vended item gets before it is placed: it doesn't collide with the vendor or the player, and
    ///         can be picked up from where it lies.
    /// </summary>
    private void PrepareVended(EntityUid spawnUid)
    {
        var recentlyVendedComponent = EnsureComp<RMCRecentlyVendedComponent>(spawnUid);
        Dirty(spawnUid, recentlyVendedComponent);

        var wallMountComponent = EnsureComp<Content.Shared.Wall.WallMountComponent>(spawnUid);
        wallMountComponent.Arc = Angle.FromDegrees(360);
        Dirty(spawnUid, wallMountComponent);
    }

    private int KsCountSlots(EntityUid playerUid, SlotFlags slotFlags)
    {
        var count = 0;
        var slots = _inventory.GetSlotEnumerator(playerUid, slotFlags);
        while (slots.MoveNext(out _))
            count++;

        return count;
    }
}
