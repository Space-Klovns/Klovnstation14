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

[RegisterComponent, Access(typeof(ScenarioSystem))]
public sealed partial class ScenarioRuleComponent : Component
{
    [ViewVariables]
    public ProtoId<ScenarioFactionPrototype>? WinningFactionId = null;

    [ViewVariables]
    public ScenarioWinType WinType;

    /// <summary>
    ///     Factions that count as alive whether or not any of their members are. For a side whose members come and
    ///         go, such as an AI that keeps sending fresh units, so that the last unit dying is not a defeat.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<ScenarioFactionPrototype>> PersistentFactions = [];

    /// <summary>
    ///     Factions that count as still holding an objective while they have none on the map, because theirs arrives
    ///         later. Cleared through <see cref="ScenarioSystem.SetObjectivePending"/> once it does.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<ScenarioFactionPrototype>> PendingObjectiveFactions = [];
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

    /// <summary>
    ///     Whether <paramref name="factionId"/> is counted as alive with no living members. False if no scenario is
    ///         running to count it.
    /// </summary>
    public bool SetPersistent(ProtoId<ScenarioFactionPrototype> factionId, bool persistent)
    {
        return SetInEveryRule(scenario => scenario.PersistentFactions, factionId, persistent);
    }

    /// <summary>
    ///     Whether <paramref name="factionId"/> is counted as holding an objective it has not placed yet. False if no
    ///         scenario is running to count it.
    /// </summary>
    public bool SetObjectivePending(ProtoId<ScenarioFactionPrototype> factionId, bool pending)
    {
        return SetInEveryRule(scenario => scenario.PendingObjectiveFactions, factionId, pending);
    }

    private bool SetInEveryRule(Func<ScenarioRuleComponent, HashSet<ProtoId<ScenarioFactionPrototype>>> getSet,
        ProtoId<ScenarioFactionPrototype> factionId,
        bool include)
    {
        var anyRule = false;
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var scenario, out _))
        {
            anyRule = true;
            if (include)
                getSet(scenario).Add(factionId);
            else
                getSet(scenario).Remove(factionId);
        }

        return anyRule;
    }

    /// <summary>
    ///     Ends the round now, with <paramref name="factionId"/> the winner. For victories decided by something other
    ///         than an objective or a death, such as a timer running out.
    /// </summary>
    public void DeclareWinner(ProtoId<ScenarioFactionPrototype> factionId, ScenarioWinType winType)
    {
        if (!IsRuleActive())
            return;

        SetWinFaction(factionId);
        SetWinType(winType);
        EndRoundNow();
    }

    /// <summary>
    ///     Ends the round if only one faction still holds an objective, as when one is lost. For a rule that kept the
    ///         round going with <see cref="SetObjectivePending"/> and has now stopped.
    /// </summary>
    public void CheckObjectiveRoundEnd()
    {
        CheckRoundShouldEndViaObjective();
    }

    private void EndRoundNow()
    {
        _roundEndSystem.DoRoundEndBehavior(RoundEndBehavior.InstantEnd,
            TimeSpan.FromMinutes(3), // doesnt matter, it's instant
            "comms-console-announcement-title-centcom",
            "comms-console-announcement-title-centcom",
            "comms-console-announcement-title-centcom");
    }

    /// <summary>
    ///     Every faction that a rule says is pending an objective or never dies.
    /// </summary>
    private void CollectRuleFactions(HashSet<ProtoId<ScenarioFactionPrototype>> factionIds, bool pendingObjectives)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var scenario, out _))
            factionIds.UnionWith(pendingObjectives ? scenario.PendingObjectiveFactions : scenario.PersistentFactions);
    }

    private void CheckRoundShouldEndViaObjective()
    {
        // Do nuffin if the game rule isnt active
        if (!IsRuleActive())
            return;

        // to win, a faction must be the only one with atleast 1 of their objectives remaining

        var holdingFactionSet = _uniqueFactionSetLocal.Value!;
        holdingFactionSet.Clear();

        // A faction whose objective has not arrived yet is still in the running.
        CollectRuleFactions(holdingFactionSet, pendingObjectives: true);

        foreach (var (factionId, factionActiveObjectiveUids) in _activeObjectiveUids)
        {
            if (factionActiveObjectiveUids.Count > 0)
                holdingFactionSet.Add(factionId);
        }

        // means that there is more than 1 faction with objectives remaining
        if (holdingFactionSet.Count > 1)
            return;

        ProtoId<ScenarioFactionPrototype>? winningFactionId = holdingFactionSet.Count == 1 ? holdingFactionSet.First() : (ProtoId<ScenarioFactionPrototype>?)null;

        // The winner was already decided when the objective was captured (or defended); only fall back to the
        //      lone remaining faction if nothing decided it, and never overwrite a decided winner with null.
        if (winningFactionId is { } &&
            !HasWinner())
            SetWinFaction(winningFactionId);

        SetWinType(ScenarioWinType.Objective);
        EndRoundNow();
    }

    private void CheckRoundShouldEndViaDeath()
    {
        if (!IsRuleActive())
            return;

        // i would like to assume thread safety but no just no
        var uniqueFactionSet = _uniqueFactionSetLocal.Value!;
        uniqueFactionSet.Clear();

        // Factions that never die are alive from the start.
        CollectRuleFactions(uniqueFactionSet, pendingObjectives: false);

        // With a faction that never dies, the round ends once it is the only side left standing. Without one, the
        //      scenarios keep their original rule: the round goes on while more than one member is alive, whatever
        //      their side.
        var byFaction = uniqueFactionSet.Count > 0;
        var livingMembers = 0;

        // Who is still alive, and on which side.
        var query = EntityQuery<ScenarioFactionMemberComponent, MobStateComponent>();
        foreach (var (memberComponent, mobStateComponent) in query)
        {
            if (mobStateComponent.CurrentState == MobState.Dead)
                continue;

            livingMembers++;
            uniqueFactionSet.Add(memberComponent.Id);

            // means that there is more than 1 faction alive, round shouldnt end so return
            if (byFaction ? uniqueFactionSet.Count > 1 : livingMembers > 1)
                return;
        }

        if (uniqueFactionSet.Count > 1)
            return;

        SetWinFaction(uniqueFactionSet.Count == 1 ? uniqueFactionSet.First() : (ProtoId<ScenarioFactionPrototype>?)null);
        EndRoundNow();
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

        // Raised before the round can end over it, so that whatever reacts to a loss still sees a running round.
        var ev = new ScenarioObjectiveLostEvent(entity.Owner, entity.Comp.FactionId);
        RaiseLocalEvent(ref ev);

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

/// <summary>
///     Broadcast when a faction loses one of its objectives, captured, destroyed or timed out, but not when it is
///         deleted with its map.
/// </summary>
[ByRefEvent]
public readonly record struct ScenarioObjectiveLostEvent(EntityUid ObjectiveUid, ProtoId<ScenarioFactionPrototype> FactionId);
