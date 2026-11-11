using Content.Shared.GameTicking;
using Content.Shared._KS14.FactionCollision;
using Content.Shared._KS14.FieldCommander;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared._KS14.Vendors;
using Content.Shared._RMC14.Vendors;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Points

    private const string KillReason = "ks-fieldcommander-points-kill";
    private const string TeamkillReason = "ks-fieldcommander-points-teamkill";
    private const string UplinkReason = "ks-fieldcommander-points-uplink";
    private const string AdminReason = "ks-fieldcommander-points-admin";

    /// <summary>
    ///     A defender's points, or what they would start with if they have none yet.
    /// </summary>
    public int GetPoints(KsFieldCommanderRuleComponent component, NetUserId userId)
    {
        return component.Points.GetValueOrDefault(userId, component.StartingPoints);
    }

    /// <summary>
    ///     Changes a defender's points, tells them why with a sound and their HUD, and carries the new total over to
    ///         the body they're in, where the shop reads it.
    /// </summary>
    public void AddPoints(Entity<KsFieldCommanderRuleComponent> rule, ICommonSession session, int delta, string reason, string? reasonName = null)
    {
        if (delta == 0)
            return;

        var total = GetPoints(rule.Comp, session.UserId) + delta;
        SetPoints(rule, session, total);

        var filter = Filter.SinglePlayer(session);
        _audioSystem.PlayGlobal(delta > 0 ? rule.Comp.PointsGainedSound : rule.Comp.PointsLostSound, filter, recordReplay: false);
        RaiseNetworkEvent(new KsFieldCommanderPointsChangedEvent(delta, reason, reasonName), filter);
    }

    /// <summary>
    ///     Sets a defender's points without telling them, and mirrors them onto their body.
    /// </summary>
    public void SetPoints(Entity<KsFieldCommanderRuleComponent> rule, ICommonSession session, int points)
    {
        rule.Comp.Points[session.UserId] = points;

        if (session.AttachedEntity is { } bodyUid)
            MirrorPoints(rule.Comp, bodyUid, points);
    }

    private void MirrorPoints(KsFieldCommanderRuleComponent component, EntityUid bodyUid, int points)
    {
        var vendorUserComponent = EnsureComp<CMVendorUserComponent>(bodyUid);
        _automatedVendorSystem.SetExtraPoints((bodyUid, vendorUserComponent), component.PointsType, points);
    }

    private bool IsDefender(KsFieldCommanderRuleComponent component, EntityUid uid)
    {
        return _scenarioFactionMemberQuery.TryComp(uid, out var factionMemberComponent) &&
               factionMemberComponent.Id == component.DefenderFaction;
    }

    /// <summary>
    ///     Readies a defender's body: their shots pass through their own side, and their points come with them.
    /// </summary>
    private void PrepareDefender(KsFieldCommanderRuleComponent component, EntityUid bodyUid, ICommonSession? session)
    {
        EnsureComp<KsFactionCollisionShooterComponent>(bodyUid);

        if (session != null)
            MirrorPoints(component, bodyUid, GetPoints(component, session.UserId));
    }

    /// <summary>
    ///     Readies the defenders who were already in the round before the rule was.
    /// </summary>
    private void PrepareExistingDefenders(KsFieldCommanderRuleComponent component)
    {
        var query = EntityQueryEnumerator<ScenarioFactionMemberComponent>();
        while (query.MoveNext(out var memberUid, out var factionMemberComponent))
        {
            if (factionMemberComponent.Id != component.DefenderFaction)
                continue;

            PrepareDefender(component, memberUid, CompOrNull<ActorComponent>(memberUid)?.PlayerSession);
        }
    }

    [SubscribeLocalEvent]
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var ruleComponent, out _))
        {
            if (IsDefender(ruleComponent, args.Mob))
                PrepareDefender(ruleComponent, args.Mob, args.Player);
        }
    }

    [SubscribeLocalEvent]
    private void OnVendorPointsSpent(ref KsVendorPointsSpentEvent args)
    {
        if (!TryComp<ActorComponent>(args.Actor, out var actorComponent))
            return;

        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var ruleComponent, out _))
        {
            if (args.PointsType == ruleComponent.PointsType)
                ruleComponent.Points[actorComponent.PlayerSession.UserId] = args.Remaining;
        }
    }

    /// <summary>
    ///     Docks a defender who killed one of their own. Kills of the commander's units are paid in
    ///         <see cref="RewardUnitKill"/>.
    /// </summary>
    // MobStateChangedEvent is a by-value class event, so this is a plain broadcast handler.
    [SubscribeLocalEvent]
    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead ||
            GetKiller(args.Origin) is not { } killerUid ||
            killerUid == args.Target ||
            !TryComp<ActorComponent>(killerUid, out var killerActorComponent))
            return;

        var query = QueryActiveRules();
        while (query.MoveNext(out var ruleUid, out _, out var ruleComponent, out _))
        {
            if (!IsDefender(ruleComponent, args.Target) || !IsDefender(ruleComponent, killerUid))
                continue;

            AddPoints((ruleUid, ruleComponent), killerActorComponent.PlayerSession, ruleComponent.TeamkillPoints, TeamkillReason, Name(args.Target));
        }
    }

    /// <summary>
    ///     Who to credit a death to: whatever dealt the killing damage, or, for an explosion, which damages with no
    ///         origin, whoever set it off.
    /// </summary>
    private EntityUid? GetKiller(EntityUid? origin)
    {
        return origin ?? _explosionSystem.KsProcessingUser;
    }

    private void RewardUnitKill(Entity<KsFieldCommanderRuleComponent> rule, Entity<KsFieldCommanderUnitComponent> unit, EntityUid? killerUid)
    {
        if (GetKiller(killerUid) is not { } killer ||
            !IsDefender(rule.Comp, killer) ||
            !TryComp<ActorComponent>(killer, out var killerActorComponent) ||
            unit.Comp.AssetIndex >= rule.Comp.Assets.Count) // the list was changed since it was bought
            return;

        AddPoints(rule, killerActorComponent.PlayerSession, GetKillPoints(rule.Comp, rule.Comp.Assets[unit.Comp.AssetIndex]), KillReason, Name(unit));
    }

    public static int GetKillPoints(KsFieldCommanderRuleComponent component, KsFieldCommanderAsset asset)
    {
        return asset.KillPoints ?? (int)MathF.Round(asset.Cost * component.PointsPerCost);
    }

    /// <summary>
    ///     Pays every defender still standing for taking out the uplink.
    /// </summary>
    private void RewardUplink(Entity<KsFieldCommanderRuleComponent> rule)
    {
        if (rule.Comp.UplinkPoints == 0)
            return;

        var query = EntityQueryEnumerator<ScenarioFactionMemberComponent, MobStateComponent, ActorComponent>();
        while (query.MoveNext(out _, out var factionMemberComponent, out var mobStateComponent, out var actorComponent))
        {
            if (factionMemberComponent.Id != rule.Comp.DefenderFaction || mobStateComponent.CurrentState == MobState.Dead)
                continue;

            AddPoints(rule, actorComponent.PlayerSession, rule.Comp.UplinkPoints, UplinkReason);
        }
    }

    /// <summary>
    ///     For admins: gives a defender points, or takes them away.
    /// </summary>
    public void AdminAddPoints(Entity<KsFieldCommanderRuleComponent> rule, ICommonSession session, int delta)
    {
        AddPoints(rule, session, delta, AdminReason);
    }

    #endregion
}
