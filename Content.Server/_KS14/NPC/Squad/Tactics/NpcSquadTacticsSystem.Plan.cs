using System.Threading;
using Content.Server._KS14.NPC.Doors;
using Content.Server._KS14.NPC.Components;
using Content.Server._KS14.NPC.Grenades;
using Content.Server._KS14.NPC.LineOfFire;
using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC.Pathfinding;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._KS14.NPC;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Destructible;
using Content.Shared.Doors.Components;
using Content.Shared.Explosion.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Flash;
using Content.Shared.NPC.Components;
using Content.Shared.Sticky.Components;
using Content.Shared.Trigger.Components;
using Content.Shared.Trigger.Components.Effects;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     The squad's plan for a hostile it can see and cannot get at: through glass, or a door shut to all of them. Every member
///         on its own would walk up to the door and stop, or give up, and hunts are no help, as a hostile in sight is not lost.
///     <para>
///         <see cref="NpcSquadGoap"/> only picks the order of things. Stacking up, forcing doors and going in are the hunt's
///             own, driven a step at a time with a hunt nobody else sees. So this is mostly about when: noticing that nobody
///             has a shot, seeing if the members will not manage by themselves, and knowing when it is over. Its orders are
///             <see cref="NpcOrder.OverCombat"/>, or a member with a gun in its hand would never get round to them.
///     </para>
/// </summary>
public sealed partial class NpcSquadTacticsSystem
{
    [Dependency] private EntityWhitelistSystem _entityWhitelistSystem = default!;
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;
    [Dependency] private NpcSquadPlanDebugSystem _npcSquadPlanDebugSystem = default!;
    [Dependency] private PathfindingSystem _pathfindingSystem = default!;
    [Dependency] private SharedDestructibleSystem _destructibleSystem = default!;

    [Dependency] private EntityQuery<BallisticAmmoProviderComponent> _ballisticAmmoQuery = default!;
    [Dependency] private EntityQuery<ExplosiveComponent> _explosiveQuery = default!;
    [Dependency] private EntityQuery<FirelockComponent> _firelockQuery = default!;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<FlashOnTriggerComponent> _flashOnTriggerQuery = default!;
    [Dependency] private EntityQuery<ItemSlotsComponent> _itemSlotsQuery = default!;
    [Dependency] private EntityQuery<NpcHealingItemComponent> _healingItemQuery = default!;
    [Dependency] private EntityQuery<NPCRecentlyInjectedComponent> _recentlyInjectedQuery = default!;
    [Dependency] private EntityQuery<NpcSquadPlanComponent> _squadPlanQuery = default!;
    [Dependency] private EntityQuery<NpcThrownGrenadeComponent> _thrownGrenadeQuery = default!;
    [Dependency] private EntityQuery<SolutionComponent> _solutionQuery = default!;
    [Dependency] private EntityQuery<SolutionManagerComponent> _solutionManagerQuery = default!;
    [Dependency] private EntityQuery<StickyComponent> _stickyQuery = default!;
    [Dependency] private EntityQuery<TimerTriggerComponent> _timerTriggerQuery = default!;

    /// <summary>
    ///     How close to the hostile the search for a way to it has to get.
    /// </summary>
    private const float ProbeRange = 1.5f;

    /// <summary>
    ///     How fast a member walks, in tiles a second, to turn a walk into a cost.
    /// </summary>
    private const float PlanWalkSpeed = 4.5f;

    private const float ForceDoorCost = 6f;
    private const float EnterCost = 2f;
    private const float BreakGlassBaseCost = 4f;
    private const float BreakGlassDamagePerSecond = 10f;
    private const float FlashbangCost = 2f;
    private const float FragCost = 3f;
    private const float TakeCoverCost = 4f;
    private const float PatchUpCost = 3f;
    private const float TopUpCost = 1.5f;

    /// <summary>
    ///     How long something to throw has to leave a hand, once ordered, before something is taken to be wrong.
    /// </summary>
    private static readonly TimeSpan ThrowTimeout = TimeSpan.FromSeconds(4);

    /// <summary>
    ///     The longest to wait on a grenade that was thrown, should it never go off.
    /// </summary>
    private static readonly TimeSpan FragWaitLimit = TimeSpan.FromSeconds(8);

    /// <summary>
    ///     How far off the spot it was sent to a member getting out of sight may stop.
    /// </summary>
    private const float CoverOrderRange = 12f;

    private static readonly TimeSpan PlanReviewInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Whether the orders being given now are for a plan. Set around the hunt's own code, which gives them.
    /// </summary>
    private bool _givingPlanOrders;

    private readonly List<EntityUid> _combatContacts = new();
    private readonly List<EntityUid> _carried = new();

    /// <summary>
    ///     Within one <c>FindThrowers</c>: whether the aim can be seen from each entrance, by its index.
    /// </summary>
    private readonly Dictionary<int, bool> _entranceAimClear = new();

    /// <summary>
    ///     What the members who can see a hostile make of it.
    /// </summary>
    private enum NpcSquadSight : byte
    {
        Nothing,
        HasShot,
        Stuck,
    }

    /// <summary>
    ///     The planner's turn, before anything else gets a say. Returns whether a plan has the squad in hand, in which case
    ///         nothing else should give it orders.
    /// </summary>
    private bool UpdateSquadPlan(EntityUid issuerUid, EntityUid? leaderUid, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (!_squadPlanQuery.TryComp(issuerUid, out var planComponent))
        {
            if (!settings.Planning)
                return false;

            if (ShouldWithdraw(settings, out var threatUid))
            {
                planComponent = EnsureComp<NpcSquadPlanComponent>(issuerUid);
                planComponent.Plan = StartWithdrawal(threatUid, now);
            }
            // Not while a flank is going round to get one: the flankers are out of sight on purpose, and the plan would end it.
            else if (!IsFlanking(issuerUid) && LookForShot(out var targetUid) == NpcSquadSight.Stuck)
            {
                planComponent = EnsureComp<NpcSquadPlanComponent>(issuerUid);
                planComponent.Plan = new NpcSquadPlan { TargetUid = targetUid, StartedAt = now, Reason = "nobody has a shot" };
            }
            else
            {
                return false;
            }
        }

        var plan = planComponent.Plan;
        var inHand = plan.State switch
        {
            NpcSquadPlanState.Grace => UpdatePlanGrace(issuerUid, plan, settings, now),
            NpcSquadPlanState.Probing => UpdatePlanProbe(issuerUid, leaderUid, plan, settings, now),
            NpcSquadPlanState.Running => UpdatePlanSteps(issuerUid, plan, settings, now),
            _ => UpdatePlanCooldown(issuerUid, planComponent, now),
        };

        if (_squadPlanQuery.HasComp(issuerUid) && _npcSquadPlanDebugSystem.IsTracking(issuerUid, _members))
            _npcSquadPlanDebugSystem.SendFrame(issuerUid, planComponent.Plan, settings, _members);

        return inHand;
    }

    /// <summary>
    ///     Whether a member has a shot at a hostile it can see, or can see one and nobody can hit it. The first shot found
    ///         ends the search, so in an ordinary fight this is one ray.
    /// </summary>
    private NpcSquadSight LookForShot(out EntityUid targetUid)
    {
        targetUid = default;
        var nearest = float.MaxValue;

        foreach (var memberUid in _members)
        {
            FillCombatContacts(memberUid);

            if (_combatContacts.Count == 0)
                continue;

            var memberMapCoordinates = _transformSystem.GetMapCoordinates(memberUid);
            var shotMask = _npcLineOfFireSystem.GetShotMask(memberUid);

            foreach (var hostileUid in _combatContacts)
            {
                if (IsHostileOutOfAction(hostileUid))
                    continue;

                var hostileMapCoordinates = _transformSystem.GetMapCoordinates(hostileUid);
                if (hostileMapCoordinates.MapId != memberMapCoordinates.MapId)
                    continue;

                if (_npcLineOfFireSystem.IsClear(memberMapCoordinates, hostileMapCoordinates, shotMask))
                    return NpcSquadSight.HasShot;

                var distance = (hostileMapCoordinates.Position - memberMapCoordinates.Position).LengthSquared();
                if (distance >= nearest)
                    continue;

                nearest = distance;
                targetUid = hostileUid;
            }
        }

        return targetUid.IsValid() ? NpcSquadSight.Stuck : NpcSquadSight.Nothing;
    }

    /// <summary>
    ///     Whether the squad is in a fight and too hurt to be in one. Goes by the health left among its members and not by who
    ///         has gone down, so that a squad all carrying wounds pulls back as readily as one that lost a member.
    /// </summary>
    private bool ShouldWithdraw(NpcSquadTacticsSettings settings, out EntityUid threatUid)
    {
        threatUid = default;

        if (settings.PlanWithdrawHealth <= 0f || _members.Count < 2)
            return false;

        var health = 0f;

        foreach (var memberUid in _members)
        {
            health += _npcSquadSystem.GetHealthFraction(memberUid);

            if (threatUid.IsValid())
                continue;

            FillCombatContacts(memberUid);

            foreach (var hostileUid in _combatContacts)
            {
                if (IsHostileDown(hostileUid))
                    continue;

                threatUid = hostileUid;
                break;
            }
        }

        return threatUid.IsValid() && health / _members.Count <= settings.PlanWithdrawHealth;
    }

    /// <summary>
    ///     Whether a plan against <paramref name="hostileUid"/> has nothing left to do: it is gone, down, or known dead.
    ///         The strongest of three tests that are kept apart: see <see cref="IsHostileOutOfAction"/> and
    ///         <see cref="IsHuntedGone"/>.
    /// </summary>
    private bool IsHostileDown(EntityUid hostileUid)
    {
        return IsHostileOutOfAction(hostileUid) || AnyoneKnowsDead(hostileUid);
    }

    /// <summary>
    ///     Whether <paramref name="hostileUid"/> is gone or down, so no use to look for a shot at. Goes by what is so and not by
    ///         what the members know.
    /// </summary>
    private bool IsHostileOutOfAction(EntityUid hostileUid)
    {
        return TerminatingOrDeleted(hostileUid) || _mobStateSystem.IsIncapacitated(hostileUid);
    }

    /// <summary>
    ///     Refills <see cref="_combatContacts"/> with the hostiles <paramref name="memberUid"/> is in combat with.
    /// </summary>
    private void FillCombatContacts(EntityUid memberUid)
    {
        _combatContacts.Clear();
        _npcPerceptionSystem.GetCombatContacts(memberUid, _combatContacts);
    }

    private static NpcSquadPlan StartWithdrawal(EntityUid threatUid, TimeSpan now)
    {
        var plan = new NpcSquadPlan
        {
            TargetUid = threatUid,
            StartedAt = now,
            StepStartedAt = now,
            NextReview = TimeSpan.MaxValue,
            State = NpcSquadPlanState.Running,
            Goal = NpcSquadFact.OutOfSight,
            Facts = NpcSquadFact.Threatened,
            StepCount = 1,
            Reason = "too hurt to fight on",
        };

        plan.Steps[0] = NpcSquadAction.TakeCover;
        return plan;
    }

    /// <summary>
    ///     Giving it a moment. Nobody having a shot is usually a matter of someone ducking behind a pillar.
    /// </summary>
    private bool UpdatePlanGrace(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (LookForShot(out var targetUid) != NpcSquadSight.Stuck)
        {
            RemovePlan(issuerUid);
            return false;
        }

        plan.TargetUid = targetUid;

        if (now - plan.StartedAt < settings.PlanGrace)
            return false;

        StartProbe(plan);
        return false;
    }

    /// <summary>
    ///     Asks for a way to the hostile from whoever is nearest it. A member walks a way like that by itself, so there is
    ///         only something to plan if the way is shut.
    /// </summary>
    private void StartProbe(NpcSquadPlan plan)
    {
        var targetMapCoordinates = _transformSystem.GetMapCoordinates(plan.TargetUid);
        var nearest = float.MaxValue;

        foreach (var memberUid in _members)
        {
            var distance = (_transformSystem.GetMapCoordinates(memberUid).Position - targetMapCoordinates.Position).LengthSquared();
            if (distance >= nearest)
                continue;

            nearest = distance;
            plan.ProbeMemberUid = memberUid;
        }

        plan.Probe = _pathfindingSystem.GetPath(plan.ProbeMemberUid,
            plan.TargetUid,
            ProbeRange,
            CancellationToken.None,
            _pathfindingSystem.GetFlags(plan.ProbeMemberUid));
        plan.State = NpcSquadPlanState.Probing;
        plan.Reason = "looking for a way";
    }

    private bool UpdatePlanProbe(EntityUid issuerUid, EntityUid? leaderUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (LookForShot(out _) == NpcSquadSight.HasShot || TerminatingOrDeleted(plan.TargetUid))
        {
            RemovePlan(issuerUid);
            return false;
        }

        // Looking again for a way, after finding none: still holding meanwhile.
        if (plan.Probe is not { IsCompleted: true } probe)
            return plan.HoldsOff && HoldOff(issuerUid, plan.TargetUid, now);

        var probeMemberUid = plan.ProbeMemberUid;
        var freePath = false;

        if (probe.IsCompletedSuccessfully)
        {
#pragma warning disable RA0004 // checked above: it has finished, so this cannot wait on anything
            var result = probe.Result;
#pragma warning restore RA0004

            if (result.Result == PathResult.Path)
            {
                foreach (var poly in result.Path)
                {
                    plan.ProbePath.Add(poly.Coordinates);
                }

                freePath = !TerminatingOrDeleted(probeMemberUid) &&
                    !_npcDoorSystem.RunsThroughShutDoor(probeMemberUid, result.Path) &&
                    !IsLongWayRound(probeMemberUid, plan.TargetUid, result.Path, settings);
            }
        }

        plan.Probe = null;

        if (freePath)
        {
            SucceedPlan(issuerUid, plan, settings, now, "a free way: they walk it themselves");
            return false;
        }

        var targetCoordinates = _transformSystem.GetMoverCoordinates(plan.TargetUid);
        plan.Hunt = CreateHunt(targetUid: null,
            settings,
            lastKnownCoordinates: targetCoordinates,
            predictedCoordinates: targetCoordinates,
            now,
            allowCharges: true,
            phase: NpcHuntPhase.Stage);

        var foundRoom = PrepareRoom(leaderUid, plan.Hunt, settings) != null;
        PrepareSealedRoom(plan, GetWalker(leaderUid), foundRoom);

        AssignEntrances(plan.Hunt, settings);
        LeaveAWatcher(plan);
        plan.MemberCount = _members.Count;

        if (!TryMakePlan(plan, now))
        {
            FailPlan(issuerUid, plan, settings, now, "nothing to be done");
            return false;
        }

        plan.State = NpcSquadPlanState.Running;
        plan.StartedAt = now;
        plan.NextReview = now + PlanReviewInterval;
        return RunPlanSteps(issuerUid, plan, settings, now);
    }

    /// <summary>
    ///     Whether <paramref name="path"/> goes more than <see cref="NpcSquadTacticsSettings.MaxDetour"/> further than straight at
    ///         the hostile. A way like that is no free way: it is planned for, against a charge through what is in between.
    /// </summary>
    private bool IsLongWayRound(EntityUid memberUid, EntityUid targetUid, List<PathPoly> path, NpcSquadTacticsSettings settings)
    {
        var start = _transformSystem.GetMapCoordinates(memberUid);
        var target = _transformSystem.GetMapCoordinates(targetUid);
        var previous = start.Position;
        var length = 0f;

        foreach (var poly in path)
        {
            var position = _transformSystem.ToMapCoordinates(poly.Coordinates).Position;
            length += (position - previous).Length();
            previous = position;
        }

        return length - (target.Position - start.Position).Length() > settings.MaxDetour;
    }

    private bool UpdatePlanSteps(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (IsHostileDown(plan.TargetUid))
        {
            SucceedPlan(issuerUid, plan, settings, now, "the hostile is down");
            return true;
        }

        // Getting out of a fight does not stop for having a shot. Nor does a door they have just opened on a hostile they
        //      mean to soften up, as that is a moment and the shot it gives them is the thing to soften up first, nor a charge
        //      that is on, with everyone on their way out of the blast.
        if (plan.Goal == NpcSquadFact.HasShot && LookForShot(out _) == NpcSquadSight.HasShot && !IsCommitted(plan))
        {
            SucceedPlan(issuerUid, plan, settings, now, "somebody has a shot");
            return true;
        }

        if (now - plan.StartedAt >= settings.PlanTimeout)
        {
            FailPlan(issuerUid, plan, settings, now, "took too long");
            return true;
        }

        // Waiting for its meds to work: it will not come.
        var recovering = RemoveRecovering(issuerUid, _recovering);

        if (_members.Count == 0)
        {
            EndPlan(issuerUid, plan, settings.PlanCooldown, now, "nobody left");
            return true;
        }

        if (NeedsReplan(plan, settings, recovering, now) && !TryMakePlan(plan, now))
        {
            FailPlan(issuerUid, plan, settings, now, "nothing left to try");
            return true;
        }

        return RunPlanSteps(issuerUid, plan, settings, now);
    }

    /// <summary>
    ///     Whether who does what has to be worked out again: somebody went down or joined, or the facts changed at the review.
    ///         Hands the ways in out again if the squad changed.
    /// </summary>
    private bool NeedsReplan(NpcSquadPlan plan, NpcSquadTacticsSettings settings, int recovering, TimeSpan now)
    {
        var replan = false;

        // Somebody down, or somebody joined: who does what has to be worked out again. One that is only seeing to itself is
        //      still where it was sent, and a charge that is on carries on regardless.
        if (plan.Hunt != null && plan.Charge == null && _members.Count + recovering != plan.MemberCount)
        {
            plan.MemberCount = _members.Count + recovering;
            AssignEntrances(plan.Hunt, settings);
            LeaveAWatcher(plan);
            plan.Stacked = false;
            SetPhase(plan.Hunt, NpcHuntPhase.Stage, now);
            replan = true;
        }

        if (plan.Hunt != null && plan.Charge == null && now >= plan.NextReview)
        {
            plan.NextReview = now + PlanReviewInterval;
            replan |= GetFacts(plan, settings) != plan.Facts;
        }

        return replan;
    }

    /// <summary>
    ///     Carries out the current step. One that finishes falls through to the next in the same update, so nobody stands about
    ///         for half a second between one and the next.
    /// </summary>
    private bool RunPlanSteps(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        using var planOrders = GivePlanOrders();

        for (var i = 0; i < NpcSquadGoap.MaxSteps; i++)
        {
            var finished = RunStep(issuerUid, plan, plan.Steps[plan.CurrentStep], settings, now);

            if (!finished || plan.State != NpcSquadPlanState.Running)
                break;

            if (++plan.CurrentStep < plan.StepCount)
            {
                plan.StepStartedAt = now;
                continue;
            }

            // Out of sight, and it stays there a while: going straight back would only put it where it just got away from.
            if (plan.Goal == NpcSquadFact.OutOfSight)
            {
                EndPlan(issuerUid, plan, settings.PlanCooldown, now, "got out of sight");
                break;
            }

            // Gone in, or shot out the glass, and still nobody has a shot. Nothing above ever saw one, so it is done.
            SucceedPlan(issuerUid, plan, settings, now, "done, and nobody has a shot");
            break;
        }

        if (plan.State == NpcSquadPlanState.Running)
            KeepWatch(issuerUid, plan, now);

        return true;
    }

    /// <summary>
    ///     Carries out one step of the plan. Returns whether it has finished.
    /// </summary>
    private bool RunStep(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadAction action, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        return action switch
        {
            NpcSquadAction.StackUp => RunStackUp(issuerUid, plan, settings, now),
            NpcSquadAction.ForceDoor => RunForceDoor(issuerUid, plan, settings, now),
            NpcSquadAction.Enter or NpcSquadAction.EnterSoftened => RunEnter(issuerUid, plan, settings, now),
            NpcSquadAction.Flashbang or NpcSquadAction.FragRoom or NpcSquadAction.SmokeRoom => RunThrow(issuerUid, plan, action, settings, now),
            NpcSquadAction.TakeCover => RunTakeCover(issuerUid, plan, settings, now),
            NpcSquadAction.HoldExits => RunHoldExits(issuerUid, plan, settings, now),
            NpcSquadAction.PatchUp => RunPatchUp(issuerUid, plan, settings, now),
            NpcSquadAction.TopUp => RunTopUp(issuerUid, plan, settings, now),
            NpcSquadAction.BlowDoor or NpcSquadAction.BlowWall => RunCharge(issuerUid, plan, action, settings, now),
            NpcSquadAction.BreakGlass => RunBreakGlass(issuerUid, plan, settings, now),
            _ => RunBreakGlass(issuerUid, plan, settings, now), // as it always was for anything not named above
        };
    }

    /// <summary>
    ///     Whether a shot should wait for what the plan is in the middle of. A charge that is on, or something still to be
    ///         thrown through a way in that is open. Only those: a shot that turned up some other way is no reason to hold
    ///         off taking it.
    /// </summary>
    private bool IsCommitted(NpcSquadPlan plan)
    {
        if (plan.Charge is { Phase: not NpcSquadChargePhase.Planting })
            return true;

        if (plan.Hunt is not { } hunt || plan.Softened || AnyDoorToForce(hunt))
            return false;

        for (var i = plan.CurrentStep; i < plan.StepCount; i++)
        {
            if (plan.Steps[i] is NpcSquadAction.Flashbang or NpcSquadAction.FragRoom or NpcSquadAction.SmokeRoom)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Keeps back one member that can see the hostile and has no door to open, for the squad not to lose it while the rest
    ///         go round. Stacking up takes everyone out of sight of what they are stacking up on.
    /// </summary>
    private void LeaveAWatcher(NpcSquadPlan plan)
    {
        plan.WatcherUid = null;

        if (plan.Hunt is not { } hunt)
            return;

        hunt.HeldBackUid = null;

        // One kept back plants nothing, and a charge may be what it comes to: one without is kept back if there is one, and the
        //      only one with never is.
        EntityUid? carrierUid = null;
        EntityUid? watcherUid = null;

        foreach (var (memberUid, staging) in hunt.StagedMembers)
        {
            if (staging.IsLead ||
                !_npcPerceptionSystem.TryGetContact(memberUid, plan.TargetUid, out var contact) ||
                contact.State != NpcContactState.Visible)
                continue;

            if (!CarriesCharge(memberUid))
            {
                watcherUid = memberUid;
                break;
            }

            carrierUid ??= memberUid;
        }

        if (watcherUid == null && carrierUid is { } carrier && CountChargeCarriers() > 1)
            watcherUid = carrier;

        if (watcherUid is not { } watcher)
            return;

        plan.WatcherUid = watcher;
        hunt.HeldBackUid = watcher;
        hunt.StagedMembers.Remove(watcher);
    }

    private int CountChargeCarriers()
    {
        var count = 0;

        foreach (var memberUid in _members)
        {
            if (CarriesCharge(memberUid))
                count++;
        }

        return count;
    }

    private void KeepWatch(EntityUid issuerUid, NpcSquadPlan plan, TimeSpan now)
    {
        if (plan.WatcherUid is not { } watcherUid ||
            !_members.Contains(watcherUid) ||
            plan.Steps[plan.CurrentStep] is NpcSquadAction.BreakGlass or NpcSquadAction.TakeCover or NpcSquadAction.PatchUp or NpcSquadAction.TopUp ||
            plan.Charge is { Phase: not NpcSquadChargePhase.Planting } ||
            TerminatingOrDeleted(plan.TargetUid))
            return;

        OrderWatchToward(watcherUid, issuerUid, _transformSystem.GetMapCoordinates(plan.TargetUid), now);
    }

    /// <summary>
    ///     One member puts something in through the way in, and the rest wait where they are until it has gone off: a set time
    ///         for a flashbang, until it is gone for a grenade, and for smoke, a while after it is gone for the cloud to fill
    ///         the room. Before gas goes in, everyone goes on internals.
    /// </summary>
    private bool RunThrow(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadAction action, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var hunt = plan.Hunt!;

        if (plan.ThrownAt is { } thrownAt)
        {
            if (!HasGoneOff(plan, action, thrownAt, settings, now))
                return false;

            plan.Softened = true;
            return true;
        }

        var chosen = action switch
        {
            NpcSquadAction.Flashbang => plan.FlashThrow,
            NpcSquadAction.FragRoom => plan.FragThrow,
            _ => plan.SmokeThrow,
        };

        if (chosen is { } thrown && (TerminatingOrDeleted(thrown.GrenadeUid) || _thrownGrenadeQuery.HasComp(thrown.GrenadeUid)))
        {
            plan.ThrownAt = now;
            plan.ThrownUid = thrown.GrenadeUid;
            return false;
        }

        if (chosen is not { } toThrow || !_members.Contains(toThrow.ThrowerUid) || now - plan.StepStartedAt >= ThrowTimeout)
        {
            FailThrow(issuerUid, plan, action, settings, now);
            return false;
        }

        // Where the hostile is now, not where it was when the plan was made: it has had a while to move since, more so for
        //      a squad that had to go on internals first. Or, for one out of sight, where they now believe it to be. A grenade
        //      that would now come down among them is not thrown.
        if (hunt.StagedMembers.TryGetValue(toThrow.ThrowerUid, out var throwerStaging))
        {
            var aim = GetSofteningAim(plan);
            var coordinates = action == NpcSquadAction.FragRoom
                ? GetFragLanding(plan, aim, toThrow.ThrowerUid, throwerStaging)
                : GetThrowCoordinates(plan, aim, toThrow.ThrowerUid, throwerStaging);

            if (action == NpcSquadAction.FragRoom && !IsClearOfEveryone(hunt, coordinates, settings.PlanFragClearance))
            {
                FailThrow(issuerUid, plan, action, settings, now);
                return false;
            }

            toThrow = toThrow with { Coordinates = coordinates };
        }

        // Nobody takes a breath of it: the throw waits until all of them are on internals.
        var holdTheThrow = action == NpcSquadAction.SmokeRoom && plan.SmokeGasses && !PutSquadOnInternals(settings, now);

        foreach (var memberUid in _members)
        {
            if (memberUid == toThrow.ThrowerUid && !holdTheThrow)
            {
                SetOrder(memberUid, issuerUid, NpcOrderKind.ThrowGrenade, toThrow.Coordinates, Angle.Zero, OrderRange, null, now, toolUid: toThrow.GrenadeUid);
                continue;
            }

            HoldStagedMember(memberUid, issuerUid, hunt, now);
        }

        return false;
    }

    /// <summary>
    ///     A throw that is not going to happen. A plan tries something else. A hunt's softening has nothing else to try, and
    ///         goes in without.
    /// </summary>
    private void FailThrow(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadAction action, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (plan.Hunt is { } hunt && hunt.Soften == plan)
        {
            hunt.SoftenGaveUp = true;
            return;
        }

        FailPlanStep(issuerUid, plan, action, settings, now);
    }

    private bool HasGoneOff(NpcSquadPlan plan, NpcSquadAction action, TimeSpan thrownAt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        switch (action)
        {
            case NpcSquadAction.Flashbang:
                return now - thrownAt >= settings.PlanFlashWait;
            case NpcSquadAction.FragRoom:
                return TerminatingOrDeleted(plan.ThrownUid) || now - thrownAt >= FragWaitLimit;
        }

        if (now - thrownAt >= FragWaitLimit + settings.PlanSmokeSettle)
            return true;

        if (!TerminatingOrDeleted(plan.ThrownUid))
            return false;

        plan.WentOffAt ??= now;
        return now - plan.WentOffAt.Value >= settings.PlanSmokeSettle;
    }

    /// <summary>
    ///     Has <paramref name="memberUid"/> wait at its way in's stage spot, if it has a way in.
    /// </summary>
    private void HoldStagedMember(EntityUid memberUid, EntityUid issuerUid, NpcHunt hunt, TimeSpan now)
    {
        TryOrderAtEntrance(memberUid, issuerUid, hunt, NpcOrderKind.Stage, useEntry: false, now);
    }

    /// <summary>
    ///     Everyone stays on their way in, and lets the hostile come to them. It either shows itself, and a shot ends the plan,
    ///         or it is banned once it has gone on long enough, and they go in after it.
    /// </summary>
    private bool RunHoldExits(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (now - plan.StepStartedAt >= settings.PlanHoldTime)
        {
            FailPlanStep(issuerUid, plan, NpcSquadAction.HoldExits, settings, now);
            return false;
        }

        foreach (var memberUid in _members)
        {
            HoldStagedMember(memberUid, issuerUid, plan.Hunt!, now);
        }

        return false;
    }

    /// <summary>
    ///     Whoever is hurt uses what it has, where it stands, and the rest wait. One that has used its meds is left out of the
    ///         members until they have worked, which is what is being waited for.
    /// </summary>
    private bool RunPatchUp(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        FindPatients(plan, settings);

        var recovering = plan.MemberCount != _members.Count;

        if ((plan.Patients.Count == 0 && !recovering) || now - plan.StepStartedAt >= settings.PlanPatchTime)
        {
            plan.PatchedUp = true;
            return true;
        }

        OrderChosenElseHold(issuerUid, plan, plan.Patients, NpcOrderKind.PatchUp, now);
        return false;
    }

    /// <summary>
    ///     Whoever is low changes its magazine, where it stands, and the rest wait.
    /// </summary>
    private bool RunTopUp(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        FindReloaders(plan, settings);

        if (plan.Reloaders.Count == 0 || now - plan.StepStartedAt >= settings.PlanTopUpTime)
        {
            plan.ToppedUp = true;
            return true;
        }

        OrderChosenElseHold(issuerUid, plan, plan.Reloaders, NpcOrderKind.TopUp, now);
        return false;
    }

    /// <summary>
    ///     The members in <paramref name="chosen"/> do <paramref name="kind"/> where they stand, and the rest wait at their
    ///         way in.
    /// </summary>
    private void OrderChosenElseHold(EntityUid issuerUid, NpcSquadPlan plan, HashSet<EntityUid> chosen, NpcOrderKind kind, TimeSpan now)
    {
        foreach (var memberUid in _members)
        {
            if (chosen.Contains(memberUid))
                SetOrder(memberUid, issuerUid, kind, Transform(memberUid).Coordinates, Angle.Zero, OrderRange, null, now);
            else
                HoldStagedMember(memberUid, issuerUid, plan.Hunt!, now);
        }
    }

    /// <summary>
    ///     Works out who is hurt and has meds that would see to it. See <see cref="NpcSquadTacticsSettings.PlanPatchHealth"/>.
    /// </summary>
    private void FindPatients(NpcSquadPlan plan, NpcSquadTacticsSettings settings)
    {
        plan.Patients.Clear();

        foreach (var memberUid in _members)
        {
            if (_recentlyInjectedQuery.HasComp(memberUid) || _npcSquadSystem.GetHealthFraction(memberUid) >= settings.PlanPatchHealth)
                continue;

            if (CarriesAny(memberUid, static (system, itemUid) => system.IsHealingItem(itemUid)))
                plan.Patients.Add(memberUid);
        }
    }

    private bool IsHealingItem(EntityUid itemUid)
    {
        return _healingItemQuery.HasComp(itemUid) && (_solutionManagerQuery.HasComp(itemUid) || _solutionQuery.HasComp(itemUid));
    }

    /// <summary>
    ///     Whether <paramref name="memberUid"/> carries anything <paramref name="matches"/> says yes to. Takes a static lambda,
    ///         so asking costs no allocation.
    /// </summary>
    private bool CarriesAny(EntityUid memberUid, Func<NpcSquadTacticsSystem, EntityUid, bool> matches)
    {
        return CarriesAny(memberUid, matches, static (system, itemUid, match) => match(system, itemUid));
    }

    /// <summary>
    ///     The same, handing <paramref name="state"/> to <paramref name="matches"/> for it to go by.
    /// </summary>
    private bool CarriesAny<TState>(EntityUid memberUid, TState state, Func<NpcSquadTacticsSystem, EntityUid, TState, bool> matches)
    {
        _npcHandsSystem.GetCarried(memberUid, _carried);

        foreach (var itemUid in _carried)
        {
            if (matches(this, itemUid, state))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Works out who is low on ammo and has a magazine that fits. See <see cref="NpcSquadTacticsSettings.PlanReloadFraction"/>.
    /// </summary>
    private void FindReloaders(NpcSquadPlan plan, NpcSquadTacticsSettings settings)
    {
        plan.Reloaders.Clear();

        foreach (var memberUid in _members)
        {
            if (!_gunSystem.TryGetGun(memberUid, out var gunEntity))
                continue;

            var ammoEvent = GetAmmo(gunEntity.Owner);

            if (ammoEvent.Capacity <= 0 ||
                (float) ammoEvent.Count / ammoEvent.Capacity >= settings.PlanReloadFraction ||
                !_itemSlotsQuery.TryComp(gunEntity.Owner, out var itemSlotsComponent) ||
                !_itemSlotsSystem.TryGetSlot(gunEntity.Owner, SharedGunSystem.MagazineSlot, out var magazineSlot, itemSlotsComponent))
                continue;

            var fitsMagazineSlot = static (NpcSquadTacticsSystem system, EntityUid itemUid, EntityWhitelist? whitelist) =>
                system._ballisticAmmoQuery.HasComp(itemUid) && !system._entityWhitelistSystem.IsWhitelistFailOrNull(whitelist, itemUid);

            if (CarriesAny(memberUid, magazineSlot.Whitelist, fitsMagazineSlot))
                plan.Reloaders.Add(memberUid);
        }
    }

    /// <summary>
    ///     What <paramref name="gunUid"/> has in it and holds.
    /// </summary>
    private GetAmmoCountEvent GetAmmo(EntityUid gunUid)
    {
        var ammoEvent = new GetAmmoCountEvent();
        RaiseLocalEvent(gunUid, ref ammoEvent);
        return ammoEvent;
    }

    /// <summary>
    ///     Everyone gets out of the hostile's sight. HTN picks each one's spot, and heals and reloads there like anywhere else.
    /// </summary>
    private bool RunTakeCover(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (now - plan.StepStartedAt >= settings.PlanCoverTime)
            return true;

        if (TerminatingOrDeleted(plan.TargetUid))
            return true;

        var threatCoordinates = _transformSystem.GetMoverCoordinates(plan.TargetUid);

        foreach (var memberUid in _members)
        {
            SetOrder(memberUid, issuerUid, NpcOrderKind.TakeCover, threatCoordinates, Angle.Zero, CoverOrderRange, null, now);
        }

        return false;
    }

    private bool RunStackUp(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var hunt = plan.Hunt!;

        EnsurePhase(hunt, NpcHuntPhase.Stage, now);

        if (!UpdateStage(issuerUid, leaderUid: null, hunt, settings, now))
        {
            // The ways in were handed out again, and there are none.
            if (hunt.Phase is NpcHuntPhase.Search or NpcHuntPhase.Exhausted)
                FailPlanStep(issuerUid, plan, NpcSquadAction.StackUp, settings, now);

            return false;
        }

        plan.Stacked = true;
        return true;
    }

    private bool RunForceDoor(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var hunt = plan.Hunt!;

        EnsurePhase(hunt, NpcHuntPhase.Breach, now);

        if (!UpdateBreach(issuerUid, hunt, settings, now))
            return false;

        if (!AnyDoorToForce(hunt))
            return true;

        FailPlanStep(issuerUid, plan, NpcSquadAction.ForceDoor, settings, now);
        return false;
    }

    private bool RunEnter(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var hunt = plan.Hunt!;

        EnsurePhase(hunt, NpcHuntPhase.Entry, now);

        return UpdateEntry(issuerUid, leaderUid: null, hunt, settings, now);
    }

    /// <summary>
    ///     Everyone who can shoot at what is stopping them. The step ends when somebody has a shot, which is looked for before
    ///         this, so it only ends here by running out of time or of things to shoot at.
    /// </summary>
    private bool RunBreakGlass(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        FindBreakers(plan, settings);

        if (plan.Breakers.Count == 0 || now - plan.StepStartedAt >= settings.PlanBreakTimeout)
        {
            FailPlanStep(issuerUid, plan, NpcSquadAction.BreakGlass, settings, now);
            return false;
        }

        foreach (var memberUid in _members)
        {
            if (!plan.Breakers.TryGetValue(memberUid, out var barrierUid))
            {
                ClearOrder(memberUid, issuerUid);
                continue;
            }

            SetOrder(memberUid, issuerUid, NpcOrderKind.BreakBarrier, Transform(barrierUid).Coordinates, Angle.Zero, OrderRange, barrierUid, now);
        }

        return false;
    }

    private void FailPlanStep(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadAction action, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        plan.BannedActions |= NpcSquadGoap.GetBan(action);

        if (!TryMakePlan(plan, now))
            FailPlan(issuerUid, plan, settings, now, $"{action} failed, and nothing else to try");
    }

    /// <summary>
    ///     Works out what is so, and the cheapest way from there to a shot. A plan that still starts with what is being done
    ///         carries on with it, rather than starting that step's clock again.
    /// </summary>
    private bool TryMakePlan(NpcSquadPlan plan, TimeSpan now)
    {
        var settings = plan.Hunt!.Settings;
        plan.Facts = GetFacts(plan, settings);

        Span<float> costs = stackalloc float[NpcSquadGoap.Actions.Length];
        costs[(int) NpcSquadAction.StackUp] = GetLongestWalk(plan.Hunt) / PlanWalkSpeed + GetDetourCost(plan.Hunt, settings);
        costs[(int) NpcSquadAction.ForceDoor] = ForceDoorCost;
        costs[(int) NpcSquadAction.Enter] = EnterCost + GetRushRisk(plan, settings);
        costs[(int) NpcSquadAction.EnterSoftened] = EnterCost;
        costs[(int) NpcSquadAction.Flashbang] = FlashbangCost;
        GetSofteningCosts(plan, settings, out costs[(int) NpcSquadAction.FragRoom], out costs[(int) NpcSquadAction.SmokeRoom]);
        costs[(int) NpcSquadAction.HoldExits] = settings.PlanHoldCost + ((plan.Facts & NpcSquadFact.WayOpen) != 0x0 ? 0f : settings.PlanHoldShutCost);
        costs[(int) NpcSquadAction.PatchUp] = PatchUpCost;
        costs[(int) NpcSquadAction.TopUp] = TopUpCost;
        costs[(int) NpcSquadAction.BlowDoor] = BlowDoorCost + GetChargeWait(plan.DoorCharge);
        costs[(int) NpcSquadAction.BlowWall] = BlowWallCost + GetChargeWait(plan.WallCharge) + (plan.WallCharge?.RouteLength ?? 0) / PlanWalkSpeed;
        costs[(int) NpcSquadAction.BreakGlass] = BreakGlassBaseCost + plan.BreakDamage / BreakGlassDamagePerSecond;
        costs[(int) NpcSquadAction.TakeCover] = TakeCoverCost;

        Span<NpcSquadAction> steps = stackalloc NpcSquadAction[NpcSquadGoap.MaxSteps];

        plan.Goal = NpcSquadFact.HasShot;
        var planned = NpcSquadGoap.TryPlan(plan.Facts, NpcSquadFact.HasShot, costs, plan.BannedActions, steps, out var length, out var cost);

        // No way of getting at it, and it can get at them.
        if (!planned)
        {
            plan.Goal = NpcSquadFact.OutOfSight;
            planned = NpcSquadGoap.TryPlan(plan.Facts, NpcSquadFact.OutOfSight, costs, plan.BannedActions, steps, out length, out cost);
        }

        if (!planned || length == 0)
        {
            plan.StepCount = 0;
            return false;
        }

        var carriesOn = plan.StepCount > plan.CurrentStep && plan.Steps[plan.CurrentStep] == steps[0];

        steps[..length].CopyTo(plan.Steps);
        plan.StepCount = length;
        plan.CurrentStep = 0;
        plan.Cost = cost;

        if (!carriesOn)
            plan.StepStartedAt = now;

        plan.Reason = "planned";
        return true;
    }

    private static float GetChargeWait(NpcChargePick? pick)
    {
        return pick is { } chosen ? (float) chosen.Charge.Fuse.TotalSeconds * ChargeFuseCost : 0f;
    }

    /// <summary>
    ///     What going straight in costs on top of going in: more if what is in there has a gun in its hand, as anyone can see.
    /// </summary>
    private float GetRushRisk(NpcSquadPlan plan, NpcSquadTacticsSettings settings)
    {
        return (plan.TargetTraits & NpcContactTraits.Armed) != 0x0 ? settings.PlanRushRisk : 0f;
    }

    /// <summary>
    ///     What is so now, for the planner. Looks again at everything the plan could pick from first
    ///         (<see cref="RefreshPlanCandidates"/>), as it writes that into the plan.
    /// </summary>
    private NpcSquadFact GetFacts(NpcSquadPlan plan, NpcSquadTacticsSettings settings)
    {
        RefreshPlanCandidates(plan, settings);
        return ReadFacts(plan);
    }

    /// <summary>
    ///     Works out again who could break, throw, patch up, reload or plant what, and writes it into the plan. Every one of
    ///         them, every time: the facts below read what they find.
    /// </summary>
    private void RefreshPlanCandidates(NpcSquadPlan plan, NpcSquadTacticsSettings settings)
    {
        FindBreakers(plan, settings);
        FindThrowers(plan, settings);
        FindPatients(plan, settings);
        FindReloaders(plan, settings);
        FindCharges(plan, settings, settings.MaxStageDistance);
    }

    /// <summary>
    ///     The facts that follow from the hunt, and from what <see cref="RefreshPlanCandidates"/> found.
    /// </summary>
    private NpcSquadFact ReadFacts(NpcSquadPlan plan)
    {
        var hunt = plan.Hunt!;
        var facts = plan.Stacked ? NpcSquadFact.Stacked : NpcSquadFact.None;

        if (plan.Softened)
            facts |= NpcSquadFact.Softened;

        facts |= GetWayInFacts(hunt);

        if (AreExitsCovered(hunt))
            facts |= NpcSquadFact.ExitsCovered;

        if (plan.Breakers.Count > 0)
            facts |= NpcSquadFact.GlassBreakable;

        if (plan.FlashThrow != null)
            facts |= NpcSquadFact.HasFlashbang;

        if (plan.FragThrow != null)
            facts |= NpcSquadFact.HasFrag;

        if (plan.SmokeThrow != null)
            facts |= NpcSquadFact.HasSmoke;

        if (CanBeHit(plan))
            facts |= NpcSquadFact.Threatened;

        var armed = (plan.TargetTraits & NpcContactTraits.Armed) != 0x0;

        if (plan.PatchedUp || !armed || plan.Patients.Count == 0)
            facts |= NpcSquadFact.Patched;

        if (plan.ToppedUp || plan.Reloaders.Count == 0)
            facts |= NpcSquadFact.Loaded;

        if (AnyoneHasACharge())
            facts |= NpcSquadFact.HasCharge;

        if (plan.DoorCharge != null)
            facts |= NpcSquadFact.DoorChargeable;

        if (plan.WallCharge != null)
            facts |= NpcSquadFact.WallChargeable;

        return facts;
    }

    /// <summary>
    ///     What the members' ways in come to: there are some, and whether the lead of each can get through it.
    /// </summary>
    private NpcSquadFact GetWayInFacts(NpcHunt hunt)
    {
        var facts = NpcSquadFact.None;

        foreach (var staging in hunt.StagedMembers.Values)
        {
            facts |= NpcSquadFact.HasWayIn;

            if (!staging.IsLead)
                continue;

            var entrance = hunt.Entrances[staging.EntranceIndex];

            // Not a way in at all, however it would be opened.
            if (entrance.DoorUid is { } doorUid && HoldsBackDanger(doorUid))
                continue;

            var standingDoor = false;

            if (entrance.DoorUid is { } standingDoorUid && _doorQuery.TryComp(standingDoorUid, out var doorComponent))
                standingDoor = doorComponent.State is not (DoorState.Open or DoorState.Opening);

            // Blowing it is a step of its own, with its own fact.
            if (staging.Method != NpcBreachMethod.Charge || !standingDoor)
                facts |= GetDoorToForce(staging, entrance) == null ? NpcSquadFact.WayOpen : NpcSquadFact.DoorForceable;

            // A door opened by hand is shut until somebody does, and nothing goes through a shut door.
            if (!standingDoor)
                facts |= NpcSquadFact.ThrowLine;
        }

        return facts;
    }

    /// <summary>
    ///     Whether somebody is on every way out of the room. A door holding back vacuum or fire is not a way out of anything.
    /// </summary>
    private bool AreExitsCovered(NpcHunt hunt)
    {
        var exits = 0;

        for (var e = 0; e < hunt.Entrances.Count; e++)
        {
            if (hunt.Entrances[e].DoorUid is { } doorUid && HoldsBackDanger(doorUid))
                continue;

            if (!IsEntranceUsed(hunt, e))
                return false;

            exits++;
        }

        return exits > 0;
    }

    /// <summary>
    ///     Finds who, stacked up on a way in, would throw what, and where to: where the hostile is, if a throw would get there,
    ///         and otherwise just inside. A flashbang is for something a flash would reach. A grenade is for somewhere that
    ///         leaves all of them out of the blast, which just inside the door hardly ever does. What the hostile is, and where,
    ///         comes from <see cref="GetSofteningTarget"/>.
    /// </summary>
    private void FindThrowers(NpcSquadPlan plan, NpcSquadTacticsSettings settings)
    {
        // Only one, and it already has it in the air.
        if (plan.ThrownAt != null)
            return;

        FindThrowers(plan, settings, GetSofteningTarget(plan));
    }

    /// <summary>
    ///     As above, for a <paramref name="target"/> the caller has already worked out. The traits in it are kept on the plan
    ///         (<see cref="NpcSquadPlan.TargetTraits"/>) for the facts and costs that follow.
    /// </summary>
    private void FindThrowers(NpcSquadPlan plan, NpcSquadTacticsSettings settings, NpcSofteningTarget target)
    {
        var hunt = plan.Hunt!;

        if (plan.ThrownAt != null)
            return;

        plan.FlashThrow = null;
        plan.FragThrow = null;
        plan.SmokeThrow = null;
        plan.SmokeGasses = false;
        plan.TargetTraits = target.Traits;

        _entranceAimClear.Clear();
        var canFlash = (target.Traits & NpcContactTraits.Flashable) != 0x0;
        bool? squadOnInternals = null;
        bool? squadSeesThroughSmoke = null;

        foreach (var (memberUid, staging) in hunt.StagedMembers)
        {
            _npcHandsSystem.GetCarried(memberUid, _carried);

            // Where this member's throw would go, worked out when first wanted and then for every item it carries.
            EntityCoordinates? throwCoordinates = null;

            foreach (var itemUid in _carried)
            {
                if (_flashOnTriggerQuery.HasComp(itemUid))
                {
                    if (canFlash && plan.FlashThrow == null)
                    {
                        throwCoordinates ??= GetThrowCoordinates(plan, target.Aim, memberUid, staging);
                        plan.FlashThrow = new NpcSquadThrow(memberUid, itemUid, throwCoordinates.Value);
                    }

                    continue;
                }

                if (_roomSmokeQuery.HasComp(itemUid))
                {
                    // Gas over a screen, and either over nothing.
                    squadOnInternals ??= CanSquadGoOnInternals();
                    squadSeesThroughSmoke ??= DoesSquadSeeThroughSmoke();

                    if (!plan.SmokeGasses &&
                        TryGetSmokeUse(itemUid, target.Traits, squadOnInternals.Value, squadSeesThroughSmoke.Value, out var gasses) &&
                        (gasses || plan.SmokeThrow == null))
                    {
                        throwCoordinates ??= GetThrowCoordinates(plan, target.Aim, memberUid, staging);
                        plan.SmokeThrow = new NpcSquadThrow(memberUid, itemUid, throwCoordinates.Value);
                        plan.SmokeGasses = gasses;
                    }

                    continue;
                }

                if (plan.FragThrow != null || !IsGrenade(itemUid))
                    continue;

                // The view of the aim from the doorway is the same for everyone stacked on that way in.
                if (!_entranceAimClear.TryGetValue(staging.EntranceIndex, out var aimClear))
                {
                    aimClear = IsAimClearFromEntrance(plan, target.Aim, staging);
                    _entranceAimClear[staging.EntranceIndex] = aimClear;
                }

                EntityCoordinates coordinates;
                if (aimClear)
                {
                    coordinates = target.Aim!.Value;
                }
                else
                {
                    throwCoordinates ??= GetThrowCoordinates(plan, target.Aim, memberUid, staging);
                    coordinates = throwCoordinates.Value;
                }

                if (IsClearOfEveryone(hunt, coordinates, settings.PlanFragClearance))
                    plan.FragThrow = new NpcSquadThrow(memberUid, itemUid, coordinates);
            }
        }
    }

    /// <summary>
    ///     Only where <see cref="GetSofteningTarget"/> would aim, for a throw being carried out, which has no use for what the
    ///         hostile is like: a plan hostile is not asked about its traits every update.
    /// </summary>
    private EntityCoordinates? GetSofteningAim(NpcSquadPlan plan)
    {
        if (plan.Hunt is { } hunt && hunt.Soften == plan)
            return GetBelievedSofteningTarget(hunt).Aim;

        return TerminatingOrDeleted(plan.TargetUid) ? null : _transformSystem.GetMoverCoordinates(plan.TargetUid);
    }

    /// <summary>
    ///     What a throw for <paramref name="plan"/> goes by. A plan's hostile is one the squad can see, so it goes by the hostile
    ///         itself. A hunt's softening goes by what the squad last saw of it, and where it believes it is now.
    /// </summary>
    private NpcSofteningTarget GetSofteningTarget(NpcSquadPlan plan)
    {
        if (plan.Hunt is { } hunt && hunt.Soften == plan)
            return GetBelievedSofteningTarget(hunt);

        if (TerminatingOrDeleted(plan.TargetUid))
            return default;

        return new NpcSofteningTarget(_transformSystem.GetMoverCoordinates(plan.TargetUid), _npcPerceptionSystem.GetTraits(plan.TargetUid));
    }

    /// <summary>
    ///     Where a grenade would land, going by the view from the doorway, which is where it goes in through. Shut or open: a
    ///         plan has to be able to choose this before there is anything to throw it through, and the choice has to hold
    ///         once there is, from wherever the thrower stands beside it.
    /// </summary>
    private EntityCoordinates GetFragLanding(NpcSquadPlan plan, EntityCoordinates? aim, EntityUid memberUid, NpcHuntStaging staging)
    {
        return IsAimClearFromEntrance(plan, aim, staging) ? aim!.Value : GetThrowCoordinates(plan, aim, memberUid, staging);
    }

    private bool IsAimClearFromEntrance(NpcSquadPlan plan, EntityCoordinates? aim, NpcHuntStaging staging)
    {
        var entrance = plan.Hunt!.Entrances[staging.EntranceIndex];

        return aim is { } at &&
            _npcLineOfFireSystem.IsThrowClear(_transformSystem.ToMapCoordinates(entrance.EntryCoordinates), _transformSystem.ToMapCoordinates(at));
    }

    /// <summary>
    ///     At the hostile, if a throw from where <paramref name="memberUid"/> stands would get there, and otherwise just inside.
    /// </summary>
    private EntityCoordinates GetThrowCoordinates(NpcSquadPlan plan, EntityCoordinates? aim, EntityUid memberUid, NpcHuntStaging staging)
    {
        return aim is { } at &&
            _npcLineOfFireSystem.IsThrowClear(_transformSystem.GetMapCoordinates(memberUid), _transformSystem.ToMapCoordinates(at))
                ? at
                : plan.Hunt!.Entrances[staging.EntranceIndex].EntryCoordinates;
    }

    /// <summary>
    ///     Something to throw that blows up. Something that sticks is a charge.
    /// </summary>
    private bool IsGrenade(EntityUid itemUid)
    {
        return _explosiveQuery.HasComp(itemUid) && _timerTriggerQuery.HasComp(itemUid) && !_stickyQuery.HasComp(itemUid);
    }

    /// <summary>
    ///     Whether <paramref name="coordinates"/> is far enough from everyone, going by where each will be standing when it
    ///         matters: on its way in, if it has one.
    /// </summary>
    private bool IsClearOfEveryone(NpcHunt hunt, EntityCoordinates coordinates, float clearance)
    {
        foreach (var memberUid in _members)
        {
            var standCoordinates = hunt.StagedMembers.TryGetValue(memberUid, out var staging)
                ? hunt.Entrances[staging.EntranceIndex].StageCoordinates
                : Transform(memberUid).Coordinates;

            if (!standCoordinates.TryDistance(EntityManager, _transformSystem, coordinates, out var distance) || distance < clearance)
                return false;
        }

        foreach (var memberUid in _recovering)
        {
            if (!Transform(memberUid).Coordinates.TryDistance(EntityManager, _transformSystem, coordinates, out var distance) || distance < clearance)
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Whether the hostile has a gun that can reach any member. Lasers go through what stops bullets, so a squad behind glass
    ///         can be shot by what it cannot shoot.
    /// </summary>
    private bool CanBeHit(NpcSquadPlan plan)
    {
        if (TerminatingOrDeleted(plan.TargetUid) || !_gunSystem.TryGetGun(plan.TargetUid, out _))
            return false;

        var shotMask = _npcLineOfFireSystem.GetShotMask(plan.TargetUid);
        var hostileMapCoordinates = _transformSystem.GetMapCoordinates(plan.TargetUid);

        foreach (var memberUid in _members)
        {
            if (_npcLineOfFireSystem.IsClear(hostileMapCoordinates, _transformSystem.GetMapCoordinates(memberUid), shotMask))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether <paramref name="doorUid"/> is a firelock shut against vacuum or fire: going through that is not the way to
    ///         get at anyone.
    /// </summary>
    private bool HoldsBackDanger(EntityUid doorUid)
    {
        return _firelockQuery.TryComp(doorUid, out var firelockComponent) && firelockComponent.IsLocked;
    }

    /// <summary>
    ///     What going the long way round costs on top of the walk: as much as blowing a wall for a detour of
    ///         <see cref="NpcSquadTacticsSettings.MaxDetour"/>, so a longer one loses to a charge. The walk alone is time,
    ///         and time is cheap next to a charge's: it takes a long way round for that to tell.
    /// </summary>
    private static float GetDetourCost(NpcHunt hunt, NpcSquadTacticsSettings settings)
    {
        var longest = 0f;

        foreach (var staging in hunt.StagedMembers.Values)
        {
            longest = Math.Max(longest, staging.Detour);
        }

        return longest * BlowWallCost / Math.Max(settings.MaxDetour, 1f);
    }

    /// <summary>
    ///     How far the member with furthest to go has to walk to its way in, along the way it will take: a door just the other
    ///         side of a wall can be a long way round.
    /// </summary>
    private static float GetLongestWalk(NpcHunt hunt)
    {
        var longest = 0;

        foreach (var staging in hunt.StagedMembers.Values)
        {
            longest = Math.Max(longest, staging.RouteLength);
        }

        return longest;
    }

    /// <summary>
    ///     Works out who can see the hostile, has a round to fire, and would break what stops it in fair time.
    /// </summary>
    private void FindBreakers(NpcSquadPlan plan, NpcSquadTacticsSettings settings)
    {
        plan.Breakers.Clear();
        plan.BreakDamage = 0f;

        if (TerminatingOrDeleted(plan.TargetUid))
            return;

        var targetMapCoordinates = _transformSystem.GetMapCoordinates(plan.TargetUid);

        foreach (var memberUid in _members)
        {
            if (!_npcPerceptionSystem.TryGetContact(memberUid, plan.TargetUid, out var contact) ||
                contact.State != NpcContactState.Visible ||
                !_gunSystem.TryGetGun(memberUid, out var gunEntity))
                continue;

            if (GetAmmo(gunEntity.Owner).Count <= 0)
                continue;

            var shotMask = _npcLineOfFireSystem.GetShotMask(memberUid);

            if (!_npcLineOfFireSystem.TryGetFirstHit(_transformSystem.GetMapCoordinates(memberUid), targetMapCoordinates, shotMask, out var hit) ||
                !TryGetBreakDamage(hit.HitEntity, shotMask, out var damage) ||
                damage > settings.PlanBreakDamage)
                continue;

            plan.Breakers[memberUid] = hit.HitEntity;
            plan.BreakDamage = MathF.Max(plan.BreakDamage, damage);
        }
    }

    /// <summary>
    ///     How much damage it takes to clear the tile <paramref name="barrierUid"/> is on of everything a shot would hit. A
    ///         window often stands on a grille, which has to go too. Not for a tile with a door in it, or with anything
    ///         that cannot be broken.
    /// </summary>
    private bool TryGetBreakDamage(EntityUid barrierUid, CollisionGroup shotMask, out float damage)
    {
        damage = 0f;

        var transformComponent = Transform(barrierUid);
        if (!transformComponent.Anchored ||
            !_npcGridSystem.TryGetGrid(transformComponent.GridUid, out var grid))
            return false;

        var anchoredEnumerator = _mapSystem.GetAnchoredEntities(grid.Owner, grid.Comp, _mapSystem.TileIndicesFor(grid, transformComponent.Coordinates));
        while (anchoredEnumerator.MoveNext(out var anchoredUid))
        {
            if (_doorQuery.HasComp(anchoredUid.Value))
                return false;

            if (!StopsShot(anchoredUid.Value, shotMask))
                continue;

            if (!TryGetBreakThreshold(anchoredUid.Value, out var threshold))
                return false;

            damage += threshold.Float();
        }

        return damage > 0f;
    }

    /// <summary>
    ///     How much damage destroys <paramref name="uid"/>. False for something that cannot be.
    /// </summary>
    private bool TryGetBreakThreshold(EntityUid uid, out FixedPoint2 threshold)
    {
        if (_destructibleSystem.TryGetDestroyedAt(uid, out var destroyedAt) &&
            destroyedAt is { } destroyedAtValue &&
            destroyedAtValue != FixedPoint2.MaxValue)
        {
            threshold = destroyedAtValue;
            return true;
        }

        threshold = default;
        return false;
    }

    private bool StopsShot(EntityUid uid, CollisionGroup shotMask)
    {
        if (!_fixturesQuery.TryComp(uid, out var fixturesComponent))
            return false;

        foreach (var fixture in fixturesComponent.Fixtures.Values)
        {
            if (fixture.Hard && (fixture.CollisionLayer & (int) shotMask) != 0x0)
                return true;
        }

        return false;
    }

    private bool UpdatePlanCooldown(EntityUid issuerUid, NpcSquadPlanComponent planComponent, TimeSpan now)
    {
        var plan = planComponent.Plan;

        if (plan.HoldsOff && !TerminatingOrDeleted(plan.TargetUid) && LookForShot(out var targetUid) == NpcSquadSight.Stuck)
        {
            // Still nothing to shoot: straight to looking for a way again. Not through the grace first, which is for a
            //      squad that may yet find itself a shot, as this one has had the chance to.
            if (now >= plan.CooldownEndsAt)
            {
                plan = new NpcSquadPlan { TargetUid = targetUid, StartedAt = now, Reason = "still nobody has a shot", HoldsOff = true };
                planComponent.Plan = plan;
                StartProbe(plan);
            }

            return HoldOff(issuerUid, plan.TargetUid, now);
        }

        if (now >= plan.CooldownEndsAt)
            RemovePlan(issuerUid);

        return false;
    }

    /// <summary>
    ///     Everyone stays where they are, watching the hostile, for as long as there is nothing to be done about it. Each
    ///         left to itself would go after a shot there is not: looking for a firing spot, closing in, giving up, again.
    ///         One seeing to its wounds is left to it. Returns that the squad is in hand.
    /// </summary>
    private bool HoldOff(EntityUid issuerUid, EntityUid targetUid, TimeSpan now)
    {
        var targetMapCoordinates = _transformSystem.GetMapCoordinates(targetUid);

        using var planOrders = GivePlanOrders();

        foreach (var memberUid in _members)
        {
            if (_npcRecoverySystem.IsRecovering(memberUid))
                continue;

            OrderWatchToward(memberUid, issuerUid, targetMapCoordinates, now);
        }

        return true;
    }

    /// <summary>
    ///     Stands the plan down as one that worked, or as good as: it waits out <see cref="NpcSquadTacticsSettings.PlanSuccessCooldown"/>.
    /// </summary>
    private void SucceedPlan(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now, string reason)
    {
        EndPlan(issuerUid, plan, settings.PlanSuccessCooldown, now, reason);
    }

    /// <summary>
    ///     Stands the plan down as one that came to nothing: it waits out <see cref="NpcSquadTacticsSettings.PlanCooldown"/>,
    ///         holding off (see <see cref="NpcSquadPlan.HoldsOff"/>).
    /// </summary>
    private void FailPlan(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now, string reason)
    {
        EndPlan(issuerUid, plan, settings.PlanCooldown, now, reason, holdOff: true);
    }

    /// <summary>
    ///     Stands the plan down, leaving the component for <paramref name="cooldown"/> so that a squad that could do nothing
    ///         does not spend its time working that out again.
    /// </summary>
    /// <param name="holdOff">Whether it came to nothing: see <see cref="NpcSquadPlan.HoldsOff"/>.</param>
    private void EndPlan(EntityUid issuerUid, NpcSquadPlan plan, TimeSpan cooldown, TimeSpan now, string reason, bool holdOff = false)
    {
        ClearOrders(issuerUid, _members);
        ReleaseCharge(plan);

        plan.State = NpcSquadPlanState.Cooldown;
        plan.CooldownStartedAt = now;
        plan.CooldownEndsAt = now + cooldown;
        plan.HoldsOff = holdOff;
        plan.Hunt = null;
        plan.StepCount = 0;
        plan.Reason = reason;
    }

    /// <summary>
    ///     Throws away a plan that will not be updated again - a lone NPC's that has joined a squad, or a squad's that has
    ///         lost its leader - letting go of any charge's wire on the way, so nobody is left tied to a wall.
    /// </summary>
    private void DropPlan(EntityUid issuerUid)
    {
        if (!_squadPlanQuery.TryComp(issuerUid, out var planComponent))
            return;

        ReleaseCharge(planComponent.Plan);
        RemComp<NpcSquadPlanComponent>(issuerUid);
    }

    private void RemovePlan(EntityUid issuerUid)
    {
        RemComp<NpcSquadPlanComponent>(issuerUid);

        if (_npcSquadPlanDebugSystem.IsTracking(issuerUid, _members))
            _npcSquadPlanDebugSystem.SendEnded(issuerUid, _members);
    }
}
