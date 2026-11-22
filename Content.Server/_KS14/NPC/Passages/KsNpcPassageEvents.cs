using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Passages;

/// <summary>
///     Raised on a <see cref="Content.Shared._KS14.NPC.Passages.KsNpcPassageComponent"/> entity to ask where taking it
///         leads right now. Asked every tick by each NPC making for it, and by planning: handlers only answer.
/// </summary>
[ByRefEvent]
public record struct KsGetNpcPassageDestinationEvent
{
    /// <summary>
    ///     Where it comes out. Left null, it leads nowhere and no NPC makes for it.
    /// </summary>
    public EntityCoordinates? Destination;
}

/// <summary>
///     Raised on a passage when an NPC making for it has come within its
///         <see cref="Content.Shared._KS14.NPC.Passages.KsNpcPassageComponent.Range"/>, for a handler to have the NPC
///         take it. With no handler the NPC walks on as it was going, which is how a passage that is walked into is
///         taken: a portal needs none.
/// </summary>
[ByRefEvent]
public record struct KsNpcTakePassageEvent(EntityUid NpcUid)
{
    /// <summary>
    ///     Set by the handler that had the NPC take it, or begin to.
    /// </summary>
    public bool Handled;

    /// <summary>
    ///     How long the NPC stands still where it is while it is taken: a climb that takes time.
    /// </summary>
    public TimeSpan Wait;
}
