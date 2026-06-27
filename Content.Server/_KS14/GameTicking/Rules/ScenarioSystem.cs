using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Zombies;
using Content.Server.RoundEnd;
using Robust.Shared.Spawners;
using Content.Shared.Destructible;
using System.Linq;
using Content.Shared.Trigger;
using Content.Shared.GameTicking;

namespace Content.Server._KS14.GameTicking.Rules;

[RegisterComponent]
public sealed partial class ScenarioRuleComponent : Component
{
    public bool NtWon = false;
    public bool ObjectiveVictory = false;
}
public sealed class ScenarioSystem : GameRuleSystem<ScenarioRuleComponent>
{
    [Dependency] private readonly RoundEndSystem _roundEndSystem = default!;

    // All active objectives
    private readonly HashSet<EntityUid> _activeObjectiveUids = [];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        //syndie checks
        SubscribeLocalEvent<ScenarioSyndieComponent, ComponentShutdown>(OnSyndieShutdown);
        SubscribeLocalEvent<ScenarioSyndieComponent, MobStateChangedEvent>(OnSyndieMobstateChanged);
        SubscribeLocalEvent<ScenarioSyndieComponent, EntityZombifiedEvent>(OnSyndieZombified);

        //NT checks
        SubscribeLocalEvent<ScenarioNtComponent, ComponentShutdown>(OnNtShutdown);
        SubscribeLocalEvent<ScenarioNtComponent, MobStateChangedEvent>(OnNtMobstateChanged);
        SubscribeLocalEvent<ScenarioNtComponent, EntityZombifiedEvent>(OnNtZombified);

        SubscribeLocalEvent<ScenarioObjectiveComponent, ComponentShutdown>(OnObjectiveShutdown);
        SubscribeLocalEvent<ScenarioObjectiveComponent, TriggerEvent>(OnTriggered);
        SubscribeLocalEvent<ScenarioObjectiveComponent, TimedDespawnEvent>(OnObjDefended);
        SubscribeLocalEvent<ScenarioObjectiveComponent, DestructionEventArgs>(OnObjDestroyed);

    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _activeObjectiveUids.Clear();
        _activeObjectiveUids.TrimExcess();
    }

    protected override void AppendRoundEndText(EntityUid uid,
        ScenarioRuleComponent component,
        GameRuleComponent gameRule,
        ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);
        //TODO SOOT: unfuck this and make this actually good
        args.AddLine("A skirmish between NT and Syndicate forces has transpired.");
        args.AddLine($"The victor is {(component.NtWon ? "Nanotrasen" : "The Syndicate")}");
        args.AddLine($"{(component.ObjectiveVictory ? (component.NtWon ? "Nanotrasen has destroyed the Syndicate objective." : "The Syndicate has destroyed the Nanotrasen objective.") : (component.NtWon ? "Nanotrasen has killed all Syndicate operatives." : "The Syndicate has killed all Nanotrasen agents."))}");
    }

    protected override void Added(EntityUid uid, ScenarioRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);

        // ScenarioSystem only handles map-loaded scenarios via OnRuleLoadedGrids event
        // Map loading and RuleLoadedGridsEvent is handled by LoadMapRuleSystem
    }

    private void OnSyndieShutdown(EntityUid uid, ScenarioSyndieComponent component, ComponentShutdown args)
    {
        CheckRoundShouldEndViaDeath();
    }

    private void OnSyndieMobstateChanged(EntityUid uid, ScenarioSyndieComponent component, MobStateChangedEvent ev)
    {
        if (ev.NewMobState == MobState.Dead)
            CheckRoundShouldEndViaDeath();
    }

    private void OnSyndieZombified(EntityUid uid, ScenarioSyndieComponent component, ref EntityZombifiedEvent args)
    {
        RemCompDeferred(uid, component);
    }

    private void OnNtShutdown(EntityUid uid, ScenarioNtComponent component, ComponentShutdown args)
    {
        CheckRoundShouldEndViaDeath();
    }

    private void OnNtMobstateChanged(EntityUid uid, ScenarioNtComponent component, MobStateChangedEvent ev)
    {
        if (ev.NewMobState == MobState.Dead)
            CheckRoundShouldEndViaDeath();
    }

    private void OnNtZombified(EntityUid uid, ScenarioNtComponent component, ref EntityZombifiedEvent args)
    {
        RemCompDeferred(uid, component);
    }
    private void SetWinType(bool value)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var scenario, out _))
        {
            SetWinType((uid, scenario), value);
        }
    }
    private void SetWinType(Entity<ScenarioRuleComponent> ent, bool value)
    {
        ent.Comp.ObjectiveVictory = value;
    }
    private void SetWinFaction(bool value)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var scenario, out _))
        {
            SetWinFaction((uid, scenario), value);
        }
    }
    private void SetWinFaction(Entity<ScenarioRuleComponent> ent, bool value)
    {
        ent.Comp.NtWon = value;
    }

    private bool IsRuleActive()
    {
        var eqe = QueryActiveRules();
        while (eqe.MoveNext(out _, out _, out _))
            return true;

        return false;
    }

    private void CheckRoundShouldEndViaObjective()
    {
        // Do nuffin if the game rule isnt active
        if (!IsRuleActive())
            return;

        if (_activeObjectiveUids.Count != 0)
            return;

        SetWinType(true);
        _roundEndSystem.DoRoundEndBehavior(RoundEndBehavior.InstantEnd,
            TimeSpan.FromMinutes(3), //doesnt matter
            "comms-console-announcement-title-centcom",
            "comms-console-announcement-title-centcom",
            "comms-console-announcement-title-centcom");
    }

    private void CheckRoundShouldEndViaDeath()
    {
        if (!IsRuleActive())
            return;

        // Check if there are syndies still alive
        // If there are, the round can continue.
        var syndies = EntityQuery<ScenarioSyndieComponent, MobStateComponent, TransformComponent>(true);
        var syndiesAlive = syndies
            .Any(syn => syn.Item2.CurrentState == MobState.Alive && syn.Item1.Running);

        if (!syndiesAlive)
        {
            SetWinFaction(true);
            _roundEndSystem.DoRoundEndBehavior(RoundEndBehavior.InstantEnd,
                TimeSpan.FromMinutes(3), //doesnt matter if its instant i think
                "comms-console-announcement-title-centcom",
                "comms-console-announcement-title-centcom",
                "comms-console-announcement-title-centcom");
        }

        // Check if there are nanotrasen still alive
        // If there are, the round can continue.
        var nanotrasen = EntityQuery<ScenarioNtComponent, MobStateComponent, TransformComponent>(true);
        var nanotrasenAlive = syndies
            .Any(nt => nt.Item2.CurrentState == MobState.Alive && nt.Item1.Running);

        if (nanotrasenAlive)
            return; // There are living nanotrasen

        _roundEndSystem.DoRoundEndBehavior(RoundEndBehavior.InstantEnd,
            TimeSpan.FromMinutes(3), //doesnt matter if its instant i think
            "comms-console-announcement-title-centcom",
            "comms-console-announcement-title-centcom",
            "comms-console-announcement-title-centcom");

    }

    private void OnObjectiveShutdown(Entity<ScenarioObjectiveComponent> entity, ref ComponentShutdown args)
    {
        _activeObjectiveUids.Remove(entity);
        CheckRoundShouldEndViaObjective();
    }

    private void OnTriggered(Entity<ScenarioObjectiveComponent> entity, ref TriggerEvent args)
    {
        if (args.Key is { } key &&
            !entity.Comp.KeysIn.Contains(key))
            return;

        CaptureObjective(entity);
    }

    private void OnObjDefended(Entity<ScenarioObjectiveComponent> entity, ref TimedDespawnEvent args)
    {
        CaptureObjective(entity);
    }

    private void OnObjDestroyed(Entity<ScenarioObjectiveComponent> entity, ref DestructionEventArgs args)
    {
        CaptureObjective(entity);
    }

    private void CaptureObjective(Entity<ScenarioObjectiveComponent> entity)
    {
        RemComp(entity, entity.Comp);
    }
}
