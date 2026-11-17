using Content.Shared.Inventory;

namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     Asks what lets an NPC see in the dark. Raised on the NPC and relayed to what it wears. Anything that helps raises
///         <see cref="DarkVision"/> to its own strength: the best one counts, as they do not add up.
/// </summary>
[ByRefEvent]
public record struct NpcGetDarkVisionEvent() : IInventoryRelayEvent
{
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;

    /// <summary>
    ///     How much of the dark the NPC sees through, from 0 to 1.
    /// </summary>
    public float DarkVision;
}
