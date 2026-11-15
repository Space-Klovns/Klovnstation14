namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     Who an NPC holds fire for when they are in its line of fire (<see cref="NpcSquadFireLaneSystem"/>). An NPC without
///         this holds fire for friends and not for bystanders: <see cref="NpcFireLaneMode.HoldForFriendlies"/>.
/// </summary>
[RegisterComponent, Access(typeof(NpcSquadFireLaneSystem))]
public sealed partial class NpcFireLaneComponent : Component
{
    [DataField]
    public NpcFireLaneMode Mode = NpcFireLaneMode.HoldForFriendlies;
}

/// <summary>
///     Whether an NPC fires through whoever is in the way, or holds fire for them, and for whom.
/// </summary>
public enum NpcFireLaneMode : byte
{
    /// <summary>
    ///     Fires through anyone in the line, friends included: no fire-lane check at all. A turret or a mob that sees past
    ///         standing mobs only by <c>TargetInLOSPrecondition.ksStandingMobsBlock</c> also sees past friendly ones.
    /// </summary>
    FireThrough,

    /// <summary>
    ///     Holds fire while a friend (faction-friendly, standing, alive) is in the line; a bystander catches the rounds. The
    ///         default.
    /// </summary>
    HoldForFriendlies,

    /// <summary>
    ///     Holds fire for a friend, and for a bystander as well: a standing mob neither friendly nor hostile to it, and not
    ///         its target.
    /// </summary>
    HoldForFriendliesAndNeutrals,
}
