using Robust.Shared.Serialization;

namespace Content.Shared._KS14.NPC;

/// <summary>
///     What a squad planner knows to be so. Issued by the server's NpcSquadGoap; shared so the plan debug overlay can draw
///         it.
/// </summary>
[Flags, Serializable, NetSerializable]
public enum NpcSquadFact : uint
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

    /// <summary>
    ///     Something can be thrown in through a way in: nothing stands in it.
    /// </summary>
    ThrowLine = 1 << 6,

    /// <summary>
    ///     Somebody stacked up on a way in is carrying a flashbang.
    /// </summary>
    HasFlashbang = 1 << 7,

    /// <summary>
    ///     A flashbang has gone in, so whoever is in there is in no state to fight.
    /// </summary>
    Softened = 1 << 8,

    /// <summary>
    ///     The hostile can hit the squad and the squad cannot hit it: glass that stops bullets and lets lasers through.
    /// </summary>
    Threatened = 1 << 9,

    /// <summary>
    ///     The squad has got out of the hostile's sight.
    /// </summary>
    OutOfSight = 1 << 10,

    /// <summary>
    ///     Somebody stacked up on a way in has a grenade to put through it, somewhere that leaves the squad out of the blast.
    /// </summary>
    HasFrag = 1 << 11,

    /// <summary>
    ///     Every way out of the hostile's room has somebody on it.
    /// </summary>
    ExitsCovered = 1 << 12,

    /// <summary>
    ///     Nobody hurt has meds it should use before going in. True of a squad with no meds: it can do no better.
    /// </summary>
    Patched = 1 << 13,

    /// <summary>
    ///     Nobody low on ammo has a magazine it should change to before going in. True of a squad with none to spare.
    /// </summary>
    Loaded = 1 << 14,

    /// <summary>
    ///     Somebody carries a charge it can set off.
    /// </summary>
    HasCharge = 1 << 15,

    /// <summary>
    ///     A way in is shut to all of them, and a charge will open it.
    /// </summary>
    DoorChargeable = 1 << 16,

    /// <summary>
    ///     A wall of the hostile's room is one thick, safe to blow, and can be walked up to from outside.
    /// </summary>
    WallChargeable = 1 << 17,

    /// <summary>
    ///     Somebody stacked up carries smoke that would do something to the hostile, and could put it in.
    /// </summary>
    HasSmoke = 1 << 18,
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
    Flashbang,
    EnterSoftened,
    TakeCover,
    FragRoom,
    HoldExits,
    PatchUp,
    TopUp,
    BlowDoor,
    BlowWall,
    SmokeRoom,
}

/// <summary>
///     How far a squad's charge has got.
/// </summary>
[Serializable, NetSerializable]
public enum NpcSquadChargePhase : byte
{
    /// <summary>
    ///     Its planter is on the way to it, or sticking it on.
    /// </summary>
    Planting,

    /// <summary>
    ///     It is on, and everyone is getting clear.
    /// </summary>
    Clearing,

    /// <summary>
    ///     It has gone off, and the blast is working its way out.
    /// </summary>
    Blasted,
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
