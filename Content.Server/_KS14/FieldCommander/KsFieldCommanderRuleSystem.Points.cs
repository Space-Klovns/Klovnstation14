using Content.Server._KS14.KillCredit;
using Content.Shared.GameTicking;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
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
    private const string AssistReason = "ks-fieldcommander-points-assist";
    private const string TeamkillReason = "ks-fieldcommander-points-teamkill";
    private const string TeamkillAssistReason = "ks-fieldcommander-points-teamkill-assist";
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

        // So the client knows who is a defender, and how to flash them as they go down.
        var defenderComponent = EnsureComp<KsFieldCommanderDefenderComponent>(bodyUid);
        _defenderSystem.SetFlashes((bodyUid, defenderComponent), component.DownFlashes);

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
    ///     Docks the defenders who killed one of their own, by how much of the damage each dealt: the one who dealt
    ///         the most is the teamkiller and the rest assisted. Kills of the commander's units are paid in
    ///         <see cref="RewardUnitKill"/>.
    /// </summary>
    // MobStateChangedEvent is a by-value class event, so this is a plain broadcast handler.
    [SubscribeLocalEvent]
    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead ||
            !TryComp<KsFieldCommanderDamageLedgerComponent>(args.Target, out var ledgerComponent))
            return;

        var query = QueryActiveRules();
        while (query.MoveNext(out var ruleUid, out _, out var ruleComponent, out _))
        {
            if (!IsDefender(ruleComponent, args.Target))
                continue;

            PayShares((ruleUid, ruleComponent), ledgerComponent, ruleComponent.TeamkillPoints, TeamkillReason, TeamkillAssistReason, Name(args.Target));
        }

        // Paid out: whoever brings them back starts with a clean slate.
        RemCompDeferred<KsFieldCommanderDamageLedgerComponent>(args.Target);
    }

    /// <summary>
    ///     Who to credit damage to: whatever dealt it, or, for an explosion, which damages with no origin, whoever
    ///         set it off.
    /// </summary>
    private EntityUid? GetKiller(EntityUid? origin)
    {
        return _killCreditSystem.ResolveCredit(origin);
    }

    /// <summary>
    ///     Keeps count of who damaged a unit, for <see cref="RewardUnitKill"/> to split the kill by.
    /// </summary>
    // DamageChangedEvent is a by-value class event, so the engine can't give this an Entity<T>.
    // Before the thresholds system, whose handler is what kills the unit: the last shot has to be counted by then.
    [SubscribeLocalEvent(before: [typeof(MobThresholdSystem)])]
    private void OnUnitDamageChanged(EntityUid unitUid, KsFieldCommanderUnitComponent unitComponent, DamageChangedEvent args)
    {
        if (TryComp<KsFieldCommanderRuleComponent>(unitComponent.RuleUid, out var ruleComponent))
            RecordDefenderDamage(unitUid, args, ruleComponent);
    }

    /// <summary>
    ///     Keeps count of which defenders damaged a defender, for <see cref="OnMobStateChanged"/> to split a teamkill by.
    ///         Healed back to full, the slate is wiped: old friendly fire isn't charged to a later death.
    /// </summary>
    // By-value class event again, and ordered like OnUnitDamageChanged.
    [SubscribeLocalEvent(before: [typeof(MobThresholdSystem)])]
    private void OnDefenderDamageChanged(EntityUid defenderUid, ScenarioFactionMemberComponent factionMemberComponent, DamageChangedEvent args)
    {
        if (!_damageableSystem.GetAllDamage((defenderUid, args.Damageable)).AnyPositive())
        {
            RemCompDeferred<KsFieldCommanderDamageLedgerComponent>(defenderUid);
            return;
        }

        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var ruleComponent, out _))
        {
            if (IsDefender(ruleComponent, defenderUid))
                RecordDefenderDamage(defenderUid, args, ruleComponent);
        }
    }

    /// <summary>
    ///     Adds the damage a defender just dealt to <paramref name="targetUid"/> to its ledger. Nobody else's damage
    ///         counts, and neither does damage to oneself.
    /// </summary>
    private void RecordDefenderDamage(EntityUid targetUid, DamageChangedEvent args, KsFieldCommanderRuleComponent ruleComponent)
    {
        if (args.DamageDelta is not { } delta ||
            !args.DamageIncreased ||
            GetKiller(args.Origin) is not { } attacker ||
            attacker == targetUid ||
            !TryComp<ActorComponent>(attacker, out var attackerActorComponent) ||
            !IsDefender(ruleComponent, attacker))
            return;

        var dealt = 0f;
        foreach (var amount in delta.DamageDict.Values)
        {
            if (amount > FixedPoint2.Zero)
                dealt += (float)amount;
        }

        if (dealt <= 0f)
            return;

        var ledgerComponent = EnsureComp<KsFieldCommanderDamageLedgerComponent>(targetUid);
        var userId = attackerActorComponent.PlayerSession.UserId;
        ledgerComponent.Damage[userId] = ledgerComponent.Damage.GetValueOrDefault(userId) + dealt;
    }

    private void RewardUnitKill(Entity<KsFieldCommanderRuleComponent> rule, Entity<KsFieldCommanderUnitComponent> unit)
    {
        if (unit.Comp.AssetIndex >= rule.Comp.Assets.Count || // the list was changed since it was bought
            !TryComp<KsFieldCommanderDamageLedgerComponent>(unit, out var ledgerComponent))
            return;

        PayShares(rule, ledgerComponent, GetKillPoints(rule.Comp, rule.Comp.Assets[unit.Comp.AssetIndex]), KillReason, AssistReason, Name(unit));
    }

    /// <summary>
    ///     Splits <paramref name="total"/> between the defenders in a ledger by how much damage each dealt. The one
    ///         who dealt the most gets <paramref name="topReason"/>, everyone else <paramref name="otherReason"/>.
    /// </summary>
    private void PayShares(Entity<KsFieldCommanderRuleComponent> rule, KsFieldCommanderDamageLedgerComponent ledgerComponent, int total, string topReason, string otherReason, string reasonName)
    {
        // Even defenders who've died since still collect, as long as they're connected: dying shouldn't erase an assist.
        var totalDamage = 0f;
        foreach (var damage in ledgerComponent.Damage.Values)
        {
            totalDamage += damage;
        }

        if (totalDamage <= 0f)
            return;

        var shares = new List<(NetUserId UserId, float Damage, int Points)>();
        var paid = 0;
        foreach (var (userId, damage) in ledgerComponent.Damage)
        {
            var points = (int)MathF.Round(total * damage / totalDamage);
            paid += points;
            shares.Add((userId, damage, points));
        }

        // Rounding can leave a point or two over or short: the top contributor absorbs it, so the total stays whole.
        var top = 0;
        for (var i = 1; i < shares.Count; i++)
        {
            if (shares[i].Damage > shares[top].Damage)
                top = i;
        }

        shares[top] = shares[top] with { Points = shares[top].Points + total - paid };

        for (var i = 0; i < shares.Count; i++)
        {
            var (userId, _, points) = shares[i];
            if (points == 0 || !_playerManager.TryGetSessionById(userId, out var session))
                continue;

            AddPoints(rule, session, points, i == top ? topReason : otherReason, reasonName);
        }
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
