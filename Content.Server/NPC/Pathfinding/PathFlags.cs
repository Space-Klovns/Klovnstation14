namespace Content.Server.NPC.Pathfinding;

[Flags]
public enum PathFlags : byte
{
    None = 0,

    /// <summary>
    /// Do we have any form of access.
    /// </summary>
    Access = 1 << 0,

    /// <summary>
    /// Can we pry airlocks if necessary.
    /// </summary>
    Prying = 1 << 1,

    /// <summary>
    /// Can stuff like walls be broken.
    /// </summary>
    Smashing = 1 << 2,

    /// <summary>
    /// Can we climb it like a table or railing.
    /// </summary>
    Climbing = 1 << 3,

    /// <summary>
    /// Can we open stuff that requires interaction (e.g. click-open doors).
    /// </summary>
    Interact = 1 << 4,

    // KS14 start
    /// <summary>
    ///     Can we push loose things - closets, crates - out of the way. See <c>NpcPushSystem</c>.
    /// </summary>
    Pushing = 1 << 5,

    /// <summary>
    ///     Do we carry something that pries, and use it on doors in our way. Unlike <see cref="Prying"/>, which is an NPC
    ///         prying with its own claws, this is a door it stops to force. See <c>NpcDoorSystem.PriesDoorsInItsWay</c>.
    /// </summary>
    PryingTool = 1 << 6,

    /// <summary>
    ///     With <see cref="Climbing"/>, also climb things that break when climbed onto - glass tables - rather than walk
    ///         round them. See <c>PathfindingBreadcrumbFlag.ClimbBreaks</c>.
    /// </summary>
    ClimbingBreakables = 1 << 7,
    // KS14 end
}
