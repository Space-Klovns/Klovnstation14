using Content.Server._KS14.GameTicking.Rules;
using Content.Server._KS14.NPC.Systems;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.SupplyPod.Passengers;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Humanoid.Systems;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Runs an AI field commander: walks its timeline of phases, sends waves bought from a threat budget, strikes the
///         defenders' works, keeps its units pointed at the defenders and lands the uplink that decides the round.
/// </summary>
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
    [Dependency] private EntityQuery<KsFieldCommanderUnitComponent> _unitQuery = default!;

    protected override void Started(EntityUid uid, KsFieldCommanderRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.NextOrderTime = _gameTiming.CurTime + component.OrderInterval;
    }

    protected override void ActiveTick(EntityUid uid, KsFieldCommanderRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        // Waits for the map: its objectives and spawn points are what everything else is placed around.
        if (!component.ObjectivesPrepared && !TryPrepare(component))
            return;

        var curTime = _gameTiming.CurTime;
        var elapsed = curTime - gameRule.ActivatedAt;

        while (component.PhaseIndex + 1 < component.Phases.Count &&
               elapsed >= component.Phases[component.PhaseIndex + 1].Start)
        {
            EnterPhase((uid, component), component.PhaseIndex + 1, curTime);
        }

        if (curTime >= component.NextWaveTime &&
            TryGetPhase(component, out var phase))
        {
            component.NextWaveTime = phase.WaveInterval > TimeSpan.Zero
                ? curTime + phase.WaveInterval
                : TimeSpan.MaxValue;

            SendWave((uid, component), phase);
        }

        if (curTime >= component.NextOrderTime)
        {
            component.NextOrderTime = curTime + component.OrderInterval;
            GiveOrders((uid, component));
        }

        UpdateUplink(component, curTime);
    }

    /// <summary>
    ///     Takes the timers off the defender objectives, counts the commander's side as alive and still to place its
    ///         objective, and finds the safe zones. False until the map with the objectives is there.
    /// </summary>
    private bool TryPrepare(KsFieldCommanderRuleComponent component)
    {
        var foundObjective = false;
        var objectiveQuery = EntityQueryEnumerator<ScenarioObjectiveComponent>();
        while (objectiveQuery.MoveNext(out var objectiveUid, out var objectiveComponent))
        {
            if (objectiveComponent.FactionId != component.DefenderFaction)
                continue;

            foundObjective = true;

            // The commander decides how the round ends; an objective must not hand it to the defenders by lasting.
            RemComp<TimedDespawnComponent>(objectiveUid);
        }

        if (!foundObjective)
            return false;

        _scenarioSystem.SetPersistent(component.Faction, persistent: true);
        _scenarioSystem.SetObjectivePending(component.Faction, pending: true);

        FindSafeZones(component);
        component.ObjectivesPrepared = true;
        return true;
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

    private void EnterPhase(Entity<KsFieldCommanderRuleComponent> rule, int phaseIndex, TimeSpan curTime)
    {
        rule.Comp.PhaseIndex = phaseIndex;
        var phase = rule.Comp.Phases[phaseIndex];

        rule.Comp.NextWaveTime = phase.WaveInterval > TimeSpan.Zero
            ? curTime + phase.FirstWaveDelay
            : TimeSpan.MaxValue;

        // Whatever was left over belongs to the old phase's waves.
        rule.Comp.CarriedBudget = 0f;

        if (phase.Announcement is { } announcement)
            Announce(rule.Comp, announcement, major: true);

        if (phase.ReinforcementPod is { } reinforcementPod)
            DropReinforcements(rule.Comp, reinforcementPod);

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
    }

    [SubscribeLocalEvent]
    private void OnPassengerDisembarked(Entity<KsFieldCommanderUnitComponent> entity, ref KsSupplyPodPassengerDisembarkedEvent args)
    {
        if (!entity.Comp.GiveOrders ||
            !TryComp<KsFieldCommanderRuleComponent>(entity.Comp.RuleUid, out var ruleComponent))
            return;

        // Straight out of the pod and at them, rather than standing about until the next round of orders.
        var defenders = new List<MapCoordinates>();
        CollectLivingDefenders(ruleComponent, defenders, out _);
        OrderUnit(ruleComponent, entity.Owner, defenders);
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
                continue;
            }

            if (args.FactionId != ruleComponent.DefenderFaction)
                continue;

            ruleComponent.ObjectivesLost++;

            var remaining = CountDefenderObjectives(ruleComponent, exceptUid: args.ObjectiveUid);
            if (remaining > 0 && ruleComponent.ObjectiveLostAnnouncement is { } announcement)
                Announce(ruleComponent, announcement, major: true, ("remaining", remaining));
        }
    }

    protected override void AppendRoundEndText(EntityUid uid, KsFieldCommanderRuleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);

        var phaseName = TryGetPhase(component, out var phase)
            ? Loc.GetString(phase.Name)
            : Loc.GetString("ks-fieldcommander-roundend-no-phase");

        args.AddLine(Loc.GetString("ks-fieldcommander-roundend-summary",
            ("phase", phaseName),
            ("waves", component.WavesSent),
            ("sent", component.UnitsSent),
            ("killed", component.UnitsKilled),
            ("lost", component.ObjectivesLost)));

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
