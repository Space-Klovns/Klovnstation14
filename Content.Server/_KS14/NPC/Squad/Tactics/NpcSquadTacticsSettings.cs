using Content.Server._KS14.NPC.Meters;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Tuning for <see cref="NpcSquadTacticsSystem"/>: how a squad hunts a hostile it has lost, and when it regroups.
///         Read from the leader, or from an NPC on its own, so a squad always works to one set of numbers.
/// </summary>
[DataDefinition]
public sealed partial class NpcSquadTacticsSettings
{
    #region Hunting

    /// <summary>
    ///     A hostile lost longer ago than this does not start a hunt.
    /// </summary>
    [DataField]
    public TimeSpan HuntStartAge = TimeSpan.FromSeconds(6);

    /// <summary>
    ///     How long members wait for a hostile they have just lost to show itself again, before going in after it.
    /// </summary>
    [DataField]
    public TimeSpan WatchTime = TimeSpan.FromSeconds(4);

    /// <summary>
    ///     How long the whole hunt may take before the hostile is given up on.
    /// </summary>
    [DataField]
    public TimeSpan HuntTimeout = TimeSpan.FromSeconds(25);

    /// <summary>
    ///     How long members wait for each other outside the room before going in regardless.
    /// </summary>
    [DataField]
    public TimeSpan StageTimeout = TimeSpan.FromSeconds(6);

    /// <summary>
    ///     How long members have to get into the room once they go in, before searching wherever they got to.
    /// </summary>
    [DataField]
    public TimeSpan EntryTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Longest the squad waits for its doors to be forced before going in anyway, through whichever are open.
    /// </summary>
    [DataField]
    public TimeSpan BreachTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How long members hold the area once the search comes up empty, before standing down.
    /// </summary>
    [DataField]
    public TimeSpan HoldAreaTime = TimeSpan.FromSeconds(8);

    /// <summary>
    ///     Whether members stack up outside the room and go in together, across different ways in where there are
    ///         several. Only ever done with at least two members.
    /// </summary>
    [DataField]
    public bool CanStackUp = true;

    /// <summary>
    ///     How far outside a way in, in tiles, members wait before going in.
    /// </summary>
    [DataField]
    public float StageDistance = 1.5f;

    /// <summary>
    ///     How close to its waiting spot a member has to be to count as in position.
    /// </summary>
    [DataField]
    public float StageArriveRange = 1f;

    /// <summary>
    ///     A way in further than this from a member, in tiles walked round the room, is not one it is sent to.
    /// </summary>
    [DataField]
    public int MaxStageDistance = 16;

    /// <summary>
    ///     How much members prefer ways in near where the hostile, or the disturbance, should be: each tile further from
    ///         it a way in is counts as this many more tiles of walking to get there. Members still spread out across
    ///         every way in before any gets a second; this decides who goes where, and which ways in get someone when
    ///         there are more of them than members. 0: the shortest walk wins outright.
    /// </summary>
    [DataField]
    public float EntranceTargetPreference = 2f;

    /// <summary>
    ///     How far into the room, past the way in, members go when they go in.
    /// </summary>
    [DataField]
    public float EntryDepth = 1.5f;

    /// <summary>
    ///     With no room to search, lockers within this many tiles of where the hostile was lost are searched.
    /// </summary>
    [DataField]
    public float SearchRadius = 6f;

    /// <summary>
    ///     The most lockers and spots checked on their own. Sweeping the room's floor is not counted.
    /// </summary>
    [DataField]
    public int MaxSearchPoints = 10;

    /// <summary>
    ///     How thorough the search is: the share of the room's floor, from 0 to 1, that has to have been seen before
    ///         the room counts as searched. At 1, every tile - round every corner, down every leg of an L - is looked
    ///         at. Lower lets a squad give up once it has seen most of a room.
    /// </summary>
    [DataField]
    public float SearchCoverage = 1f;

    /// <summary>
    ///     A spot, or a tile of floor, is searched once any member has seen it from within this many tiles.
    /// </summary>
    [DataField]
    public float ClearRange = 7f;

    /// <summary>
    ///     A member that has not cleared its spot, or seen the tile it was sweeping towards, within this long - it
    ///         cannot get there, say - gives up on it.
    /// </summary>
    [DataField]
    public TimeSpan SearchPointTimeout = TimeSpan.FromSeconds(12);

    /// <summary>
    ///     How much members sweeping a room keep in sight of each other: a stretch of floor no squadmate can see, from
    ///         within <see cref="SearchCohesionRange"/>, counts as this many tiles further to walk to. 0: each goes for
    ///         whatever unseen floor is nearest, and in a big room they soon lose sight of each other. Around the size of
    ///         the room: they clear it in sight of each other, splitting up only for what nobody can see otherwise.
    /// </summary>
    [DataField]
    public float SearchCohesion;

    /// <summary>
    ///     How far, in tiles, a squadmate can be and still count as keeping a member in sight while sweeping. See
    ///         <see cref="SearchCohesion"/>.
    /// </summary>
    [DataField]
    public float SearchCohesionRange = 12f;

    #endregion

    #region Caution

    /// <summary>
    ///     The meter that makes this squad careful, read from whoever it concerns: the leader for squad decisions, each
    ///         member for its own. Null for none, and caution then changes nothing.
    /// </summary>
    [DataField]
    public ProtoId<NpcMeterPrototype>? CautionMeter;

    /// <summary>
    ///     With the leader's caution at least this high, the squad hunts a disturbance - stacks up, goes in, sweeps
    ///         the room - rather than walking up to it.
    /// </summary>
    [DataField]
    public float CautiousHuntThreshold = 30f;

    /// <summary>
    ///     How far a member strays from its leader before regrouping, at full caution. It shrinks from
    ///         <see cref="RegroupDistance"/> towards this as the member's own caution rises.
    /// </summary>
    [DataField]
    public float CautiousRegroupDistance = 3.5f;

    #endregion

    #region Regrouping

    /// <summary>
    ///     A member further than this from its leader, in tiles, with nothing going on, goes back to it. Less, the more
    ///         cautious it is: see <see cref="CautiousRegroupDistance"/>.
    /// </summary>
    [DataField]
    public float RegroupDistance = 8f;

    /// <summary>
    ///     How close to the leader a regrouping member gets.
    /// </summary>
    [DataField]
    public float RegroupRange = 2.5f;

    /// <summary>
    ///     A member that has seen or heard of a hostile within this long has something going on.
    /// </summary>
    [DataField]
    public TimeSpan RegroupQuietTime = TimeSpan.FromSeconds(20);

    #endregion
}
