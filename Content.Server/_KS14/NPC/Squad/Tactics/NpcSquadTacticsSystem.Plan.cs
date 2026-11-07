using System.Threading;
using Content.Server._KS14.NPC.Doors;
using Content.Server._KS14.NPC.Components;
using Content.Server._KS14.NPC.Grenades;
using Content.Server._KS14.NPC.LineOfFire;
using Content.Server.NPC.Pathfinding;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._KS14.NPC;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Destructible;
using Content.Shared.Doors.Components;
using Content.Shared.Explosion.Components;
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
    [Dependency] private SharedFlashSystem _flashSystem = default!;

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
            else if (LookForShot(out var targetUid) == NpcSquadSight.Stuck)
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
            _ => UpdatePlanCooldown(issuerUid, plan, now),
        };

        if (_squadPlanQuery.HasComp(issuerUid) && _npcSquadPlanDebugSystem.IsTracking(issuerUid, _members))
            _npcSquadPlanDebugSystem.SendFrame(issuerUid, plan, _members);

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
            _combatContacts.Clear();
            _npcPerceptionSystem.GetCombatContacts(memberUid, _combatContacts);

            if (_combatContacts.Count == 0)
                continue;

            var memberMapCoordinates = _transformSystem.GetMapCoordinates(memberUid);
            var shotMask = _npcLineOfFireSystem.GetShotMask(memberUid);

            foreach (var hostileUid in _combatContacts)
            {
                if (TerminatingOrDeleted(hostileUid) || _mobStateSystem.IsIncapacitated(hostileUid))
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

            _combatContacts.Clear();
            _npcPerceptionSystem.GetCombatContacts(memberUid, _combatContacts);

            if (_combatContacts.Count > 0)
                threatUid = _combatContacts[0];
        }

        return threatUid.IsValid() && health / _members.Count <= settings.PlanWithdrawHealth;
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

        if (plan.Probe is not { IsCompleted: true } probe)
            return false;

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

                freePath = !TerminatingOrDeleted(probeMemberUid) && !_npcDoorSystem.RunsThroughShutDoor(probeMemberUid, result.Path);
            }
        }

        plan.Probe = null;

        if (freePath)
        {
            EndPlan(issuerUid, plan, settings.PlanSuccessCooldown, now, "a free way: they walk it themselves");
            return false;
        }

        var targetCoordinates = _transformSystem.GetMoverCoordinates(plan.TargetUid);
        plan.Hunt = new NpcHunt
        {
            TargetUid = null,
            Settings = settings,
            AllowCharges = true,
            LastKnownCoordinates = targetCoordinates,
            PredictedCoordinates = targetCoordinates,
            StartedAt = now,
            PhaseStartedAt = now,
            Phase = NpcHuntPhase.Stage,
        };

        if (PrepareRoom(leaderUid, plan.Hunt, settings) == null)
            PrepareSealedRoom(plan);

        AssignEntrances(plan.Hunt, settings);
        LeaveAWatcher(plan);
        plan.MemberCount = _members.Count;

        if (!TryMakePlan(plan, now))
        {
            EndPlan(issuerUid, plan, settings.PlanCooldown, now, "nothing to be done");
            return false;
        }

        plan.State = NpcSquadPlanState.Running;
        plan.StartedAt = now;
        plan.NextReview = now + PlanReviewInterval;
        return RunPlanSteps(issuerUid, plan, settings, now);
    }

    private bool UpdatePlanSteps(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (TerminatingOrDeleted(plan.TargetUid) || _mobStateSystem.IsIncapacitated(plan.TargetUid) || AnyoneKnowsDead(plan.TargetUid))
        {
            EndPlan(issuerUid, plan, settings.PlanSuccessCooldown, now, "the hostile is down");
            return true;
        }

        // Getting out of a fight does not stop for having a shot. Nor does a door they have just opened on a hostile they
        //      mean to soften up, as that is a moment and the shot it gives them is the thing to soften up first, nor a charge
        //      that is on, with everyone on their way out of the blast.
        if (plan.Goal == NpcSquadFact.HasShot && LookForShot(out _) == NpcSquadSight.HasShot && !IsCommitted(plan))
        {
            EndPlan(issuerUid, plan, settings.PlanSuccessCooldown, now, "somebody has a shot");
            return true;
        }

        if (now - plan.StartedAt >= settings.PlanTimeout)
        {
            EndPlan(issuerUid, plan, settings.PlanCooldown, now, "took too long");
            return true;
        }

        // Waiting for its meds to work: it will not come.
        var recovering = 0;

        for (var i = _members.Count - 1; i >= 0; i--)
        {
            if (!_npcRecoverySystem.IsRecovering(_members[i]))
                continue;

            ClearOrder(_members[i], issuerUid);
            _members.RemoveAt(i);
            recovering++;
        }

        if (_members.Count == 0)
        {
            EndPlan(issuerUid, plan, settings.PlanCooldown, now, "nobody left");
            return true;
        }

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

        if (replan && !TryMakePlan(plan, now))
        {
            EndPlan(issuerUid, plan, settings.PlanCooldown, now, "nothing left to try");
            return true;
        }

        return RunPlanSteps(issuerUid, plan, settings, now);
    }

    /// <summary>
    ///     Carries out the current step. One that finishes falls through to the next in the same update, so nobody stands about
    ///         for half a second between one and the next.
    /// </summary>
    private bool RunPlanSteps(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        _givingPlanOrders = true;

        for (var i = 0; i < NpcSquadGoap.MaxSteps; i++)
        {
            bool finished;

            switch (plan.Steps[plan.CurrentStep])
            {
                case NpcSquadAction.StackUp:
                    finished = RunStackUp(issuerUid, plan, settings, now);
                    break;
                case NpcSquadAction.ForceDoor:
                    finished = RunForceDoor(issuerUid, plan, settings, now);
                    break;
                case NpcSquadAction.Enter:
                case NpcSquadAction.EnterSoftened:
                    finished = RunEnter(issuerUid, plan, settings, now);
                    break;
                case NpcSquadAction.Flashbang:
                case NpcSquadAction.FragRoom:
                    finished = RunThrow(issuerUid, plan, plan.Steps[plan.CurrentStep], settings, now);
                    break;
                case NpcSquadAction.TakeCover:
                    finished = RunTakeCover(issuerUid, plan, settings, now);
                    break;
                case NpcSquadAction.HoldExits:
                    finished = RunHoldExits(issuerUid, plan, settings, now);
                    break;
                case NpcSquadAction.PatchUp:
                    finished = RunPatchUp(issuerUid, plan, settings, now);
                    break;
                case NpcSquadAction.TopUp:
                    finished = RunTopUp(issuerUid, plan, settings, now);
                    break;
                case NpcSquadAction.BlowDoor:
                case NpcSquadAction.BlowWall:
                    finished = RunCharge(issuerUid, plan, plan.Steps[plan.CurrentStep], settings, now);
                    break;
                default:
                    finished = RunBreakGlass(issuerUid, plan, settings, now);
                    break;
            }

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
            EndPlan(issuerUid, plan, settings.PlanSuccessCooldown, now, "done, and nobody has a shot");
            break;
        }

        if (plan.State == NpcSquadPlanState.Running)
            KeepWatch(issuerUid, plan, now);

        _givingPlanOrders = false;
        return true;
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
            if (plan.Steps[i] is NpcSquadAction.Flashbang or NpcSquadAction.FragRoom)
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

        foreach (var (memberUid, staging) in hunt.StagedMembers)
        {
            if (staging.IsLead ||
                !_npcPerceptionSystem.TryGetContact(memberUid, plan.TargetUid, out var contact) ||
                contact.State != NpcContactState.Visible)
                continue;

            plan.WatcherUid = memberUid;
            hunt.HeldBackUid = memberUid;
            hunt.StagedMembers.Remove(memberUid);
            return;
        }
    }

    private void KeepWatch(EntityUid issuerUid, NpcSquadPlan plan, TimeSpan now)
    {
        if (plan.WatcherUid is not { } watcherUid ||
            !_members.Contains(watcherUid) ||
            plan.Steps[plan.CurrentStep] is NpcSquadAction.BreakGlass or NpcSquadAction.TakeCover or NpcSquadAction.PatchUp or NpcSquadAction.TopUp ||
            plan.Charge is { Phase: not NpcSquadChargePhase.Planting } ||
            TerminatingOrDeleted(plan.TargetUid))
            return;

        var standCoordinates = _transformSystem.GetMoverCoordinates(watcherUid);
        var facing = (_transformSystem.GetMapCoordinates(plan.TargetUid).Position - _transformSystem.GetMapCoordinates(watcherUid).Position).ToWorldAngle();

        SetOrder(watcherUid, issuerUid, NpcOrderKind.Watch, standCoordinates, ToLocalFacing(standCoordinates, facing), OrderRange, null, now);
    }

    /// <summary>
    ///     One member puts something in through the way in, and the rest wait where they are until it has gone off: a set time
    ///         for a flashbang, and for a grenade, until it is gone.
    /// </summary>
    private bool RunThrow(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadAction action, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var hunt = plan.Hunt!;

        if (plan.ThrownAt is { } thrownAt)
        {
            var wentOff = action == NpcSquadAction.Flashbang
                ? now - thrownAt >= settings.PlanFlashWait
                : TerminatingOrDeleted(plan.ThrownUid) || now - thrownAt >= FragWaitLimit;

            if (!wentOff)
                return false;

            plan.Softened = true;
            return true;
        }

        var chosen = action == NpcSquadAction.Flashbang ? plan.FlashThrow : plan.FragThrow;

        if (chosen is { } thrown && (TerminatingOrDeleted(thrown.GrenadeUid) || _thrownGrenadeQuery.HasComp(thrown.GrenadeUid)))
        {
            plan.ThrownAt = now;
            plan.ThrownUid = thrown.GrenadeUid;
            return false;
        }

        if (chosen is not { } toThrow || !_members.Contains(toThrow.ThrowerUid) || now - plan.StepStartedAt >= ThrowTimeout)
        {
            FailPlanStep(issuerUid, plan, action, settings, now);
            return false;
        }

        foreach (var memberUid in _members)
        {
            if (memberUid == toThrow.ThrowerUid)
            {
                SetOrder(memberUid, issuerUid, NpcOrderKind.ThrowGrenade, toThrow.Coordinates, Angle.Zero, OrderRange, null, now, toolUid: toThrow.GrenadeUid);
                continue;
            }

            HoldStagedMember(memberUid, issuerUid, hunt, now);
        }

        return false;
    }

    private void HoldStagedMember(EntityUid memberUid, EntityUid issuerUid, NpcHunt hunt, TimeSpan now)
    {
        if (!hunt.StagedMembers.TryGetValue(memberUid, out var staging))
            return;

        var entrance = hunt.Entrances[staging.EntranceIndex];
        SetOrder(memberUid, issuerUid, NpcOrderKind.Stage, entrance.StageCoordinates, entrance.LocalFacing, OrderRange, null, now);
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

        if (plan.Patients.Count == 0 && !recovering || now - plan.StepStartedAt >= settings.PlanPatchTime)
        {
            plan.PatchedUp = true;
            return true;
        }

        foreach (var memberUid in _members)
        {
            if (plan.Patients.Contains(memberUid))
                SetOrder(memberUid, issuerUid, NpcOrderKind.PatchUp, Transform(memberUid).Coordinates, Angle.Zero, OrderRange, null, now);
            else
                HoldStagedMember(memberUid, issuerUid, plan.Hunt!, now);
        }

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

        foreach (var memberUid in _members)
        {
            if (plan.Reloaders.Contains(memberUid))
                SetOrder(memberUid, issuerUid, NpcOrderKind.TopUp, Transform(memberUid).Coordinates, Angle.Zero, OrderRange, null, now);
            else
                HoldStagedMember(memberUid, issuerUid, plan.Hunt!, now);
        }

        return false;
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

            _carried.Clear();
            _npcDoorSystem.GetCarried(memberUid, _carried);

            foreach (var itemUid in _carried)
            {
                if (!_healingItemQuery.HasComp(itemUid) || !_solutionManagerQuery.HasComp(itemUid) && !_solutionQuery.HasComp(itemUid))
                    continue;

                plan.Patients.Add(memberUid);
                break;
            }
        }
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

            var ammoEvent = new GetAmmoCountEvent();
            RaiseLocalEvent(gunEntity.Owner, ref ammoEvent);

            if (ammoEvent.Capacity <= 0 ||
                (float) ammoEvent.Count / ammoEvent.Capacity >= settings.PlanReloadFraction ||
                !_itemSlotsQuery.TryComp(gunEntity.Owner, out var itemSlotsComponent) ||
                !_itemSlotsSystem.TryGetSlot(gunEntity.Owner, SharedGunSystem.MagazineSlot, out var magazineSlot, itemSlotsComponent))
                continue;

            _carried.Clear();
            _npcDoorSystem.GetCarried(memberUid, _carried);

            foreach (var itemUid in _carried)
            {
                if (!_ballisticAmmoQuery.HasComp(itemUid) || _entityWhitelistSystem.IsWhitelistFailOrNull(magazineSlot.Whitelist, itemUid))
                    continue;

                plan.Reloaders.Add(memberUid);
                break;
            }
        }
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

        if (hunt.Phase != NpcHuntPhase.Stage)
            SetPhase(hunt, NpcHuntPhase.Stage, now);

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

        if (hunt.Phase != NpcHuntPhase.Breach)
            SetPhase(hunt, NpcHuntPhase.Breach, now);

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

        if (hunt.Phase != NpcHuntPhase.Entry)
            SetPhase(hunt, NpcHuntPhase.Entry, now);

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
            EndPlan(issuerUid, plan, settings.PlanCooldown, now, $"{action} failed, and nothing else to try");
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
        costs[(int) NpcSquadAction.StackUp] = GetLongestWalk(plan.Hunt) / PlanWalkSpeed;
        costs[(int) NpcSquadAction.ForceDoor] = ForceDoorCost;
        costs[(int) NpcSquadAction.Enter] = EnterCost + GetRushRisk(plan, settings);
        costs[(int) NpcSquadAction.EnterSoftened] = EnterCost;
        costs[(int) NpcSquadAction.Flashbang] = FlashbangCost;
        costs[(int) NpcSquadAction.FragRoom] = FragCost;
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
        return _gunSystem.TryGetGun(plan.TargetUid, out _) ? settings.PlanRushRisk : 0f;
    }

    private NpcSquadFact GetFacts(NpcSquadPlan plan, NpcSquadTacticsSettings settings)
    {
        var hunt = plan.Hunt!;
        var facts = plan.Stacked ? NpcSquadFact.Stacked : NpcSquadFact.None;

        if (plan.Softened)
            facts |= NpcSquadFact.Softened;

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

        if (AreExitsCovered(hunt))
            facts |= NpcSquadFact.ExitsCovered;

        FindBreakers(plan, settings);
        if (plan.Breakers.Count > 0)
            facts |= NpcSquadFact.GlassBreakable;

        FindThrowers(plan, settings);
        if (plan.FlashThrow != null)
            facts |= NpcSquadFact.HasFlashbang;

        if (plan.FragThrow != null)
            facts |= NpcSquadFact.HasFrag;

        if (CanBeHit(plan))
            facts |= NpcSquadFact.Threatened;

        var armed = !TerminatingOrDeleted(plan.TargetUid) && _gunSystem.TryGetGun(plan.TargetUid, out _);

        FindPatients(plan, settings);
        if (plan.PatchedUp || !armed || plan.Patients.Count == 0)
            facts |= NpcSquadFact.Patched;

        FindReloaders(plan, settings);
        if (plan.ToppedUp || plan.Reloaders.Count == 0)
            facts |= NpcSquadFact.Loaded;

        if (AnyoneHasACharge())
            facts |= NpcSquadFact.HasCharge;

        FindCharges(plan, settings);
        if (plan.DoorCharge != null)
            facts |= NpcSquadFact.DoorChargeable;

        if (plan.WallCharge != null)
            facts |= NpcSquadFact.WallChargeable;

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
    ///         leaves all of them out of the blast, which just inside the door hardly ever does.
    /// </summary>
    private void FindThrowers(NpcSquadPlan plan, NpcSquadTacticsSettings settings)
    {
        var hunt = plan.Hunt!;

        // Only one, and it already has it in the air.
        if (plan.ThrownAt != null)
            return;

        plan.FlashThrow = null;
        plan.FragThrow = null;

        var canFlash = !TerminatingOrDeleted(plan.TargetUid) && _flashSystem.CanFlash(plan.TargetUid);

        foreach (var (memberUid, staging) in hunt.StagedMembers)
        {
            _carried.Clear();
            _npcDoorSystem.GetCarried(memberUid, _carried);

            foreach (var itemUid in _carried)
            {
                if (_flashOnTriggerQuery.HasComp(itemUid))
                {
                    if (canFlash)
                        plan.FlashThrow ??= new NpcSquadThrow(memberUid, itemUid, GetThrowCoordinates(plan, memberUid, staging));

                    continue;
                }

                if (plan.FragThrow != null || !IsGrenade(itemUid))
                    continue;

                var coordinates = GetFragLanding(plan, memberUid, staging);

                if (IsClearOfEveryone(hunt, coordinates, settings.PlanFragClearance))
                    plan.FragThrow = new NpcSquadThrow(memberUid, itemUid, coordinates);
            }
        }
    }

    /// <summary>
    ///     Where a grenade would land. With the door still shut, that is where it will land once it is open, going by the view
    ///         from the doorway: a plan has to be able to choose this before there is anything to throw it through.
    /// </summary>
    private EntityCoordinates GetFragLanding(NpcSquadPlan plan, EntityUid memberUid, NpcHuntStaging staging)
    {
        var entrance = plan.Hunt!.Entrances[staging.EntranceIndex];

        if (!TerminatingOrDeleted(plan.TargetUid) &&
            entrance.DoorUid is { } doorUid &&
            _doorQuery.TryComp(doorUid, out var doorComponent) &&
            doorComponent.State is not (DoorState.Open or DoorState.Opening) &&
            _npcLineOfFireSystem.IsClear(_transformSystem.ToMapCoordinates(entrance.EntryCoordinates), _transformSystem.GetMapCoordinates(plan.TargetUid), CollisionGroup.ItemMask))
            return _transformSystem.GetMoverCoordinates(plan.TargetUid);

        return GetThrowCoordinates(plan, memberUid, staging);
    }

    private EntityCoordinates GetThrowCoordinates(NpcSquadPlan plan, EntityUid memberUid, NpcHuntStaging staging)
    {
        return TerminatingOrDeleted(plan.TargetUid) ||
            !_npcLineOfFireSystem.IsClear(_transformSystem.GetMapCoordinates(memberUid), _transformSystem.GetMapCoordinates(plan.TargetUid), CollisionGroup.ItemMask)
                ? plan.Hunt!.Entrances[staging.EntranceIndex].EntryCoordinates
                : _transformSystem.GetMoverCoordinates(plan.TargetUid);
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

            var ammoEvent = new GetAmmoCountEvent();
            RaiseLocalEvent(gunEntity.Owner, ref ammoEvent);
            if (ammoEvent.Count <= 0)
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
            transformComponent.GridUid is not { } gridUid ||
            !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return false;

        var anchoredEnumerator = _mapSystem.GetAnchoredEntities(gridUid, mapGridComponent, _mapSystem.TileIndicesFor(gridUid, mapGridComponent, transformComponent.Coordinates));
        while (anchoredEnumerator.MoveNext(out var anchoredUid))
        {
            if (_doorQuery.HasComp(anchoredUid.Value))
                return false;

            if (!StopsShot(anchoredUid.Value, shotMask))
                continue;

            if (!_destructibleSystem.TryGetDestroyedAt(anchoredUid.Value, out var destroyedAt) ||
                destroyedAt is not { } threshold ||
                threshold == Content.Shared.FixedPoint.FixedPoint2.MaxValue)
                return false;

            damage += threshold.Float();
        }

        return damage > 0f;
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

    private bool UpdatePlanCooldown(EntityUid issuerUid, NpcSquadPlan plan, TimeSpan now)
    {
        if (now >= plan.CooldownEndsAt)
            RemovePlan(issuerUid);

        return false;
    }

    /// <summary>
    ///     Stands the plan down, leaving the component for <paramref name="cooldown"/> so that a squad that could do nothing
    ///         does not spend its time working that out again.
    /// </summary>
    private void EndPlan(EntityUid issuerUid, NpcSquadPlan plan, TimeSpan cooldown, TimeSpan now, string reason)
    {
        ClearOrders(issuerUid, _members);

        plan.State = NpcSquadPlanState.Cooldown;
        plan.CooldownEndsAt = now + cooldown;
        plan.Hunt = null;
        plan.StepCount = 0;
        plan.Reason = reason;
    }

    private void RemovePlan(EntityUid issuerUid)
    {
        RemComp<NpcSquadPlanComponent>(issuerUid);

        if (_npcSquadPlanDebugSystem.IsTracking(issuerUid, _members))
            _npcSquadPlanDebugSystem.SendEnded(issuerUid, _members);
    }
}
