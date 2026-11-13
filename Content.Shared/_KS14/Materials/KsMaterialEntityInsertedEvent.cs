namespace Content.Shared._KS14.Materials;

/// <summary>
///     Raised on a material storage when an entity has been put into it, alongside the upstream event, which says
///         neither who did it nor how much went in.
/// </summary>
/// <param name="Units">How many units of <paramref name="Inserted"/> were taken: all of a stack, or what fitted.</param>
[ByRefEvent]
public readonly record struct KsMaterialEntityInsertedEvent(EntityUid User, EntityUid Inserted, int Units);
