using System.Diagnostics.CodeAnalysis;
using Content.Server.Hands.Systems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Containers;

namespace Content.Server._KS14.NPC.Hands;

/// <summary>
///     What an NPC took out with <see cref="NpcHandsSystem.TryTakeOut"/>: the item, where it came from, and what it had
///         in hand before - all that <see cref="NpcHandsSystem.PutBack"/> needs to put things as they were.
/// </summary>
/// <param name="ItemUid">What was taken out.</param>
/// <param name="SourceUid">What it was in: the NPC itself for a worn slot, or a storage like a belt. Null if it was
///     already in hand.</param>
/// <param name="SourceContainerId">The container on <paramref name="SourceUid"/>: the slot's name, for a worn
///     slot.</param>
/// <param name="PreviousActiveHand">The hand that was active before, to switch back to.</param>
/// <param name="WieldedUid">What was wielded before, to wield again.</param>
public readonly record struct NpcTakenOut(
    EntityUid ItemUid,
    EntityUid? SourceUid,
    string? SourceContainerId,
    string? PreviousActiveHand,
    EntityUid? WieldedUid);

/// <summary>
///     The one way an NPC takes something out to use it - a tool, a medkit - and puts it back after.
///     <para>
///         A hand holding a virtual item counts as free. Wielding fills the other hand with one, and for an NPC that
///             hand is there to be used: taking it ends the wield. The virtual item is dropped rather than left to the
///             wield to delete, because that deletion is queued to the end of the tick, and a hand still holding it
///             until then is a hand nothing can be put in now. Unwielding and then looking for an empty hand finds
///             none, which is how an NPC ends up wielding and unwielding every tick without ever drawing anything.
///     </para>
/// </summary>
public sealed partial class NpcHandsSystem : EntitySystem
{
    [Dependency] private HandsSystem _handsSystem = default!;
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedStorageSystem _storageSystem = default!;
    [Dependency] private SharedWieldableSystem _wieldableSystem = default!;

    [Dependency] private EntityQuery<StorageComponent> _storageQuery = default!;
    [Dependency] private EntityQuery<VirtualItemComponent> _virtualItemQuery = default!;
    [Dependency] private EntityQuery<WieldableComponent> _wieldableQuery = default!;

    /// <summary>
    ///     Whether <paramref name="npcUid"/> could take <paramref name="itemUid"/> out right now: it holds it already,
    ///         or has a hand free for it (see <see cref="NpcHandsSystem"/> for what counts as free). Changes nothing.
    /// </summary>
    public bool CanTakeOut(EntityUid npcUid, EntityUid itemUid)
    {
        return _handsSystem.IsHolding(npcUid, itemUid) || FindFreeHand(npcUid) != null;
    }

    /// <summary>
    ///     Takes <paramref name="itemUid"/> - from a hand, a worn slot, or something worn - into a free hand, and makes
    ///         that hand active. Whatever was wielded is unwielded if the hand is needed. Put it all back with
    ///         <see cref="PutBack"/>, however the use goes.
    /// </summary>
    public bool TryTakeOut(EntityUid npcUid, EntityUid itemUid, out NpcTakenOut takenOut)
    {
        var previousActiveHand = _handsSystem.GetActiveHand(npcUid);
        var wieldedUid = GetWielded(npcUid);
        takenOut = new NpcTakenOut(itemUid, null, null, previousActiveHand, wieldedUid);

        if (_handsSystem.IsHolding(npcUid, itemUid, out var heldHand))
            return MakeActive(npcUid, heldHand);

        if (!TryFreeHand(npcUid, out var freeHand))
            return false;

        if (_containerSystem.TryGetContainingContainer(itemUid, out var container))
            takenOut = takenOut with { SourceUid = container.Owner, SourceContainerId = container.ID };

        return _handsSystem.TryPickup(npcUid, itemUid, freeHand, animate: false) && MakeActive(npcUid, freeHand);
    }

    /// <summary>
    ///     Puts back what <see cref="TryTakeOut"/> took out - where it came from, or failing that into anything worn
    ///         with room for it, or failing that on the floor - then switches back to the hand that was active, and
    ///         wields what was wielded again.
    /// </summary>
    public void PutBack(EntityUid npcUid, NpcTakenOut takenOut)
    {
        var itemUid = takenOut.ItemUid;

        // Only something it took from somewhere: an item that was in hand all along stays there.
        if (takenOut.SourceUid is { } sourceUid && Exists(itemUid) && _handsSystem.IsHolding(npcUid, itemUid))
        {
            var stowed = takenOut.SourceContainerId is { } containerId &&
                (sourceUid == npcUid
                    ? _inventorySystem.TryEquip(npcUid, itemUid, containerId, silent: true, force: true)
                    : _storageQuery.TryComp(sourceUid, out var sourceStorageComponent) &&
                      _storageSystem.Insert(sourceUid, itemUid, out _, user: npcUid, storageComp: sourceStorageComponent, playSound: false));

            if (!stowed)
            {
                foreach (var wornUid in _inventorySystem.GetHandOrInventoryEntities(npcUid, SlotFlags.All))
                {
                    if (wornUid == itemUid || !_storageQuery.TryComp(wornUid, out var wornStorageComponent))
                        continue;

                    if (_storageSystem.Insert(wornUid, itemUid, out _, user: npcUid, storageComp: wornStorageComponent, playSound: false))
                    {
                        stowed = true;
                        break;
                    }
                }
            }

            if (!stowed)
                _handsSystem.TryDrop(npcUid, itemUid, checkActionBlocker: false);
        }

        if (takenOut.PreviousActiveHand is { } previousHand)
            MakeActive(npcUid, previousHand);

        if (takenOut.WieldedUid is { } wieldedUid &&
            _handsSystem.IsHolding(npcUid, wieldedUid) &&
            _wieldableQuery.TryComp(wieldedUid, out var wieldableComponent) &&
            !wieldableComponent.Wielded)
            _wieldableSystem.TryWield((wieldedUid, wieldableComponent), npcUid);
    }

    /// <summary>
    ///     A hand <paramref name="npcUid"/> can put something in right now: an empty one, or failing that one holding a
    ///         virtual item, which is dropped to empty it - ending the wield it belongs to. See
    ///         <see cref="NpcHandsSystem"/>.
    /// </summary>
    public bool TryFreeHand(EntityUid npcUid, [NotNullWhen(true)] out string? hand)
    {
        hand = FindFreeHand(npcUid);
        if (hand == null)
            return false;

        if (_handsSystem.TryGetHeldItem(npcUid, hand, out var heldUid) &&
            !_handsSystem.TryDrop(npcUid, hand, checkActionBlocker: false, doDropInteraction: false))
            return false;

        return !_handsSystem.TryGetHeldItem(npcUid, hand, out heldUid);
    }

    /// <summary>
    ///     Makes <paramref name="hand"/> the active one. Unlike <c>TrySetActiveHand</c>, a hand already active counts.
    /// </summary>
    public bool MakeActive(EntityUid npcUid, string hand)
    {
        return _handsSystem.GetActiveHand(npcUid) == hand || _handsSystem.TrySetActiveHand(npcUid, hand);
    }

    /// <summary>
    ///     An empty hand, or failing that one holding only a virtual item. Changes nothing.
    /// </summary>
    private string? FindFreeHand(EntityUid npcUid)
    {
        string? virtualHand = null;

        foreach (var hand in _handsSystem.EnumerateHands(npcUid))
        {
            if (!_handsSystem.TryGetHeldItem(npcUid, hand, out var heldUid))
                return hand;

            if (virtualHand == null && _virtualItemQuery.HasComp(heldUid))
                virtualHand = hand;
        }

        return virtualHand;
    }

    private EntityUid? GetWielded(EntityUid npcUid)
    {
        foreach (var heldUid in _handsSystem.EnumerateHeld(npcUid))
        {
            if (_wieldableQuery.TryComp(heldUid, out var wieldableComponent) && wieldableComponent.Wielded)
                return heldUid;
        }

        return null;
    }
}
