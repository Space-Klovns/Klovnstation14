using Robust.Shared.Serialization;

namespace Content.Shared._KS14.NPC;

/// <summary>
///     What a squad's tactics have told one member to do. Issued by the server's NpcSquadTacticsSystem; shared so the
///         squad debug overlay can draw it.
/// </summary>
[Serializable, NetSerializable]
public enum NpcOrderKind : byte
{
    None,

    /// <summary>
    ///     Lost a hostile while on the move: carry on to where it should be by now.
    /// </summary>
    Investigate,

    /// <summary>
    ///     Lost a hostile while holding: stay put, facing where it went, for it to show itself again.
    /// </summary>
    Watch,

    /// <summary>
    ///     Get in position outside a way into the room the hostile was lost in, and wait for the rest.
    /// </summary>
    Stage,

    /// <summary>
    ///     Force a door that will not open by hand, with a tool: a crowbar, jaws of life, an access breaker. Then put
    ///         the tool away, weapon back in hand, and wait to go in.
    /// </summary>
    Breach,

    /// <summary>
    ///     Go in, all at once.
    /// </summary>
    Enter,

    /// <summary>
    ///     Check one spot the hostile could be hiding: a locker, a corner, where it was last seen.
    /// </summary>
    Search,

    /// <summary>
    ///     The search came up empty: hold the area a little while before standing down.
    /// </summary>
    HoldArea,

    /// <summary>
    ///     Nothing is going on: get back to the leader.
    /// </summary>
    Regroup,
}

/// <summary>
///     How far along a squad's hunt for a hostile it has lost is.
/// </summary>
[Serializable, NetSerializable]
public enum NpcHuntPhase : byte
{
    /// <summary>
    ///     Just lost: whoever was moving keeps going, whoever was holding waits for it to peek.
    /// </summary>
    Watch,

    /// <summary>
    ///     Members moving up to the ways into the room it was lost in.
    /// </summary>
    Stage,

    /// <summary>
    ///     Members with tools forcing the doors that will not open for anyone by hand, the rest waiting.
    /// </summary>
    Breach,

    /// <summary>
    ///     Members going in together.
    /// </summary>
    Entry,

    /// <summary>
    ///     Members checking every spot it could be hiding.
    /// </summary>
    Search,

    /// <summary>
    ///     Nowhere left to look: holding the area before standing down.
    /// </summary>
    Exhausted,
}
