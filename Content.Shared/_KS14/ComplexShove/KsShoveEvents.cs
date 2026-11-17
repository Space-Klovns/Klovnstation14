namespace Content.Shared._KS14.ComplexShove;

/// <summary>
///     Raised on a <see cref="KsShoveObstacleComponent"/> when someone is shoved into it, before the default
///         knockdown. Handle it to replace what happens, such as a glass table breaking or a locker swallowing them.
/// </summary>
[ByRefEvent]
public record struct KsShoveCollideEvent(EntityUid Shover, EntityUid Target, EntityUid Obstacle)
{
    public bool Handled;
}

/// <summary>
///     Raised on the shoved entity before a shove is resolved. Handle it to replace the shove entirely, as a grab
///         does to pin its victim.
/// </summary>
[ByRefEvent]
public record struct KsBeforeShoveEvent(EntityUid Shover, EntityUid Target)
{
    public bool Handled;
}

/// <summary>
///     Raised on someone down, about to be shoved, to decide whether the shove can knock things out of their hands.
///     <see cref="Allowed"/> starts from the data: true against a wall (<c>ComplexShoveComponent.DisarmAgainstWalls</c>)
///         or an obstacle with <c>KsShoveObstacleComponent.DisarmsAgainst</c>. Change it to make something else count.
/// </summary>
[ByRefEvent]
public record struct KsShoveDisarmCheckEvent(EntityUid Shover, EntityUid Target, EntityUid? Obstacle, bool Allowed)
{
    public bool Allowed = Allowed;
}

/// <summary>
///     Raised on the shoved entity once a shove has been resolved.
/// </summary>
[ByRefEvent]
public readonly record struct KsShovedEvent(EntityUid Shover, EntityUid Target, KsShoveOutcome Outcome, EntityUid? Obstacle);

public enum KsShoveOutcome : byte
{
    /// <summary>
    ///     Nothing behind them: pushed away and staggered.
    /// </summary>
    Pushed,

    /// <summary>
    ///     Shoved into a <see cref="KsShoveObstacleComponent"/>.
    /// </summary>
    Obstacle,

    /// <summary>
    ///     Shoved against a wall or something else solid.
    /// </summary>
    Blocked,

    /// <summary>
    ///     Already down, and kicked onto their side.
    /// </summary>
    Paralyzed,

    /// <summary>
    ///     Already kicked over, and made to drop everything they hold.
    /// </summary>
    Disarmed,

    /// <summary>
    ///     Something else took the shove, through <see cref="KsBeforeShoveEvent"/>.
    /// </summary>
    Handled,
}
