using System.Threading.Tasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared._KS14.NPC;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Squad.Tactics;

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

    public TimeSpan CooldownEndsAt;

    /// <summary>
    ///     Why it is doing what it is doing, in a few words, for the overlay.
    /// </summary>
    public string Reason = string.Empty;
}
