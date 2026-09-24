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
    /// <summary>What is coming down on this entity.</summary>
    public readonly EntityUid CrusherUid = CrusherUid;

    /// <summary>How fast it landed, in z-levels per second.</summary>
    public readonly float ImpactSpeed = ImpactSpeed;

    /// <summary>
    ///     Whether to spare this entity. The only writable member.
    /// </summary>
    /// <remarks>
    ///     One instance is reused for every target of a single landing, so this is reset before each raise -
    ///         see <see cref="KsZLevelPhysicsSystem"/>. Declaring the other two as readonly fields rather than
    ///         leaving them as the positional properties is what makes that reuse safe to rely on.
    /// </remarks>
    public bool Cancelled = false;
}

/// <summary>
///     Raised on an entity that has just been crushed by something landing on top of it.
/// </summary>
[ByRefEvent]
public readonly record struct KsZLevelCrushedEvent(EntityUid CrusherUid, float ImpactSpeed);

/// <summary>
///     Raised to ask whether anything solid sits part way up a z-level's gap, in the path of something
///         falling through it.
/// </summary>
/// <remarks>
///     A z-level's floor plane is the only surface <see cref="KsZLevelPhysicsSystem"/> knows about by itself,
///         which is why a grid crossing the gap on a map of its own - an elevator between two floors - would
///         otherwise be fallen straight through. Rather than teaching the fall about gaps, it asks: anything
///         occupying part of a gap answers this, and a fall lands on whatever answers highest.
///     Raised broadcast, and only for a descent. Subscribers must be pure other than writing the landing.
/// </remarks>
[ByRefEvent]
public record struct KsZLevelTransitObstructionEvent(
    EntityUid ZLevelUid,
    Robust.Shared.Map.MapId MapId,
    System.Numerics.Vector2 WorldPosition,
    float FromHeight,
    float ToHeight)
{
    /// <summary>The z-level being fallen through.</summary>
    public readonly EntityUid ZLevelUid = ZLevelUid;

    /// <summary>That z-level's map, for a subscriber that wants to ask spatial questions of it.</summary>
    public readonly Robust.Shared.Map.MapId MapId = MapId;

    /// <summary>Where the faller is, so a subscriber can check it is actually over the obstruction.</summary>
    public readonly System.Numerics.Vector2 WorldPosition = WorldPosition;

    /// <summary>
    ///     The height the faller started this tick at, as a fraction of the z-level's Depth. Always greater
    ///         than <see cref="ToHeight"/>.
    /// </summary>
    public readonly float FromHeight = FromHeight;

    /// <summary>The height it would reach this tick, in the same units.</summary>
    public readonly float ToHeight = ToHeight;

    /// <summary>
    ///     The map to land on, if anything answered. Left null by anything that does not want the fall.
    /// </summary>
    public EntityUid? LandingMapUid;

    /// <summary>
    ///     How far up the z-level's gap the surface being landed on sits, in the same units as
    ///     <see cref="FromHeight"/>. Highest answer wins, so a fall lands on the topmost of several.
    /// </summary>
    public float LandingHeight;
}
