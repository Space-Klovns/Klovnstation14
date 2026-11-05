using Content.Shared.Inventory;

namespace Content.Shared._KS14.Flash;

/// <summary>
///     Raised on an entity, and on what it wears, to ask whether a flash could reach it. A pure question: a handler only
///         answers it, by setting <see cref="Cancelled"/>, and never changes anything, as it can be asked for a flash that
///         never happens. Anything responding to a real flash belongs on <see cref="Content.Shared.Flash.FlashAttemptEvent"/>.
/// </summary>
/// <param name="FlashOverride">Whether this flash pierces flash protection.</param>
[ByRefEvent]
public record struct KsCanFlashEvent(bool FlashOverride = false) : IInventoryRelayEvent
{
    public bool Cancelled;

    SlotFlags IInventoryRelayEvent.TargetSlots => SlotFlags.HEAD | SlotFlags.EYES | SlotFlags.MASK;
}
