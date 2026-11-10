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
    [Dependency] private RoundEndSystem _roundEndSystem = default!;
    [Dependency] private PvsOverrideSystem _pvsOverrideSystem = default!;

    // All active objectives for factions
    private readonly Dictionary<ProtoId<ScenarioFactionPrototype>, HashSet<EntityUid>> _activeObjectiveUids = [];
    private readonly ThreadLocal<HashSet<ProtoId<ScenarioFactionPrototype>>> _uniqueFactionSetLocal = new(() => []);

    [SubscribeLocalEvent]
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
        args.AddLine(GetSkirmishLine());

        if (component.WinningFactionId is { } winningFactionId &&
            ProtoMan.TryIndex(winningFactionId, out var winningFaction))
        {
            args.AddLine(Loc.GetString(winningFaction.VictoryLocId));

            if (winningFaction.WinTypeLocIds.TryGetValue(component.WinType, out var winLoc))
                args.AddLine(Loc.GetString(winLoc));
        }
    }

    /// <summary>
    ///     Builds the opening round-end line from the factions that actually took part:
    ///         those with a (possibly already captured) objective, and those with members.
    /// </summary>
    private string GetSkirmishLine()
    {
        var factionIds = new List<ProtoId<ScenarioFactionPrototype>>();

        foreach (var factionId in _activeObjectiveUids.Keys)
        {
            if (!factionIds.Contains(factionId))
                factionIds.Add(factionId);
        }

        var memberQuery = AllEntityQuery<ScenarioFactionMemberComponent>();
        while (memberQuery.MoveNext(out _, out var memberComponent))
        {
            if (!factionIds.Contains(memberComponent.Id))
                factionIds.Add(memberComponent.Id);
        }

        var names = new List<string>();
        foreach (var factionId in factionIds)
        {
            if (ProtoMan.TryIndex(factionId, out var faction))
                names.Add(Loc.GetString(faction.NameLocId));
        }

        if (names.Count == 0)
            return Loc.GetString("scenario-roundend-skirmish-unknown");

        var joinedNames = names.Count == 1
            ? names[0]
            : Loc.GetString("scenario-roundend-skirmish-and",
                ("others", string.Join(", ", names.Take(names.Count - 1))),
                ("last", names[^1]));

        return Loc.GetString("scenario-roundend-skirmish", ("factions", joinedNames));
    }

    protected override void Added(EntityUid uid, ScenarioRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);

        // ScenarioSystem only handles map-loaded scenarios via OnRuleLoadedGrids event
        // Map loading and RuleLoadedGridsEvent is handled by LoadMapRuleSystem
    }

    [SubscribeLocalEvent]
    private void OnMemberShutdown(Entity<ScenarioFactionMemberComponent> entity, ref ComponentShutdown args)
    {
        CheckRoundShouldEndViaDeath();
    }

    [SubscribeLocalEvent]
    private void OnMemberMobStateChanged(Entity<ScenarioFactionMemberComponent> entity, ref MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead)
            return;

        CheckRoundShouldEndViaDeath();
    }

    [SubscribeLocalEvent]
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

    private bool HasWinner()
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var scenario, out _))
        {
            if (scenario.WinningFactionId is { })
                return true;
        }

        return false;
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

        // The winner was already decided when the objective was captured (or defended); only fall back to the
        //      lone remaining faction if nothing decided it, and never overwrite a decided winner with null.
        if (winningFactionId is { } &&
            !HasWinner())
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

    [SubscribeLocalEvent]
    private void OnObjectiveMapInit(Entity<ScenarioObjectiveComponent> entity, ref MapInitEvent args)
    {
        _pvsOverrideSystem.AddGlobalOverride(entity);
        _activeObjectiveUids.GetOrNew(entity.Comp.FactionId).Add(entity);
    }

    [SubscribeLocalEvent]
    private void OnObjectiveShutdown(Entity<ScenarioObjectiveComponent> entity, ref ComponentShutdown args)
    {
        _pvsOverrideSystem.RemoveGlobalOverride(entity);
        if (_activeObjectiveUids.TryGetValue(entity.Comp.FactionId, out var factionActiveObjectiveUids))
            factionActiveObjectiveUids.Remove(entity);

        // An objective going away with its map or entity (round cleanup, deletion) is not a capture, and
        //      must neither decide a winner nor end the round. Captures and defences remove the component alone.
        if (TerminatingOrDeleted(entity))
            return;

        // The winner is decided by CaptureObjective: the owner of a removed objective is the one that lost it.
        CheckRoundShouldEndViaObjective();
    }

    [SubscribeLocalEvent]
    private void OnTriggered(Entity<ScenarioObjectiveComponent> entity, ref TriggerEvent args)
    {
        if (args.Key is { } key &&
            !entity.Comp.KeysIn.Contains(key))
            return;

        CaptureObjective(entity, args.User);
    }

    [SubscribeLocalEvent]
    private void OnObjDefended(Entity<ScenarioObjectiveComponent> entity, ref TimedDespawnEvent args)
    {
        // Nobody took it before the timer ran out, so the owner held it.
        SetWinFaction(entity.Comp.FactionId);
        RemComp(entity, entity.Comp);
    }

    [SubscribeLocalEvent]
    private void OnObjDestroyed(Entity<ScenarioObjectiveComponent> entity, ref DestructionEventArgs args)
    {
        CaptureObjective(entity, capturerUid: null);
    }

    /// <summary>
    ///     The faction that took an objective is the winner, which is never the objective's owner.
    /// </summary>
    private void CaptureObjective(Entity<ScenarioObjectiveComponent> entity, EntityUid? capturerUid)
    {
        // Never write a null winner here: an unresolved capturer must not wipe a winner that is already decided.
        if (ResolveCapturingFaction(entity.Comp.FactionId, capturerUid) is { } capturingFactionId)
            SetWinFaction(capturingFactionId);

        RemComp(entity, entity.Comp);
    }

    /// <summary>
    ///     The capturer's own faction when known; otherwise the single living faction other than the owner.
    ///         Null when that is ambiguous, so that the survivor logic can decide.
    /// </summary>
    private ProtoId<ScenarioFactionPrototype>? ResolveCapturingFaction(ProtoId<ScenarioFactionPrototype> ownerId, EntityUid? capturerUid)
    {
        if (capturerUid is { } userUid &&
            TryComp<ScenarioFactionMemberComponent>(userUid, out var userMember) &&
            userMember.Id != ownerId)
            return userMember.Id;

        ProtoId<ScenarioFactionPrototype>? otherId = null;
        var query = EntityQueryEnumerator<ScenarioFactionMemberComponent, MobStateComponent>();
        while (query.MoveNext(out _, out var memberComponent, out var mobStateComponent))
        {
            if (mobStateComponent.CurrentState == MobState.Dead ||
                memberComponent.Id == ownerId)
                continue;

            if (otherId is { } &&
                otherId != memberComponent.Id)
                return null;

            otherId = memberComponent.Id;
        }

        return otherId;
    }
}
