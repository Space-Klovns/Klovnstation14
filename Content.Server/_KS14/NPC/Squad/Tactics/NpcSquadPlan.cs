using System.Threading.Tasks;
using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC.Pathfinding;
using Content.Shared._KS14.NPC;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Who would throw what, and where to.
/// </summary>
public readonly record struct NpcSquadThrow(EntityUid ThrowerUid, EntityUid GrenadeUid, EntityCoordinates Coordinates);

/// <summary>
///     What a throw into a room goes by: where to aim, if anywhere, and what the hostile is like. The hostile itself for one in
///         sight; what the squad last saw of it for one that is not.
/// </summary>
/// <param name="Aim">Where it is, or is believed to be. Null for nowhere: the throw goes just inside.</param>
/// <param name="Traits">What it is like. <see cref="NpcContactTraits.None"/> for nothing known, which rules out every softener
///     that depends on it.</param>
public readonly record struct NpcSofteningTarget(EntityCoordinates? Aim, NpcContactTraits Traits);

/// <summary>
///     A squad's plan for a hostile it can see and cannot get at: everything from noticing that, through carrying the plan
///         out, to the wait before it plans again.
/// </summary>
public sealed class NpcSquadPlan
{
    /// <summary>
    ///     The hostile it is all about.
    /// </summary>
    public required EntityUid TargetUid;

    public required TimeSpan StartedAt;

    /// <summary>
    ///     What the squad made of the target when <see cref="NpcSquadTacticsSystem"/> last looked for throwers
    ///         (<c>FindThrowers</c>): the traits <see cref="NpcPerceptionSystem.GetTraits"/> gave for the target itself, or, for a
    ///         hunt softening, the squad belief (<see cref="NpcSquadTacticsSystem.TryGetSquadBelief"/>). Kept so that
    ///         whether the target is armed (<see cref="NpcContactTraits.Armed"/>) is read from here, not asked of the gun system
    ///         again. Only as fresh as the last <c>RefreshPlanCandidates</c>, which every fact and cost is worked out after.
    /// </summary>
    public NpcContactTraits TargetTraits;

    public NpcSquadPlanState State = NpcSquadPlanState.Grace;

    /// <summary>
    ///     The search for a way to the hostile, and who it was asked for.
    /// </summary>
    public Task<PathResultEvent>? Probe;

    public EntityUid ProbeMemberUid;

    /// <summary>
    ///     What the search found, for the overlay.
    /// </summary>
    public readonly List<EntityCoordinates> ProbePath = new();

    /// <summary>
    ///     The room the hostile is in, the ways in, and who goes to which. The hunt's own, driven a step at a time, and never
    ///         on <see cref="NpcHuntComponent"/>: the squad is not hunting anyone. Null until there is a plan to carry out.
    /// </summary>
    public NpcHunt? Hunt;

    /// <summary>
    ///     What was so when the plan was made.
    /// </summary>
    public NpcSquadFact Facts;

    /// <summary>
    ///     Members have finished stacking up. Stays so: nobody has any reason to wander off.
    /// </summary>
    public bool Stacked;

    public readonly NpcSquadAction[] Steps = new NpcSquadAction[NpcSquadGoap.MaxSteps];

    public int StepCount;

    public int CurrentStep;

    public float Cost;

    public TimeSpan StepStartedAt;

    /// <summary>
    ///     Actions that have failed on this plan, as bits from <see cref="NpcSquadGoap.GetBan"/>.
    /// </summary>
    public int BannedActions;

    /// <summary>
    ///     When to look at what is so again.
    /// </summary>
    public TimeSpan NextReview;

    /// <summary>
    ///     How many members it was planned for.
    /// </summary>
    public int MemberCount;

    /// <summary>
    ///     Who can shoot out what is stopping it, and what that is.
    /// </summary>
    public readonly Dictionary<EntityUid, EntityUid> Breakers = new();

    /// <summary>
    ///     The most damage anything in <see cref="Breakers"/> takes to break.
    /// </summary>
    public float BreakDamage;

    /// <summary>
    ///     What the plan is after: a shot, or to be out of sight.
    /// </summary>
    public NpcSquadFact Goal = NpcSquadFact.HasShot;

    /// <summary>
    ///     Who stays where it can see the hostile while the rest go round, so that the squad does not lose it.
    /// </summary>
    public EntityUid? WatcherUid;

    /// <summary>
    ///     Who would throw a flashbang, and a grenade. Worked out with the facts.
    /// </summary>
    public NpcSquadThrow? FlashThrow;

    public NpcSquadThrow? FragThrow;

    /// <summary>
    ///     Who would put smoke in, and whether that smoke gasses: if it does, everyone goes on internals first.
    /// </summary>
    public NpcSquadThrow? SmokeThrow;

    public bool SmokeGasses;

    /// <summary>
    ///     Whether this plan puts smoke in before a grenade, where it could do either
    ///         (<see cref="NpcSquadTacticsSettings.PlanSmokeOverFragChance"/>). Rolled the first time it plans, so it holds
    ///         every time it plans again.
    /// </summary>
    public bool? SmokeOverFrag;

    /// <summary>
    ///     When what was thrown left its hand, and what it was.
    /// </summary>
    public TimeSpan? ThrownAt;

    public EntityUid ThrownUid;

    /// <summary>
    ///     When what was thrown was first seen to be gone, for smoke that takes a while to fill the room after.
    /// </summary>
    public TimeSpan? WentOffAt;

    /// <summary>
    ///     Something has gone in or gone off. Stays so for the plan: nobody has more than one go at this.
    /// </summary>
    public bool Softened;

    /// <summary>
    ///     Members that should use their meds before going in, and that should change magazines. Worked out with the facts.
    /// </summary>
    public readonly HashSet<EntityUid> Patients = new();

    public readonly HashSet<EntityUid> Reloaders = new();

    /// <summary>
    ///     Whether seeing to those is done with. Stay so for the plan: it is not going to stand about all day.
    /// </summary>
    public bool PatchedUp;

    public bool ToppedUp;

    /// <summary>
    ///     What a charge could do from here. Worked out with the facts.
    /// </summary>
    public NpcChargePick? DoorCharge;

    public NpcChargePick? WallCharge;

    /// <summary>
    ///     Whether the search for a wall to blow has been done.
    /// </summary>
    public bool WallSearched;

    /// <summary>
    ///     The charge being put to use, from the moment its step starts until it has gone off.
    /// </summary>
    public NpcSquadCharge? Charge;

    public TimeSpan CooldownStartedAt;

    public TimeSpan CooldownEndsAt;

    /// <summary>
    ///     Whether it came to nothing against a hostile they can see and cannot shoot. Until there is a shot or a way, the
    ///         squad holds where it is, watching it, rather than each member going after a shot it will never get.
    /// </summary>
    public bool HoldsOff;

    /// <summary>
    ///     Why it is doing what it is doing, in a few words, for the overlay.
    /// </summary>
    public string Reason = string.Empty;
}
