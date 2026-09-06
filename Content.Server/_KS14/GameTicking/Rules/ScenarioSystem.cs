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
using Robust.Shared.Prototypes;
using System.Threading;
using Robust.Server.GameStates;
using Robust.Shared.Utility;

namespace Content.Server._KS14.GameTicking.Rules;

[RegisterComponent]
public sealed partial class ScenarioRuleComponent : Component
{
    public ProtoId<ScenarioFactionPrototype>? WinningFactionId = null;

    public ScenarioWinType WinType;
}

public sealed partial class ScenarioSystem : GameRuleSystem<ScenarioRuleComponent>
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private RoundEndSystem _roundEndSystem = default!;
    [Dependency] private PvsOverrideSystem _pvsOverrideSystem = default!;

    // All active objectives for factions
    private readonly Dictionary<ProtoId<ScenarioFactionPrototype>, HashSet<EntityUid>> _activeObjectiveUids = [];
    private readonly ThreadLocal<HashSet<ProtoId<ScenarioFactionPrototype>>> _uniqueFactionSetLocal = new(() => []);


    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        //syndie checks
        SubscribeLocalEvent<ScenarioFactionMemberComponent, ComponentShutdown>(OnMemberShutdown);
        SubscribeLocalEvent<ScenarioFactionMemberComponent, MobStateChangedEvent>(OnMemberMobStateChanged);
        SubscribeLocalEvent<ScenarioFactionMemberComponent, EntityZombifiedEvent>(OnMemberZombified);

        SubscribeLocalEvent<ScenarioObjectiveComponent, MapInitEvent>(OnObjectiveMapInit);
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

        if (component.WinningFactionId is { } winningFactionId &&
            _prototypeManager.TryIndex(winningFactionId, out var winningFaction))
        {
            args.AddLine(Loc.GetString(winningFaction.VictoryLocId));

            if (winningFaction.WinTypeLocIds.TryGetValue(component.WinType, out var winLoc))
                args.AddLine(Loc.GetString(winLoc));
        }
    }

    protected override void Added(EntityUid uid, ScenarioRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);

        // ScenarioSystem only handles map-loaded scenarios via OnRuleLoadedGrids event
        // Map loading and RuleLoadedGridsEvent is handled by LoadMapRuleSystem
    }

    private void OnMemberShutdown(Entity<ScenarioFactionMemberComponent> entity, ref ComponentShutdown args)
    {
        CheckRoundShouldEndViaDeath();
    }

    private void OnMemberMobStateChanged(Entity<ScenarioFactionMemberComponent> entity, ref MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead)
            return;

        CheckRoundShouldEndViaDeath();
    }

    private void OnMemberZombified(Entity<ScenarioFactionMemberComponent> entity, ref EntityZombifiedEvent args)
    {
        RemComp(entity, entity.Comp);
    }

    private void SetWinType(ScenarioWinType winType)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var scenario, out _))
            SetWinType((uid, scenario), winType);
    }

    private static void SetWinType(Entity<ScenarioRuleComponent> ent, ScenarioWinType winType)
    {
        ent.Comp.WinType = winType;
    }

    private void SetWinFaction(ProtoId<ScenarioFactionPrototype>? factionId)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var scenario, out _))
            SetWinFaction((uid, scenario), factionId);
    }

    private void SetWinFaction(Entity<ScenarioRuleComponent> ent, ProtoId<ScenarioFactionPrototype>? factionId)
    {
        ent.Comp.WinningFactionId = factionId;
    }

    private bool IsRuleActive()
        => EntityQuery<ScenarioRuleComponent>().Any();

    private void CheckRoundShouldEndViaObjective()
    {
        // Do nuffin if the game rule isnt active
        if (!IsRuleActive())
            return;

        // to win, a faction must be the only one with atleast 1 of their objectives remaining

        ProtoId<ScenarioFactionPrototype>? winningFactionId = null;
        foreach (var (factionId, factionActiveObjectiveUids) in _activeObjectiveUids)
        {
            if (factionActiveObjectiveUids.Count == 0)
                continue;

            // means that there is more than 1 faction with objectives remaining
            if (winningFactionId is { })
                return;

            winningFactionId = factionId;
        }

        SetWinFaction(winningFactionId);
        SetWinType(ScenarioWinType.Objective);

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

        // i would like to assume thread safety but no just no
        var uniqueFactionSet = _uniqueFactionSetLocal.Value!;
        uniqueFactionSet.Clear();

        ProtoId<ScenarioFactionPrototype>? winningFactionId = null;

        // Check if there are syndies still alive
        // If there are, the round can continue.
        var query = EntityQuery<ScenarioFactionMemberComponent, MobStateComponent>();
        foreach (var (memberComponent, mobStateComponent) in query)
        {
            if (mobStateComponent.CurrentState == MobState.Dead)
                continue;

            // means that there is more than 1 faction alive, round shouldnt end so return
            if (winningFactionId is { })
                return;

            winningFactionId = memberComponent.Id;
        }

        SetWinFaction(winningFactionId);
        _roundEndSystem.DoRoundEndBehavior(RoundEndBehavior.InstantEnd,
            TimeSpan.FromMinutes(3), //doesnt matter if its instant i think
            "comms-console-announcement-title-centcom",
            "comms-console-announcement-title-centcom",
            "comms-console-announcement-title-centcom");
    }

    private void OnObjectiveMapInit(Entity<ScenarioObjectiveComponent> entity, ref MapInitEvent args)
    {
        _pvsOverrideSystem.AddGlobalOverride(entity);
        _activeObjectiveUids.GetOrNew(entity.Comp.FactionId).Add(entity);
    }

    private void OnObjectiveShutdown(Entity<ScenarioObjectiveComponent> entity, ref ComponentShutdown args)
    {
        _pvsOverrideSystem.RemoveGlobalOverride(entity);
        if (_activeObjectiveUids.TryGetValue(entity.Comp.FactionId, out var factionActiveObjectiveUids))
            factionActiveObjectiveUids.Remove(entity);

        SetWinFaction(entity.Comp.FactionId);
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
        SetWinFaction(entity.Comp.FactionId);
        RemComp(entity, entity.Comp);
    }
}
