using Content.Shared._KS14.CCVar;

namespace Content.Shared._KS14.ZLevel.Physics;

/*
    KsZLevelTransitComponent's own ComponentStartup/ComponentShutdown are not usable for this: only one system
        may subscribe to a given component and event pair, and KsZLevelPhysicsSystem already takes both to keep
        the self-movement block in sync. These re-broadcast them so anything else can hook a transit starting
        and ending - the client-side sprite lift being the reason they exist.
*/

/// <summary>
///     Raised on an entity when it starts moving vertically between z-levels.
/// </summary>
[ByRefEvent]
public readonly record struct KsZLevelTransitStartedEvent;

/// <summary>
///     Raised on an entity when it stops moving vertically between z-levels, for any reason.
///     <see cref="KsZLevelTransitComponent"/> is still attached while this is raised.
/// </summary>
[ByRefEvent]
public readonly record struct KsZLevelTransitEndedEvent;

/// <summary>
///     Raised on a transiting entity that has just moved from one z-level to another.
/// </summary>
[ByRefEvent]
public readonly record struct KsZLevelChangedEvent(EntityUid OldZLevelUid, EntityUid NewZLevelUid, bool Rising);

/// <summary>
///     Raised on a transiting entity to decide whether it is blocked from moving itself while in transit.
///     Subscribers must be pure other than writing <see cref="Blocked"/>.
/// </summary>
[ByRefEvent]
public record struct KsZLevelTransitMoveAttemptEvent(bool Blocked)
{
    /// <summary>
    ///     Whether the entity is blocked from moving itself. Defaults to <see langword="true"/>; set it to
    ///         <see langword="false"/> to let the entity move under its own power mid-transit.
    /// </summary>
    public bool Blocked = Blocked;
}

/// <summary>
///     Raised on an entity that has reached the floor plane of a z-level, to decide whether that impact
///         counts as a landing with or without damage.
///     Subscribers must be pure other than writing <see cref="Damaging"/>: this is a query raised before
///         anything about the impact has been applied, not a notification.
/// </summary>
[ByRefEvent]
public record struct KsZLevelLandAttemptEvent(float ImpactSpeed, bool Damaging)
{
    /// <summary>
    ///     Whether this landing deals impact damage. Pre-filled from
    ///         <see cref="KsCCVars.ZLevelTransitImpactVelocity"/>.
    /// </summary>
    public bool Damaging = Damaging;
}

/// <summary>
///     Raised on an entity after a landing has been resolved. Free to have side effects.
/// </summary>
[ByRefEvent]
public readonly record struct KsZLevelLandEvent(float ImpactSpeed, bool Damaged);

/// <summary>
///     Raised on an entity about to be crushed by something solid landing on top of it, to give it the chance
///         not to be.
///     Subscribers must be pure other than cancelling.
/// </summary>
[ByRefEvent]
public record struct KsZLevelCrushAttemptEvent(EntityUid CrusherUid, float ImpactSpeed)
{
    public bool Cancelled = false;
}

/// <summary>
///     Raised on an entity that has just been crushed by something landing on top of it.
/// </summary>
[ByRefEvent]
public readonly record struct KsZLevelCrushedEvent(EntityUid CrusherUid, float ImpactSpeed);
