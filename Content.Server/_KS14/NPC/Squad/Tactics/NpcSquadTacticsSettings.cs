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
    ///     How long the whole hunt may take before the hostile is given up on. A blast is not cut short by it, and one that
    ///         gets them through starts it over.
    /// </summary>
    [DataField]
    public TimeSpan HuntTimeout = TimeSpan.FromSeconds(25);

    /// <summary>
    ///     How long members wait for each other outside the room before going in regardless.
    /// </summary>
    [DataField]
    public TimeSpan StageTimeout = TimeSpan.FromSeconds(6);

    /// <summary>
    ///     How long members have to get into the room once they go in, before searching wherever they got to. A member
    ///         part way through forcing a door holds this off.
    /// </summary>
    [DataField]
    public TimeSpan EntryTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     How long the squad waits for its doors to be forced before going in anyway, through whichever are open. A door
    ///         part way through being forced holds this off, however long the tool takes.
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
    ///     How far round members will go to get at a hostile instead of breaking in, in tiles walked past the straight line to
    ///         it. With the nearest way in one to force or blow, a way in more than this further is not used; with it opened by
    ///         hand, the rest are a flank, and all are used. A path to the hostile this much longer than the straight line is
    ///         no free way to leave members to walk, and a squad plan weighs a detour this long the same as blowing a wall, so a
    ///         longer one loses to a charge. Above 0.
    /// </summary>
    [DataField]
    public float MaxDetour = 15f;

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

    #region Planning

    /// <summary>
    ///     Whether the squad makes a plan for a hostile it can see and cannot get at, rather than leaving each member to it.
    ///         See <see cref="NpcSquadGoap"/>.
    /// </summary>
    [DataField]
    public bool Planning;

    /// <summary>
    ///     How long nobody has to have a shot before it counts as a problem: someone ducking behind a pillar is not one.
    /// </summary>
    [DataField]
    public TimeSpan PlanGrace = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     How long a plan may take before it is given up on.
    /// </summary>
    [DataField]
    public TimeSpan PlanTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     How long after a plan that could not be made or carried out before trying again.
    /// </summary>
    [DataField]
    public TimeSpan PlanCooldown = TimeSpan.FromSeconds(20);

    /// <summary>
    ///     How long after a plan that worked before planning again. Short: it is only there for a shot that comes and goes.
    /// </summary>
    [DataField]
    public TimeSpan PlanSuccessCooldown = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     The most damage something may take to break for it to be worth shooting out. Enough for ordinary windows, at 50, and
    ///         not for reinforced ones, at 75, or grilles, so that nobody can drain a squad's ammo from behind armoured glass.
    /// </summary>
    [DataField]
    public float PlanBreakDamage = 60f;

    /// <summary>
    ///     How long members shoot at something before giving up on it.
    /// </summary>
    [DataField]
    public TimeSpan PlanBreakTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     What going in on a hostile with a gun in its hand costs a plan, on top of going in at all, in about the seconds
    ///         other steps cost. Above what a flashbang costs, a squad with one softens up a room with an armed hostile in
    ///         it first, and does not waste one on anybody else.
    /// </summary>
    [DataField]
    public float PlanRushRisk = 6f;

    /// <summary>
    ///     How long after a flashbang goes in to go in after it, for it to go off first.
    /// </summary>
    [DataField]
    public TimeSpan PlanFlashWait = TimeSpan.FromSeconds(2.5);

    /// <summary>
    ///     How long after smoke goes off to go in after it, for the cloud to fill the room first.
    /// </summary>
    [DataField]
    public TimeSpan PlanSmokeSettle = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How long members keep their internals on for, from when gas is about to go in, whatever the air: long enough to
    ///         go through the cloud. Breathing it still keeps them on after.
    /// </summary>
    [DataField]
    public TimeSpan PlanSmokeInternalsTime = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How long members have to get out of sight when they have to, before the plan ends.
    /// </summary>
    [DataField]
    public TimeSpan PlanCoverTime = TimeSpan.FromSeconds(8);

    /// <summary>
    ///     How hurt a squad in a fight has to be, as the health left among its members from 0 to 1, for it to break contact and
    ///         patch itself up. 0 to fight to the end.
    /// </summary>
    [DataField]
    public float PlanWithdrawHealth;

    /// <summary>
    ///     The nearest a grenade may land to any member for it to be thrown at all.
    /// </summary>
    [DataField]
    public float PlanFragClearance = 4f;

    /// <summary>
    ///     What waiting at the exits costs a plan with a way in standing open, in about the seconds other steps cost. Under the
    ///         risk of walking in on a gun, and over softening the room up first, so that only a squad that cannot soften it
    ///         up waits. With every way shut it costs <see cref="PlanHoldShutCost"/> on top, which keeps that true of forcing
    ///         the door as well.
    /// </summary>
    [DataField]
    public float PlanHoldCost = 6f;

    /// <summary>
    ///     What waiting at the exits costs on top of <see cref="PlanHoldCost"/> with no way in open.
    /// </summary>
    [DataField]
    public float PlanHoldShutCost = 7f;

    /// <summary>
    ///     How long to wait at the exits for the hostile to come out, before going in after it.
    /// </summary>
    [DataField]
    public TimeSpan PlanHoldTime = TimeSpan.FromSeconds(12);

    /// <summary>
    ///     How hurt a member has to be, as the health it has left from 0 to 1, to use its meds before going in on a gun.
    /// </summary>
    [DataField]
    public float PlanPatchHealth = 0.7f;

    /// <summary>
    ///     How long members have to use their meds before the squad goes in without them.
    /// </summary>
    [DataField]
    public TimeSpan PlanPatchTime = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How empty a magazine has to be, as the share of it left from 0 to 1, to change it before going in.
    /// </summary>
    [DataField]
    public float PlanReloadFraction = 0.5f;

    /// <summary>
    ///     How long members have to change magazines before the squad goes in without.
    /// </summary>
    [DataField]
    public TimeSpan PlanTopUpTime = TimeSpan.FromSeconds(4);

    /// <summary>
    ///     How close to a charge going off the hostile has to be to count as softened up by it.
    /// </summary>
    [DataField]
    public float PlanChargeBlastRange = 3f;

    /// <summary>
    ///     Whether a hunt shut out of a room blows its way in, rather than holding outside it. A plan only has charges for a hostile
    ///         in sight; this is for going through a wall after a noise, so it should be for those meant to be frightening.
    /// </summary>
    [DataField]
    public bool HuntCharges;

    /// <summary>
    ///     The least pressure either side of a charge can have, in kPa: nobody blows a hole through to a room with nothing in it.
    /// </summary>
    [DataField]
    public float PlanChargeMinPressure = 50f;

    /// <summary>
    ///     How long a planter may go without getting any nearer its site, by the way it has to walk, before it is given up on.
    ///         Time busy with its hands (a door on the way, sticking the charge on) does not count, and nor does how long the way
    ///         is: nearer each time is bound to get there.
    /// </summary>
    [DataField]
    public TimeSpan PlanChargeTimeout = TimeSpan.FromSeconds(20);

    #endregion

    #region Fighting

    /// <summary>
    ///     How much members spread out around a hostile they are shooting, from 0 to 1. A firing spot's score is lowered by
    ///         up to this much the closer it is, seen from the hostile, to a squadmate already shooting it. See
    ///         <c>TacticalPositionOperator.CrossfireTargetKey</c>.
    /// </summary>
    [DataField]
    public float Crossfire;

    #endregion
}
