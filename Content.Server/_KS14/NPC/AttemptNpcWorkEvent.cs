namespace Content.Server._KS14.NPC;

/// <summary>
///     Raised on an entity with an npc component to potentially
///         cancel any planning.
/// </summary>
[ByRefEvent]
public record struct AttemptNpcWorkEvent(bool Cancelled);
