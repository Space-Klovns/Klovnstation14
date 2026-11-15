using Content.Shared._KS14.FieldCommander;
using Content.Shared.Damage.Systems;
using Content.Server._KS14.KillCredit;
using Content.Server._KS14.GameTicking.Rules;
using Content.Server._KS14.NPC.Systems;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.SupplyPod.Passengers;
using Content.Server.Chat.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Humanoid.Systems;
using Content.Shared._KS14.RCD;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared._RMC14.Vendors;
using Content.Shared.GameTicking.Components;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Runs an AI field commander: walks its timeline of phases, sends waves bought from a threat budget, strikes the
///         defenders' works, keeps its units pointed at the defenders and lands the uplink that decides the round.
/// </summary>
/// <remarks>
///     Split by concern: <c>.Waves</c> buys and sends units, <c>.Placement</c> finds where things go and keeps the
///         safe zones, <c>.Orders</c> and <c>.Strikes</c> are what the units and missiles are pointed at,
///         <c>.Uplink</c> the final objective, <c>.Announcements</c> both sides' transmissions, <c>.Fog</c> the play
///         area, <c>.Points</c> the defenders' requisition points and <c>.Admin</c> the console API.
/// </remarks>
public sealed partial class KsFieldCommanderRuleSystem : GameRuleSystem<KsFieldCommanderRuleComponent>
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private ScenarioSystem _scenarioSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;
    [Dependency] private MapSystem _mapSystem = default!;
    [Dependency] private TurfSystem _turfSystem = default!;
    [Dependency] private NpcSensorSystem _npcSensorSystem = default!;
    [Dependency] private NpcLineOfSightSystem _npcLineOfSightSystem = default!;
    [Dependency] private RandomHumanoidSystem _randomHumanoidSystem = default!;
    [Dependency] private KsSupplyPodPassengerSystem _supplyPodPassengerSystem = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private KsKillCreditSystem _killCreditSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private KsFieldCommanderFogSystem _fogSystem = default!;
    [Dependency] private KsFieldCommanderDefenderSystem _defenderSystem = default!;
    [Dependency] private KsFieldCommanderBeaconSystem _beaconSystem = default!;
    [Dependency] private KsNoBuildZoneSystem _noBuildZoneSystem = default!;
    [Dependency] private SharedCMAutomatedVendorSystem _automatedVendorSystem = default!;
    [Dependency] private EntityQuery<ScenarioFactionMemberComponent> _scenarioFactionMemberQuery = default!;

    protected override void Started(EntityUid uid, KsFieldCommanderRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.NextOrderTime = _gameTiming.CurTime + component.OrderInterval;
    }

    protected override void Ended(EntityUid uid, KsFieldCommanderRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        // A rule ended mid-round, by an admin, takes its fog and no-build zones with it.
        ClearFog(component);
        ClearWaveStatus(component);
        AllowBuildingInSafeZones(component);
    }

    protected override void ActiveTick(EntityUid uid, KsFieldCommanderRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (!component.Prepared && !TryPrepare(component))
            return;

        var curTime = _gameTiming.CurTime;
        var elapsed = GetElapsed(component, gameRule, curTime);

        // The defenders' own command speaks first, before anything of the commander's.
        if (!component.BriefingSent && curTime - gameRule.ActivatedAt >= component.BriefingDelay)
        {
            component.BriefingSent = true;
            if (component.BriefingAnnouncement is { } briefing)
                SendIntel(component, Loc.GetString(briefing), sound: true);
        }

        SendDueIntel(component, curTime);

        while (component.PhaseIndex + 1 < component.Phases.Count &&
               elapsed >= component.Phases[component.PhaseIndex + 1].Start)
        {
            EnterPhase((uid, component), component.PhaseIndex + 1, curTime);
        }

        if (curTime >= component.NextWaveTime)
            ForceWave((uid, component));

        if (curTime >= component.NextOrderTime)
        {
            component.NextOrderTime = curTime + component.OrderInterval;
            GiveOrders((uid, component));
        }

        UpdateUplink(component, curTime);
    }

    /// <summary>
    ///     Sets up the battlefield (see <see cref="KsFieldCommanderRuleComponent.Prepared"/>). False, to be tried again
    ///         next tick, until the map with the defenders' objectives is there and a scenario rule is running to count
    ///         the commander's side.
    /// </summary>
    private bool TryPrepare(KsFieldCommanderRuleComponent component)
    {
        var objectiveUids = new List<EntityUid>();
        var objectiveQuery = EntityQueryEnumerator<ScenarioObjectiveComponent>();
        while (objectiveQuery.MoveNext(out var objectiveUid, out var objectiveComponent))
        {
            if (objectiveComponent.FactionId == component.DefenderFaction)
                objectiveUids.Add(objectiveUid);
        }

        if (objectiveUids.Count == 0 ||
            !_scenarioSystem.SetPersistent(component.Faction, persistent: true) ||
            !_scenarioSystem.SetObjectivePending(component.Faction, pending: true))
            return false;

        // The commander decides how the round ends; an objective must not hand it to the defenders by lasting.
        foreach (var objectiveUid in objectiveUids)
            RemComp<TimedDespawnComponent>(objectiveUid);

        FindSafeZones(component);
        ForbidBuildingInSafeZones(component);
        PrepareExistingDefenders(component);
        RaiseFog(component);
        PrepareWaveStatus(component);
        component.Prepared = true;
        return true;
    }

    /// <summary>
    ///     How far along the timeline the rule is, counting any phase set by hand.
    /// </summary>
    private static TimeSpan GetElapsed(KsFieldCommanderRuleComponent component, GameRuleComponent gameRule, TimeSpan curTime)
    {
        return curTime - gameRule.ActivatedAt + component.TimeOffset;
    }

    /// <summary>
    ///     Moves the timeline to the start of a phase and enters it, announcement, pod and uplink included. For
    ///         admins and tests; the timeline then carries on from there.
    /// </summary>
    public void SetPhase(Entity<KsFieldCommanderRuleComponent, GameRuleComponent> rule, int phaseIndex)
    {
        var component = rule.Comp1;
        if (phaseIndex < 0 || phaseIndex >= component.Phases.Count)
            return;

        var curTime = _gameTiming.CurTime;
        component.TimeOffset += component.Phases[phaseIndex].Start - GetElapsed(component, rule.Comp2, curTime);
        EnterPhase((rule.Owner, component), phaseIndex, curTime);
    }

    /// <summary>
    ///     Sends the current phase's next wave now, and counts the wait for the one after from here.
    /// </summary>
    public void ForceWave(Entity<KsFieldCommanderRuleComponent> rule)
    {
        if (!TryGetPhase(rule.Comp, out var phase))
            return;

        SetNextWave(rule.Comp, GetNextWaveTime(rule.Comp, phase, _gameTiming.CurTime));
        SendWave(rule, phase);
    }

    /// <summary>
    ///     Lands the uplink now, guarded as the current phase would guard it.
    /// </summary>
    public void ForceUplink(Entity<KsFieldCommanderRuleComponent> rule)
    {
        var phase = TryGetPhase(rule.Comp, out var currentPhase) ? currentPhase : new KsFieldCommanderPhase();
        DeployUplink(rule, phase, _gameTiming.CurTime);
    }

    private static bool TryGetPhase(KsFieldCommanderRuleComponent component, out KsFieldCommanderPhase phase)
    {
        if (component.PhaseIndex < 0 || component.PhaseIndex >= component.Phases.Count)
        {
            phase = default!;
            return false;
        }

        phase = component.Phases[component.PhaseIndex];
        return true;
    }

    /// <summary>
    ///     The current phase's name, for the round end summary and admins.
    /// </summary>
    private string GetPhaseName(KsFieldCommanderRuleComponent component)
    {
        return TryGetPhase(component, out var phase)
            ? Loc.GetString(phase.Name)
            : Loc.GetString("ks-fieldcommander-roundend-no-phase");
    }

    /// <summary>
    ///     When a phase's next wave is due, if it is sent at <paramref name="due"/>: never, for a phase that sends none.
    /// </summary>
    private static TimeSpan ScheduleWave(KsFieldCommanderPhase phase, TimeSpan due)
    {
        return phase.WaveInterval > TimeSpan.Zero ? due : TimeSpan.MaxValue;
    }

    private void EnterPhase(Entity<KsFieldCommanderRuleComponent> rule, int phaseIndex, TimeSpan curTime)
    {
        rule.Comp.PhaseIndex = phaseIndex;
        var phase = rule.Comp.Phases[phaseIndex];

        SetNextWave(rule.Comp, ScheduleWave(phase, curTime + phase.FirstWaveDelay));

        // Whatever was left over belongs to the old phase's waves.
        rule.Comp.CarriedBudget = 0f;

        Transmit(rule.Comp, phase.Announcement, major: true, phase.Intel);

        if (phase.ReinforcementPod is { } reinforcementPod)
        {
            DropReinforcements(rule.Comp, reinforcementPod);
            Transmit(rule.Comp, taunt: null, major: false, rule.Comp.ReinforcementIntel);
        }

        if (phase.DeployUplink)
            DeployUplink(rule, phase, curTime);
    }

    [SubscribeLocalEvent]
    private void OnUnitMobStateChanged(Entity<KsFieldCommanderUnitComponent> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead ||
            !TryComp<KsFieldCommanderRuleComponent>(entity.Comp.RuleUid, out var ruleComponent))
            return;

        ruleComponent.UnitsKilled++;
        RewardUnitKill((entity.Comp.RuleUid, ruleComponent), entity);
    }

    [SubscribeLocalEvent]
    private void OnPassengerDisembarked(Entity<KsFieldCommanderUnitComponent> entity, ref KsSupplyPodPassengerDisembarkedEvent args)
    {
        if (!entity.Comp.GiveOrders ||
            !TryComp<KsFieldCommanderRuleComponent>(entity.Comp.RuleUid, out var ruleComponent))
            return;

        // Straight out of the pod and at them, rather than standing about until the next round of orders.
        OrderUnit(ruleComponent, entity.Owner, SurveyBattlefield(ruleComponent));
    }

    [SubscribeLocalEvent]
    private void OnObjectiveLost(ref ScenarioObjectiveLostEvent args)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var ruleUid, out _, out var ruleComponent, out _))
        {
            if (args.ObjectiveUid == ruleComponent.UplinkUid)
            {
                OnUplinkLost((ruleUid, ruleComponent));

                // Only a win over the uplink leads on; one that lasted to call down its bombardment ends the round.
                if (ruleComponent.UplinkDestroyedTime != null)
                    TryStartEndlessVote((ruleUid, ruleComponent));
                continue;
            }

            if (args.FactionId != ruleComponent.DefenderFaction)
                continue;

            ruleComponent.ObjectivesLost++;

            var remainingObjectives = new List<MapCoordinates>();
            CollectDefenderObjectives(ruleComponent, remainingObjectives, exceptUid: args.ObjectiveUid);
            if (remainingObjectives.Count == 0)
                continue;

            Transmit(ruleComponent, ruleComponent.ObjectiveLostAnnouncement, major: true, ruleComponent.ObjectiveLostIntel,
                ("remaining", remainingObjectives.Count));
        }
    }

    protected override void AppendRoundEndText(EntityUid uid, KsFieldCommanderRuleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);

        args.AddLine(Loc.GetString("ks-fieldcommander-roundend-summary",
            ("phase", GetPhaseName(component)),
            ("waves", component.WavesSent),
            ("sent", component.UnitsSent),
            ("killed", component.UnitsKilled),
            ("lost", component.ObjectivesLost)));

        args.AddLine(component.Endless
            ? Loc.GetString("ks-fieldcommander-roundend-waves-endless",
                ("wave", component.WavesSent),
                ("endless", component.EndlessWavesSent))
            : Loc.GetString("ks-fieldcommander-roundend-waves", ("wave", component.WavesSent)));

        if (component.UplinkDestroyedTime is { } destroyedTime && component.UplinkDeployedTime is { } deployedTime)
        {
            args.AddLine(Loc.GetString("ks-fieldcommander-roundend-uplink-destroyed",
                ("minutes", (int)(destroyedTime - deployedTime).TotalMinutes),
                ("seconds", (destroyedTime - deployedTime).Seconds)));
        }
        else if (component.UplinkUid is { } uplinkUid && TerminatingOrDeleted(uplinkUid))
        {
            args.AddLine(Loc.GetString("ks-fieldcommander-roundend-uplink-bombarded"));
        }
    }
}
