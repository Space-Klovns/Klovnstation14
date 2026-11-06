using System.Threading;
using Content.Server._KS14.NPC.Doors;
using Content.Server._KS14.NPC.LineOfFire;
using Content.Server.NPC.Pathfinding;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._KS14.NPC;
using Content.Shared.Destructible;
using Content.Shared.Doors.Components;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Events;
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
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;
    [Dependency] private NpcSquadPlanDebugSystem _npcSquadPlanDebugSystem = default!;
    [Dependency] private PathfindingSystem _pathfindingSystem = default!;
    [Dependency] private SharedDestructibleSystem _destructibleSystem = default!;

    [Dependency] private EntityQuery<FirelockComponent> _firelockQuery = default!;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<NpcSquadPlanComponent> _squadPlanQuery = default!;

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

    private static readonly TimeSpan PlanReviewInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Whether the orders being given now are for a plan. Set around the hunt's own code, which gives them.
    /// </summary>
    private bool _givingPlanOrders;

    private readonly List<EntityUid> _combatContacts = new();

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
            if (!settings.Planning || LookForShot(out var targetUid) != NpcSquadSight.Stuck)
                return false;

            planComponent = EnsureComp<NpcSquadPlanComponent>(issuerUid);
            planComponent.Plan = new NpcSquadPlan { TargetUid = targetUid, StartedAt = now, Reason = "nobody has a shot" };
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
            LastKnownCoordinates = targetCoordinates,
            PredictedCoordinates = targetCoordinates,
            StartedAt = now,
            PhaseStartedAt = now,
            Phase = NpcHuntPhase.Stage,
        };

        PrepareRoom(leaderUid, plan.Hunt, settings);
        AssignEntrances(plan.Hunt, settings);
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

        if (LookForShot(out _) == NpcSquadSight.HasShot)
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
        for (var i = _members.Count - 1; i >= 0; i--)
        {
            if (!_npcRecoverySystem.IsRecovering(_members[i]))
                continue;

            ClearOrder(_members[i], issuerUid);
            _members.RemoveAt(i);
        }

        if (_members.Count == 0)
        {
            EndPlan(issuerUid, plan, settings.PlanCooldown, now, "nobody left");
            return true;
        }

        var replan = false;

        // Somebody down, or somebody joined: who does what has to be worked out again.
        if (_members.Count != plan.MemberCount)
        {
            plan.MemberCount = _members.Count;
            AssignEntrances(plan.Hunt!, settings);
            plan.Stacked = false;
            SetPhase(plan.Hunt!, NpcHuntPhase.Stage, now);
            replan = true;
        }

        if (now >= plan.NextReview)
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
                    finished = RunEnter(issuerUid, plan, settings, now);
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

            // Gone in, or shot out the glass, and still nobody has a shot. Nothing above ever saw one, so it is done.
            EndPlan(issuerUid, plan, settings.PlanSuccessCooldown, now, "done, and nobody has a shot");
            break;
        }

        _givingPlanOrders = false;
        return true;
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
        costs[(int) NpcSquadAction.Enter] = EnterCost;
        costs[(int) NpcSquadAction.BreakGlass] = BreakGlassBaseCost + plan.BreakDamage / BreakGlassDamagePerSecond;

        Span<NpcSquadAction> steps = stackalloc NpcSquadAction[NpcSquadGoap.MaxSteps];

        if (!NpcSquadGoap.TryPlan(plan.Facts, NpcSquadFact.HasShot, costs, plan.BannedActions, steps, out var length, out var cost) ||
            length == 0)
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

    private NpcSquadFact GetFacts(NpcSquadPlan plan, NpcSquadTacticsSettings settings)
    {
        var hunt = plan.Hunt!;
        var facts = plan.Stacked ? NpcSquadFact.Stacked : NpcSquadFact.None;

        foreach (var staging in hunt.StagedMembers.Values)
        {
            facts |= NpcSquadFact.HasWayIn;

            if (!staging.IsLead)
                continue;

            var entrance = hunt.Entrances[staging.EntranceIndex];

            // Not a way in at all, however it would be opened.
            if (entrance.DoorUid is { } doorUid && HoldsBackDanger(doorUid))
                continue;

            facts |= GetDoorToForce(staging, entrance) == null ? NpcSquadFact.WayOpen : NpcSquadFact.DoorForceable;
        }

        FindBreakers(plan, settings);
        if (plan.Breakers.Count > 0)
            facts |= NpcSquadFact.GlassBreakable;

        return facts;
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
    ///     How far the member with furthest to go has to walk to its way in.
    /// </summary>
    private float GetLongestWalk(NpcHunt hunt)
    {
        var longest = 0f;

        foreach (var (memberUid, staging) in hunt.StagedMembers)
        {
            if (!TerminatingOrDeleted(memberUid) &&
                Transform(memberUid).Coordinates.TryDistance(EntityManager, _transformSystem, hunt.Entrances[staging.EntranceIndex].StageCoordinates, out var distance))
                longest = MathF.Max(longest, distance);
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
