namespace Content.Server._KS14.NPC.Doors;

/// <summary>
///     What an NPC believes about getting through a door by hand. See <see cref="NpcDoorSystem.GetDoorAccess"/>.
/// </summary>
public enum NpcDoorAccess : byte
{
    /// <summary>
    ///     Not shut: open, opening, or not a door at all.
    /// </summary>
    Open,

    /// <summary>
    ///     Shut, and it believes it can open it: the door asks for no access, or for access it has, and nothing visible
    ///         - bolts, a weld, a dead airlock - says otherwise. It can be wrong: see
    ///         <see cref="NpcDoorSystem.ReportRefused"/>.
    /// </summary>
    Openable,

    /// <summary>
    ///     Shut to it by hand: access it lacks, bolted, welded, unpowered, not a door anyone opens by hand, or one its
    ///         squad has found will not open. A tool may still get it through: see
    ///         <see cref="NpcDoorSystem.TryGetBreachTool"/>.
    /// </summary>
    Locked,
}

/// <summary>
///     How a door is forced. See <see cref="NpcDoorSystem.TryGetBreachTool"/>.
/// </summary>
public enum NpcBreachMethod : byte
{
    None,

    /// <summary>
    ///     A prying tool: a crowbar on an unpowered door, jaws of life on any.
    /// </summary>
    Pry,

    /// <summary>
    ///     An access breaker, which opens a powered airlock however it is locked.
    /// </summary>
    AccessBreaker,
}
