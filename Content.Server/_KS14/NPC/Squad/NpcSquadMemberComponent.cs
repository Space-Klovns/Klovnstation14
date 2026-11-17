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
    ///     How far this NPC may have to walk, in tiles, to a member of a squad it joins or merges into: round walls and
    ///         through doors, never through a wall. A squad it can see through a window, but not get to within this, is
    ///         not one it joins - it could not keep up with it.
    /// </summary>
    [DataField]
    public int JoinWalkDistance = 18;

    /// <summary>
    ///     Whether this NPC can lead a squad. Only NPCs that can lead found squads or take over a squad whose leader
    ///         falls; the rest only ever join one, and on their own they stay disorganised. A squad left with nobody
    ///         able to lead breaks up.
    /// </summary>
    [DataField]
    public bool CanLead = true;

    /// <summary>
    ///     Whether it goes to back up a squadmate calling out a hostile it cannot see itself, when it has nothing of
    ///         its own to fight. Off, it only fights what it sees, and leaves a squadmate in trouble to it. See
    ///         <c>AnswersCalloutPrecondition</c>.
    /// </summary>
    [DataField]
    public bool RespondsToCallouts = true;

    /// <summary>
    ///     How far, in tiles, it goes to back up a squadmate's callout: a hostile called out further away than this is
    ///         left to whoever is nearer. 0 for any distance, so a member ambushed on its own across the station still
    ///         gets help. Getting there is still up to the pathfinder, which gives up on long paths: see
    ///         <c>klovn.npc.path_node_limit</c>.
    /// </summary>
    [DataField]
    public float CalloutResponseRange;

    /// <summary>
    ///     Blackboard keys this NPC hands down while it leads: any member with no value at one of these keys gets
    ///         the leader's. What the leader knows - where the threat is, say - becomes what the squad knows.
    /// </summary>
    [DataField]
    public List<string> SharedBlackboardKeys = new();

    /// <summary>
    ///     How this NPC's squad covers rooms while it leads. Read from the leader only, so a squad always
    ///         works to one set of numbers. A child prototype's settings merge into its parent's, so it only needs to
    ///         list what it changes.
    /// </summary>
    [DataField, AlwaysPushInheritance]
    public NpcSquadCoverSettings Cover = new();

    /// <summary>
    ///     How this NPC's squad hunts hostiles it has lost, and regroups, while it leads - or how it does on its own.
    ///         See <see cref="Tactics.NpcSquadTacticsSystem"/>. A child prototype's settings merge into its parent's,
    ///         as <see cref="Cover"/>'s do.
    /// </summary>
    [DataField, AlwaysPushInheritance]
    public Tactics.NpcSquadTacticsSettings Tactics = new();

    /// <summary>
    ///     The value each shared blackboard key was last given by this NPC's leader. A key the NPC no longer has,
    ///         whose leader value still matches this, was dropped by the NPC on purpose - it dealt with it - and is
    ///         not handed back.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, object> ReceivedSharedValues = new();

    /// <summary>
    ///     The leader <see cref="ReceivedSharedValues"/> came from. Under any other leader - a new squad, or the
    ///         old leader succeeded - they say nothing about what this NPC dropped, and are forgotten.
    /// </summary>
    [ViewVariables]
    public EntityUid? ReceivedSharedFrom;

    /// <summary>
    ///     The squad entity this NPC is in, if any.
    /// </summary>
    [ViewVariables]
    public EntityUid? Squad;

    /// <summary>
    ///     The squad this NPC was in when it went down, so that dying after going critical is still reported against
    ///         the squad it went down with. See <see cref="NpcSquadMemberDownedEvent"/>.
    /// </summary>
    [ViewVariables]
    public EntityUid? LastSquad;
}
