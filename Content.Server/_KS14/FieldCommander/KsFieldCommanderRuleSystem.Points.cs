using Content.Server._KS14.KillCredit;
using Content.Shared.GameTicking;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared._KS14.FactionCollision;
using Content.Shared._KS14.FieldCommander;
using Content.Shared._KS14.PopupLocalization;
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
    private const string MobikReason = "ks-fieldcommander-points-mobik";

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
    /// <param name="reason">Localisation id of a short reason. Takes <c>name</c>, if there is a <paramref name="reasonName"/>.</param>
    /// <param name="reasonName">
    ///     What the reason names, as something the client can put in its own language: see <see cref="GetReasonName"/>.
    ///         Never a name formatted here, which would be in the server's.
    /// </param>
    public void AddPoints(Entity<KsFieldCommanderRuleComponent> rule, ICommonSession session, int delta, string reason, object? reasonName = null)
    {
        if (delta == 0)
            return;

        var total = GetPoints(rule.Comp, session.UserId) + delta;
        SetPoints(rule, session, total);

        var filter = Filter.SinglePlayer(session);
        _audioSystem.PlayGlobal(delta > 0 ? rule.Comp.PointsGainedSound : rule.Comp.PointsLostSound, filter, recordReplay: false);

        // Sent unformatted: the client's language is its own, not the server's.
        var message = reasonName != null ? KsPopupMessage.Create(reason, ("name", reasonName)) : KsPopupMessage.Create(reason);
        RaiseNetworkEvent(new KsFieldCommanderPointsChangedEvent(delta, message.ToPayload(EntityManager)), filter);
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
    ///     Readies a defender's body: their shots pass through their own side, and their points and perk come with them.
    /// </summary>
    private void PrepareDefender(Entity<KsFieldCommanderRuleComponent> rule, EntityUid bodyUid, ICommonSession? session)
    {
        var component = rule.Comp;
        EnsureComp<KsFactionCollisionShooterComponent>(bodyUid);

        // So the client knows who is a defender, and how to flash them as they go down.
        var defenderComponent = EnsureComp<KsFieldCommanderDefenderComponent>(bodyUid);
        _defenderSystem.SetFlashes((bodyUid, defenderComponent), component.DownFlashes);

        if (session != null)
        {
            // A player's body only: the beacon is for finding them when they go down.
            _beaconSystem.GiveBeacon(bodyUid);

            component.Defenders.Add(session.UserId);
            MirrorPoints(component, bodyUid, GetPoints(component, session.UserId));
            ApplyOrRequestPerk(rule, bodyUid, session);
        }
    }

    /// <summary>
    ///     Readies the defenders who were already in the round before the rule was.
    /// </summary>
    private void PrepareExistingDefenders(Entity<KsFieldCommanderRuleComponent> rule)
    {
        var component = rule.Comp;
        var query = EntityQueryEnumerator<ScenarioFactionMemberComponent>();
        while (query.MoveNext(out var memberUid, out var factionMemberComponent))
        {
            if (factionMemberComponent.Id != component.DefenderFaction)
                continue;

            PrepareDefender(rule, memberUid, CompOrNull<ActorComponent>(memberUid)?.PlayerSession);
        }
    }

    [SubscribeLocalEvent]
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var ruleUid, out _, out var ruleComponent, out _))
        {
            if (IsDefender(ruleComponent, args.Mob))
                PrepareDefender((ruleUid, ruleComponent), args.Mob, args.Player);
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
    ///     A defender's death and return: see <see cref="OnDefenderDied"/>, and <see cref="RewardRevive"/> for being
    ///         brought back.
    /// </summary>
    // MobStateChangedEvent is a by-value class event, so this is a plain broadcast handler.
    [SubscribeLocalEvent]
    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        // A unit's ledger is paid and wiped by RewardUnitKill, whichever of the two handlers hears of the death first.
        if (HasComp<KsFieldCommanderUnitComponent>(args.Target))
            return;

        if (args.NewMobState == MobState.Dead)
            OnDefenderDied(args.Target);
        else if (args.OldMobState == MobState.Dead)
        {
            RewardRevive(args.Target, args.Origin);
            RestoreLedger(args.Target);
        }
    }

    /// <summary>
    ///     Docks the defenders who killed one of their own. The penalty is scaled by how much of everything the
    ///         victim took was their own side's doing (<see cref="GetTeamkillPenalty"/>), and split between those
    ///         defenders by how much each dealt: the one who dealt the most is the teamkiller and the rest assisted.
    ///         Kills of the commander's units are paid in <see cref="RewardUnitKill"/>. Who did it is kept on the body
    ///         for whoever revives it (<see cref="KsFieldCommanderDeathRecordComponent"/>).
    /// </summary>
    private void OnDefenderDied(EntityUid targetUid)
    {
        TryComp<KsFieldCommanderDamageLedgerComponent>(targetUid, out var ledgerComponent);

        var recorded = false;
        var query = QueryActiveRules();
        while (query.MoveNext(out var ruleUid, out _, out var ruleComponent, out _))
        {
            if (!IsDefender(ruleComponent, targetUid))
                continue;

            if (!recorded)
            {
                RecordDeath(targetUid, ledgerComponent);
                recorded = true;

                // Whoever revives them is weighed against everything that killed them, but a teamkill already charged
                //      at an earlier death is not charged again.
                if (ledgerComponent != null)
                    TakeOutSettledDamage(ledgerComponent);
            }

            if (ledgerComponent == null)
                continue;

            var defenderDamage = GetDefenderDamage(ledgerComponent);
            if (defenderDamage <= 0f)
                continue;

            var penalty = GetTeamkillPenalty(ruleComponent, defenderDamage / (defenderDamage + ledgerComponent.OtherDamage));
            PayShares((ruleUid, ruleComponent), ledgerComponent, penalty, TeamkillReason, TeamkillAssistReason, GetReasonName(targetUid));
        }

        // Paid out: whoever brings them back starts with a clean slate.
        if (ledgerComponent != null)
            WipeLedger((targetUid, ledgerComponent));
    }

    private static void TakeOutSettledDamage(KsFieldCommanderDamageLedgerComponent ledgerComponent)
    {
        foreach (var (userId, settled) in ledgerComponent.SettledDamage)
        {
            if (!ledgerComponent.Damage.TryGetValue(userId, out var dealt))
                continue;

            if (dealt - settled > 0f)
                ledgerComponent.Damage[userId] = dealt - settled;
            else
                ledgerComponent.Damage.Remove(userId);
        }

        ledgerComponent.SettledDamage.Clear();
    }

    /// <summary>
    ///     Empties a ledger and takes it off its entity. Emptied as well as taken off: a component removed this way is
    ///         still stored, and still found, until the tick ends, and a death later in the same tick must not be paid
    ///         from it.
    /// </summary>
    private void WipeLedger(Entity<KsFieldCommanderDamageLedgerComponent> ledger)
    {
        ledger.Comp.Damage.Clear();
        ledger.Comp.SettledDamage.Clear();
        ledger.Comp.OtherDamage = 0f;
        ledger.Comp.AttackerDamage = 0f;
        RemCompDeferred<KsFieldCommanderDamageLedgerComponent>(ledger.Owner);
    }

    /// <summary>
    ///     What a teamkill costs the defenders behind it, between them: <see cref="KsFieldCommanderRuleComponent.TeamkillPoints"/>
    ///         times their side's share of everything the victim took, the enemy's and the world's damage included, but
    ///         never less than <see cref="KsFieldCommanderRuleComponent.TeamkillMinimumPoints"/>, nor more than the
    ///         whole penalty.
    /// </summary>
    /// <param name="defenderShare">How much of all the damage the victim took was dealt by defenders, 0 to 1.</param>
    public static int GetTeamkillPenalty(KsFieldCommanderRuleComponent component, float defenderShare)
    {
        var scaled = (int)MathF.Round(component.TeamkillPoints * Math.Clamp(defenderShare, 0f, 1f));

        // Both are losses, so below zero: the bigger loss of the two, capped at the whole penalty.
        return Math.Max(component.TeamkillPoints, Math.Min(scaled, component.TeamkillMinimumPoints));
    }

    private static float GetDefenderDamage(KsFieldCommanderDamageLedgerComponent ledgerComponent)
    {
        return GetDefenderDamage(ledgerComponent.Damage);
    }

    private static float GetDefenderDamage(Dictionary<NetUserId, float> defenderDamage)
    {
        var total = 0f;
        foreach (var damage in defenderDamage.Values)
        {
            total += damage;
        }

        return total;
    }

    /// <summary>
    ///     Whether a player is on the defenders' side. Whoever they are now decides, if they are anyone: a defender's
    ///         body, or a unit the commander's side handed them. Dead and watching, or between bodies, they are a
    ///         defender if they have been one this round, so a turret goes on paying its owner.
    /// </summary>
    private bool IsDefenderPlayer(KsFieldCommanderRuleComponent component, NetUserId userId)
    {
        if (_playerManager.TryGetSessionById(userId, out var session) &&
            session.AttachedEntity is { } bodyUid &&
            _scenarioFactionMemberQuery.TryComp(bodyUid, out var factionMemberComponent))
        {
            if (factionMemberComponent.Id != component.DefenderFaction)
                return false;

            component.Defenders.Add(userId);
            return true;
        }

        return component.Defenders.Contains(userId);
    }

    /// <summary>
    ///     What to call an entity in a points line, for the client to put in its own language: its prototype, unless
    ///         it has a name of its own (a player's character), which is sent as it is.
    /// </summary>
    private object GetReasonName(EntityUid uid)
    {
        var metaDataComponent = MetaData(uid);
        return metaDataComponent.EntityPrototype is { } prototype && metaDataComponent.EntityName == prototype.Name
            ? new KsPopupPrototypeName(prototype.ID)
            : metaDataComponent.EntityName;
    }

    /// <summary>
    ///     Keeps count of who damaged a unit, for <see cref="RewardUnitKill"/> to split the kill by.
    /// </summary>
    // DamageChangedEvent is a by-value class event, so the engine can't give this an Entity<T>.
    // Before the thresholds and destructible systems, whose handlers are what kill or destroy the unit: the last shot
    //      has to be counted by then.
    [SubscribeLocalEvent(before: [typeof(MobThresholdSystem), typeof(Content.Server.Destructible.DestructibleSystem)])]
    private void OnUnitDamageChanged(EntityUid unitUid, KsFieldCommanderUnitComponent unitComponent, DamageChangedEvent args)
    {
        // A unit being shot at is in a fight, not stuck (see .Stuck).
        if (args.DamageIncreased)
            unitComponent.LastDamagedTime = _gameTiming.CurTime;

        if (TryComp<KsFieldCommanderRuleComponent>(unitComponent.RuleUid, out var ruleComponent))
            RecordDamage(unitUid, args, ruleComponent, recordOther: false);
    }

    /// <summary>
    ///     Keeps count of who damaged a defender, their own side and everything else, for <see cref="OnMobStateChanged"/>
    ///         to work a teamkill out from. Healed back to full, the slate is wiped: old friendly fire isn't charged to
    ///         a later death.
    /// </summary>
    // By-value class event again, and ordered like OnUnitDamageChanged, as every subscription of one system to one
    //      event must be.
    [SubscribeLocalEvent(before: [typeof(MobThresholdSystem), typeof(Content.Server.Destructible.DestructibleSystem)])]
    private void OnDefenderDamageChanged(EntityUid defenderUid, ScenarioFactionMemberComponent factionMemberComponent, DamageChangedEvent args)
    {
        // Every hit on every member of every faction comes through here, so the cheap answers come first: only
        //      healing can leave them unhurt, and only a ledger can be wiped. GetAllDamage copies the whole dictionary.
        if (!args.DamageIncreased)
        {
            TryComp<KsFieldCommanderDamageLedgerComponent>(defenderUid, out var healedLedgerComponent);

            // Paid from the ledger as it stands, before a full heal wipes it.
            if (args.Origin != null)
                RewardHealing(defenderUid, args, healedLedgerComponent);

            if (healedLedgerComponent != null &&
                !_damageableSystem.GetAllDamage((defenderUid, args.Damageable)).AnyPositive())
                WipeLedger((defenderUid, healedLedgerComponent));

            return;
        }

        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var ruleComponent, out _))
        {
            if (IsDefender(ruleComponent, defenderUid))
                RecordDamage(defenderUid, args, ruleComponent, recordOther: true);
        }
    }

    /// <summary>
    ///     Adds damage just dealt to <paramref name="targetUid"/> to its ledger: under the defender who is paid for it,
    ///         asked now, while what dealt it (a bullet, a turret about to be destroyed) still exists to say whose it
    ///         is. Damage to oneself is nobody's.
    /// </summary>
    /// <param name="recordOther">
    ///     Whether damage that no defender is paid for is counted as well, as one total: the enemy's, the world's, the
    ///         target's own. A teamkill is weighed against it; a kill is split between defenders only.
    /// </param>
    private void RecordDamage(EntityUid targetUid, DamageChangedEvent args, KsFieldCommanderRuleComponent ruleComponent, bool recordOther)
    {
        if (args.DamageDelta is not { } delta || !args.DamageIncreased)
            return;

        var dealt = 0f;
        foreach (var amount in delta.DamageDict.Values)
        {
            if (amount > FixedPoint2.Zero)
                dealt += (float)amount;
        }

        if (dealt <= 0f)
            return;

        var recipient = _killCreditSystem.GetHitRecipient(args.Origin);
        var credited = recipient is { } recipientUserId &&
            recipientUserId != _killCreditSystem.GetRecipient(targetUid) &&
            IsDefenderPlayer(ruleComponent, recipientUserId);

        if (!credited && !recordOther)
            return;

        var ledgerComponent = EnsureComp<KsFieldCommanderDamageLedgerComponent>(targetUid);
        if (credited)
        {
            ledgerComponent.Damage[recipient!.Value] = ledgerComponent.Damage.GetValueOrDefault(recipient.Value) + dealt;
            return;
        }

        ledgerComponent.OtherDamage += dealt;
        if (IsAttackerHit(ruleComponent, args.Origin))
            ledgerComponent.AttackerDamage += dealt;
    }

    /// <summary>
    ///     Whether damage dealt with this <paramref name="origin"/> was the commander's side's doing: anything on its
    ///         faction, its units first of all, hitting directly, with a bullet it fired, with a gun it holds, or with an
    ///         explosion it set off or sent (a mortar crewman's shell). To be asked as the damage lands. An attacker
    ///         already gone, or a strike missile, which nothing sends, is not recognised.
    /// </summary>
    private bool IsAttackerHit(KsFieldCommanderRuleComponent component, EntityUid? origin)
    {
        if (_killCreditSystem.GetAttacker(origin) is not { } attackerUid || Deleted(attackerUid))
            return false;

        if (IsAttackerSide(component, attackerUid))
            return true;

        // A beam names the gun, not who holds it.
        return _containerSystem.TryGetContainingContainer(attackerUid, out var container) &&
               IsAttackerSide(component, container.Owner);
    }

    private bool IsAttackerSide(KsFieldCommanderRuleComponent component, EntityUid uid)
    {
        return _scenarioFactionMemberQuery.TryComp(uid, out var factionMemberComponent) &&
               factionMemberComponent.Id == component.Faction;
    }

    private void RewardUnitKill(Entity<KsFieldCommanderRuleComponent> rule, Entity<KsFieldCommanderUnitComponent> unit)
    {
        if (!TryGetUnitAsset(rule.Comp, unit.Comp, out var asset) || // the list was changed since it was bought
            !TryComp<KsFieldCommanderDamageLedgerComponent>(unit, out var ledgerComponent))
            return;

        PayShares(rule, ledgerComponent, GetKillPoints(rule.Comp, asset), KillReason, AssistReason, GetReasonName(unit));

        // Paid out: a unit brought back and killed again is a new kill.
        WipeLedger((unit.Owner, ledgerComponent));
    }

    /// <summary>
    ///     Splits <paramref name="total"/> between the defenders in a ledger by how much damage each dealt. The one
    ///         who dealt the most gets <paramref name="topReason"/>, everyone else <paramref name="otherReason"/>.
    /// </summary>
    private void PayShares(Entity<KsFieldCommanderRuleComponent> rule, KsFieldCommanderDamageLedgerComponent ledgerComponent, int total, string topReason, string otherReason, object reasonName)
    {
        // Even defenders who've died since still collect, as long as they're connected: dying shouldn't erase an assist.
        var totalDamage = GetDefenderDamage(ledgerComponent);
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
        RewardLivingDefenders(rule, rule.Comp.UplinkPoints, UplinkReason);
    }

    /// <summary>
    ///     Pays every defender still standing <paramref name="points"/>, for <paramref name="reason"/>.
    /// </summary>
    private void RewardLivingDefenders(Entity<KsFieldCommanderRuleComponent> rule, int points, string reason)
    {
        if (points == 0)
            return;

        var query = EntityQueryEnumerator<ScenarioFactionMemberComponent, MobStateComponent, ActorComponent>();
        while (query.MoveNext(out _, out var factionMemberComponent, out var mobStateComponent, out var actorComponent))
        {
            if (factionMemberComponent.Id != rule.Comp.DefenderFaction || mobStateComponent.CurrentState == MobState.Dead)
                continue;

            AddPoints(rule, actorComponent.PlayerSession, points, reason);
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
