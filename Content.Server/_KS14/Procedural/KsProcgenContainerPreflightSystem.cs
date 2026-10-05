using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Containers;

namespace Content.Server._KS14.Procedural;

public enum KsProcgenContainerPreflightStatus : byte
{
    Eligible,
    Rejected,
    InvalidInput,
}

/// <summary>
/// Permission at the time of this check, not an insertion, capacity reservation or placement proof.
/// Engine attempt events may run handlers. Call again at commit; do not cache eligibility.
/// </summary>
public sealed record KsProcgenContainerPreflight(
    KsProcgenContainerPreflightStatus Status, string Reason, bool UsesItemSlot, int? DeclaredCapacity)
{
    public bool InsertionVerified => false;
}

/// <summary>
/// Live preflight for future staged InContainer relations. Never creates containers, swaps existing
/// contents, inserts/reparents entities or bypasses engine filters and attempt events.
/// </summary>
public sealed partial class KsProcgenContainerPreflightSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;

    public KsProcgenContainerPreflight Check(EntityUid subjectUid, EntityUid targetUid, string containerId)
    {
        if (string.IsNullOrWhiteSpace(containerId) || containerId.Length > 256)
            return new(KsProcgenContainerPreflightStatus.InvalidInput, "InvalidContainerId", false, null);
        if (!Ready(subjectUid) || !Ready(targetUid))
            return new(KsProcgenContainerPreflightStatus.InvalidInput, "ContainerEntityNotReady", false, null);
        if (subjectUid == targetUid)
            return new(KsProcgenContainerPreflightStatus.Rejected, "ContainerSelfInsertion", false, null);
        if (_containerSystem.TryGetContainingContainer(subjectUid, out _))
            return new(KsProcgenContainerPreflightStatus.Rejected, "ContainerSubjectAlreadyContained", false, null);

        // A slot declaration must use the item-slot API; falling through would bypass its filters.
        if (TryComp<ItemSlotsComponent>(targetUid, out var itemSlotsComponent) &&
            _itemSlotsSystem.TryGetSlot(targetUid, containerId, out var slot, component: itemSlotsComponent))
        {
            if (slot.ContainerSlot == null ||
                !_containerSystem.TryGetContainer(targetUid, containerId, out var actualContainer) ||
                !ReferenceEquals(slot.ContainerSlot, actualContainer))
                return new(KsProcgenContainerPreflightStatus.Rejected, "ItemSlotContainerMismatch", true, 1);
            if (slot.Locked)
                return new(KsProcgenContainerPreflightStatus.Rejected, "ItemSlotLocked", true, 1);
            if (slot.HasItem || slot.ContainerSlot.ExpectedEntities.Count > 0)
                return new(KsProcgenContainerPreflightStatus.Rejected, "ContainerSlotOccupied", true, 1);
            var eligible = _itemSlotsSystem.CanInsert(targetUid, subjectUid, user: null, slot: slot, swap: false);
            return new(eligible ? KsProcgenContainerPreflightStatus.Eligible : KsProcgenContainerPreflightStatus.Rejected,
                eligible ? "ContainerInsertionEligible" : "ItemSlotInsertionDenied", true, 1);
        }

        if (!_containerSystem.TryGetContainer(targetUid, containerId, out var container))
            return new(KsProcgenContainerPreflightStatus.Rejected, "MissingLiveContainer", false, null);
        int? capacity = container is ContainerSlot ? 1 : null;
        if (capacity == 1 && (container.ContainedEntities.Count > 0 || container.ExpectedEntities.Count > 0))
            return new(KsProcgenContainerPreflightStatus.Rejected, "ContainerSlotOccupied", false, capacity);
        var canInsert = _containerSystem.CanInsert(subjectUid, container, assumeEmpty: false);
        return new(canInsert ? KsProcgenContainerPreflightStatus.Eligible : KsProcgenContainerPreflightStatus.Rejected,
            canInsert ? "ContainerInsertionEligible" : "ContainerInsertionDenied", false, capacity);
    }

    private bool Ready(EntityUid entityUid) =>
        TryComp<MetaDataComponent>(entityUid, out var metadataComponent) &&
        metadataComponent.EntityLifeStage is >= EntityLifeStage.Initialized and < EntityLifeStage.Terminating &&
        !EntityManager.IsQueuedForDeletion(entityUid);
}
