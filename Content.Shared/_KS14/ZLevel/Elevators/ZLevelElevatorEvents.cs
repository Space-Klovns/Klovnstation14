namespace Content.Shared._KS14.ZLevel.Elevators;

/// <summary>
///     Raised on an elevator grid once it has finished crossing onto a new z-level.
/// </summary>
/// <remarks>
///     Deliberately shaped like <see cref="Physics.KsZLevelChangedEvent"/>, which says the same thing about
///         a falling entity, so a subscriber that cares about "this thing changed z-level" reads the two the
///         same way.
/// </remarks>
[ByRefEvent]
public readonly record struct ZLevelElevatorArrivedEvent(EntityUid OldZLevelUid, EntityUid NewZLevelUid, bool Rising);

/// <summary>
///     Raised on an elevator grid when it has come to a stop at a floor.
/// </summary>
[ByRefEvent]
public readonly record struct ZLevelElevatorStoppedEvent(EntityUid ZLevelUid);

/// <summary>
///     Raised on an elevator grid just before it leaves a floor.
/// </summary>
[ByRefEvent]
public readonly record struct ZLevelElevatorDepartingEvent(EntityUid ZLevelUid, bool Rising);
