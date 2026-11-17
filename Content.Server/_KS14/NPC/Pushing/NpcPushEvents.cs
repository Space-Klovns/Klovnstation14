namespace Content.Server._KS14.NPC.Pushing;

/// <summary>
///     Raised on something loose in an NPC's way, to ask whether it can be pushed out of the way. A pure question: a
///         handler only answers it, by setting <see cref="Cancelled"/>, and never changes anything. Asked twice:
///     <list type="bullet">
///         <item>when the navmesh is built, with no <see cref="PusherUid"/>: whether anyone could push it, which marks
///             its tile as one paths may cross by pushing (<see cref="Shared.NPC.PathfindingBreadcrumbFlag.Pushable"/>).
///             The navmesh is not rebuilt when only the answer changes, so a handler whose answer can change on its
///             own should dirty the tile itself;</item>
///         <item>when an NPC walks up to it, with <see cref="PusherUid"/> set: whether that NPC can. If not, it goes
///             round it for a while instead.</item>
///     </list>
///     Only loose, non-static bodies are asked about: anything anchored is never pushable.
/// </summary>
[ByRefEvent]
public record struct NpcPushableAttemptEvent(EntityUid? PusherUid)
{
    public bool Cancelled;
}

/// <summary>
///     Raised on something loose in an NPC's way, which it has walked up to and is about to push out of the way. A
///         handler that moves it some other way sets <see cref="Handled"/>, and the NPC waits for its tile to clear.
///         Unhandled, the NPC shoves it, the same as a player shoving it, which needs combat mode that can disarm and
///         is held back by the shove's cooldown and its chance to fail.
/// </summary>
[ByRefEvent]
public record struct NpcPushObstacleEvent(EntityUid PusherUid)
{
    public bool Handled;
}
