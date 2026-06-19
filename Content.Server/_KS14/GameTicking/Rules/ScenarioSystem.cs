using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Zombies;
using Content.Server.RoundEnd;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Content.Shared.Destructible;
using System.Linq;

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

    public override void Initialize()
    {
        base.Initialize();

        //syndie checks
        SubscribeLocalEvent<ScenarioSyndieComponent, ComponentRemove>(OnSyndieCompRemove);
        SubscribeLocalEvent<ScenarioSyndieComponent, MobStateChangedEvent>(OnSyndieMobstateChanged);
        SubscribeLocalEvent<ScenarioSyndieComponent, EntityZombifiedEvent>(OnSyndieZombified);

        //NT checks
        SubscribeLocalEvent<ScenarioNtComponent, ComponentRemove>(OnNtCompRemove);
        SubscribeLocalEvent<ScenarioNtComponent, MobStateChangedEvent>(OnNtMobstateChanged);
        SubscribeLocalEvent<ScenarioNtComponent, EntityZombifiedEvent>(OnNtZombified);

        SubscribeLocalEvent<ScenarioObjectiveComponent, TimedDespawnEvent>(OnObjDefended);
        SubscribeLocalEvent<ScenarioObjectiveComponent, DestructionEventArgs>(OnObjDestroyed);

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

    private void OnSyndieCompRemove(EntityUid uid, ScenarioSyndieComponent component, ComponentRemove args)
    {
        CheckRoundShouldEnd();
    }

    private void OnSyndieMobstateChanged(EntityUid uid, ScenarioSyndieComponent component, MobStateChangedEvent ev)
    {
        if (ev.NewMobState == MobState.Dead)
            CheckRoundShouldEnd();
    }

    private void OnSyndieZombified(EntityUid uid, ScenarioSyndieComponent component, ref EntityZombifiedEvent args)
    {
        RemCompDeferred(uid, component);
    }

    private void OnNtCompRemove(EntityUid uid, ScenarioNtComponent component, ComponentRemove args)
    {
        CheckRoundShouldEnd();
    }

    private void OnNtMobstateChanged(EntityUid uid, ScenarioNtComponent component, MobStateChangedEvent ev)
    {
        if (ev.NewMobState == MobState.Dead)
            CheckRoundShouldEnd();
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
    private void CheckRoundShouldEnd()
    {
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

    private void OnObjDefended(EntityUid uid, ScenarioObjectiveComponent component, TimedDespawnEvent args)
    {
        SetWinType(true);
        _roundEndSystem.DoRoundEndBehavior(RoundEndBehavior.InstantEnd,
            TimeSpan.FromMinutes(3), //doesnt matter
            "comms-console-announcement-title-centcom",
            "comms-console-announcement-title-centcom",
            "comms-console-announcement-title-centcom");
    }

    private void OnObjDestroyed(EntityUid uid, ScenarioObjectiveComponent component, DestructionEventArgs args)
    {
        SetWinType(true);
        _roundEndSystem.DoRoundEndBehavior(RoundEndBehavior.InstantEnd,
            TimeSpan.FromMinutes(3), //doesnt matter
            "comms-console-announcement-title-centcom",
            "comms-console-announcement-title-centcom",
            "comms-console-announcement-title-centcom");
    }
}
