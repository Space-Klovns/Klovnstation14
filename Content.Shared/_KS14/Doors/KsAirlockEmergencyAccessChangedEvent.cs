namespace Content.Shared._KS14.Doors;

/// <summary>
///     Raised on an airlock when its emergency access is switched on or off. Emergency access lets anyone through,
///         whatever the door's access, so whatever treats the door as access-restricted - NPC pathfinding - has to
///         look again.
/// </summary>
[ByRefEvent]
public readonly record struct KsAirlockEmergencyAccessChangedEvent(bool EmergencyAccess);
