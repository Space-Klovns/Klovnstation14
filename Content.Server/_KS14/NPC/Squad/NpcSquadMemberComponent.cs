namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     Lets an NPC self-assign into a squad with nearby friendly NPCs. See <see cref="NpcSquadSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcSquadSystem))]
public sealed partial class NpcSquadMemberComponent : Component
{
    /// <summary>
    ///     The most members, leader included, a squad this NPC joins or founds may have.
    /// </summary>
    [DataField]
    public int MaxSquadSize = 4;

    /// <summary>
    ///     A squad with fewer members than this, leader included, tries to merge into a nearby squad.
    /// </summary>
    [DataField]
    public int AssimilationThreshold = 2;

    /// <summary>
    ///     How close, with line of sight, this NPC must be to any member of a squad to join or merge into it.
    /// </summary>
    [DataField]
    public float JoinRange = 12f;

    /// <summary>
    ///     Whether this NPC can lead a squad. Only NPCs that can lead found squads or take over a squad whose leader
    ///         falls; the rest only ever join one, and on their own they stay disorganised. A squad left with nobody
    ///         able to lead breaks up.
    /// </summary>
    [DataField]
    public bool CanLead = true;

    /// <summary>
    ///     Blackboard keys this NPC hands down while it leads: any member with no value at one of these keys gets
    ///         the leader's. What the leader knows - where the threat is, say - becomes what the squad knows.
    /// </summary>
    [DataField]
    public List<string> SharedBlackboardKeys = new();

    /// <summary>
    ///     How this NPC's squad covers rooms while it leads. Read from the leader only, so a squad always
    ///         works to one set of numbers.
    /// </summary>
    [DataField]
    public NpcSquadCoverSettings Cover = new();

    /// <summary>
    ///     The squad entity this NPC is in, if any.
    /// </summary>
    [ViewVariables]
    public EntityUid? Squad;
}
