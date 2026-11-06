using Robust.Shared.Serialization;

namespace Content.Shared._KS14.NPC;

/// <summary>
///     What a squad planner knows to be so. Issued by the server's NpcSquadGoap; shared so the plan debug overlay can draw
///         it.
/// </summary>
[Flags, Serializable, NetSerializable]
public enum NpcSquadFact : ushort
{
    None = 0,

    /// <summary>
    ///     Somebody can hit the hostile: what every plan is for.
    /// </summary>
    HasShot = 1 << 0,

    /// <summary>
    ///     There is a way into the hostile's room that somebody can reach round the outside of it.
    /// </summary>
    HasWayIn = 1 << 1,

    /// <summary>
    ///     A way in stands open, or opens for whoever leads it.
    /// </summary>
    WayOpen = 1 << 2,

    /// <summary>
    ///     A way in is shut, and whoever leads it has something to force it with.
    /// </summary>
    DoorForceable = 1 << 3,

    /// <summary>
    ///     Members have stacked up on their ways in.
    /// </summary>
    Stacked = 1 << 4,

    /// <summary>
    ///     What stops a member's shot is glass it could shoot out.
    /// </summary>
    GlassBreakable = 1 << 5,
}

/// <summary>
///     What a squad planner can have a squad do.
/// </summary>
[Serializable, NetSerializable]
public enum NpcSquadAction : byte
{
    StackUp,
    ForceDoor,
    Enter,
    BreakGlass,
}

/// <summary>
///     Where a squad's plan has got to.
/// </summary>
[Serializable, NetSerializable]
public enum NpcSquadPlanState : byte
{
    /// <summary>
    ///     Nobody has a shot, but that may only be for a moment.
    /// </summary>
    Grace,

    /// <summary>
    ///     Finding out whether there is a way to it after all.
    /// </summary>
    Probing,

    /// <summary>
    ///     Carrying out a plan.
    /// </summary>
    Running,

    /// <summary>
    ///     Finished, for better or worse, and not planning again for a while.
    /// </summary>
    Cooldown,
}
