using Content.Shared._KS14.CCVar;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Robust.Shared.Network;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Medical

    private const string ReviveReason = "ks-fieldcommander-points-revive";
    private const string HealReason = "ks-fieldcommander-points-heal";

    /// <summary>
    ///     <see cref="KsCCVars.FieldCommanderHealAttackerDamageOnly"/>: whether healing and reviving pay only for the
    ///         attackers' share of the damage, rather than for everything but the defenders' (<see cref="GetPaidShare"/>).
    /// </summary>
    private bool _medicalAttackerDamageOnly;

    /// <summary>
    ///     How much of undoing a defender's damage a medic is paid for, 0 to 1, from what their ledger or death record
    ///         holds. Everything but the blamed defenders' share; or, with <paramref name="attackerDamageOnly"/>, only the
    ///         attackers' share, so that a wound from barbed wire, a fall or a defender pays nothing.
    /// </summary>
    /// <param name="defenderDamage">What each defender dealt them.</param>
    /// <param name="otherDamage">Everything else they took, the attackers' damage included.</param>
    /// <param name="attackerDamage">The part of <paramref name="otherDamage"/> the attackers dealt.</param>
    /// <param name="blamedUserId">
    ///     The one defender whose damage is taken off, the medic; null for every defender's. Unused with
    ///         <paramref name="attackerDamageOnly"/>, where no defender's damage pays anyway.
    /// </param>
    public static float GetPaidShare(Dictionary<NetUserId, float> defenderDamage,
        float otherDamage,
        float attackerDamage,
        NetUserId? blamedUserId,
        bool attackerDamageOnly)
    {
        var allDefenderDamage = GetDefenderDamage(defenderDamage);
        var total = allDefenderDamage + otherDamage;

        if (attackerDamageOnly)
            return total <= 0f ? 0f : Math.Clamp(attackerDamage / total, 0f, 1f);

        if (total <= 0f)
            return 1f;

        var blamed = blamedUserId is { } userId ? defenderDamage.GetValueOrDefault(userId) : allDefenderDamage;
        return 1f - Math.Clamp(blamed / total, 0f, 1f);
    }

    /// <summary>
    ///     Keeps who brought a defender down on their body, from the ledger about to be wiped, for whoever revives them.
    /// </summary>
    private void RecordDeath(EntityUid targetUid, KsFieldCommanderDamageLedgerComponent? ledgerComponent)
    {
        var recordComponent = EnsureComp<KsFieldCommanderDeathRecordComponent>(targetUid);
        recordComponent.Damage.Clear();
        recordComponent.OtherDamage = 0f;
        recordComponent.AttackerDamage = 0f;

        if (ledgerComponent == null)
            return;

        foreach (var (userId, dealt) in ledgerComponent.Damage)
            recordComponent.Damage[userId] = dealt;

        recordComponent.OtherDamage = ledgerComponent.OtherDamage;
        recordComponent.AttackerDamage = ledgerComponent.AttackerDamage;
    }

    /// <summary>
    ///     Gives a defender brought back their ledger as it stood at the death, from their death record, so that healing
    ///         the wounds they came back with is paid by who dealt them, as before they died. The defenders' part is
    ///         marked settled: it was charged as a teamkill at that death.
    /// </summary>
    private void RestoreLedger(EntityUid targetUid)
    {
        if (!TryComp<KsFieldCommanderDeathRecordComponent>(targetUid, out var recordComponent))
            return;

        var ledgerComponent = EnsureComp<KsFieldCommanderDamageLedgerComponent>(targetUid);
        foreach (var (userId, dealt) in recordComponent.Damage)
        {
            ledgerComponent.Damage[userId] = ledgerComponent.Damage.GetValueOrDefault(userId) + dealt;
            ledgerComponent.SettledDamage[userId] = ledgerComponent.SettledDamage.GetValueOrDefault(userId) + dealt;
        }

        ledgerComponent.OtherDamage += recordComponent.OtherDamage;
        ledgerComponent.AttackerDamage += recordComponent.AttackerDamage;
    }

    /// <summary>
    ///     Pays whoever brought a dead defender back (a defibrillator names its user), less their share of what killed
    ///         them, or only for the attackers' share with <see cref="_medicalAttackerDamageOnly"/>, once per <see cref="KsFieldCommanderRuleComponent.ReviveCooldown"/> for each defender. Nothing for
    ///         bringing back oneself.
    /// </summary>
    private void RewardRevive(EntityUid targetUid, EntityUid? originUid)
    {
        // Only a revive someone did: with no origin, the kill credit would look to an explosion going off.
        if (originUid is not { } reviverUid ||
            _killCreditSystem.GetRecipient(reviverUid) is not { } reviverUserId ||
            reviverUserId == _killCreditSystem.GetRecipient(targetUid) ||
            !TryComp<KsFieldCommanderDeathRecordComponent>(targetUid, out var recordComponent) ||
            !_playerManager.TryGetSessionById(reviverUserId, out var session))
            return;

        var curTime = _gameTiming.CurTime;

        var query = QueryActiveRules();
        while (query.MoveNext(out var ruleUid, out _, out var ruleComponent, out _))
        {
            if (ruleComponent.RevivePoints <= 0 ||
                !IsDefender(ruleComponent, targetUid) ||
                !IsDefenderPlayer(ruleComponent, reviverUserId) ||
                recordComponent.LastRevivePaidTime is { } paidTime && curTime - paidTime < ruleComponent.ReviveCooldown)
                continue;

            var paidShare = GetPaidShare(recordComponent.Damage,
                recordComponent.OtherDamage,
                recordComponent.AttackerDamage,
                blamedUserId: reviverUserId,
                attackerDamageOnly: _medicalAttackerDamageOnly);
            var points = (int)MathF.Round(ruleComponent.RevivePoints * paidShare);
            recordComponent.LastRevivePaidTime = curTime;

            AddPoints((ruleUid, ruleComponent), session, points, ReviveReason, GetReasonName(targetUid));
        }
    }

    /// <summary>
    ///     Pays whoever healed a living defender for each point healed, less the defenders' share, anyone's, of the
    ///         damage the defender has taken; or, with <see cref="_medicalAttackerDamageOnly"/>, times the attackers' share,
    ///         so that a wound from barbed wire or a fall pays nothing. Nothing for healing oneself, or the dead.
    /// </summary>
    private void RewardHealing(EntityUid targetUid, DamageChangedEvent args, KsFieldCommanderDamageLedgerComponent? ledgerComponent)
    {
        if (args.DamageDelta is not { } delta ||
            _mobStateQuery.TryComp(targetUid, out var mobStateComponent) && mobStateComponent.CurrentState == MobState.Dead)
            return;

        var healed = 0f;
        foreach (var amount in delta.DamageDict.Values)
        {
            if (amount < FixedPoint2.Zero)
                healed -= (float)amount;
        }

        if (healed <= 0f ||
            args.Origin is not { } healerUid ||
            _killCreditSystem.GetRecipient(healerUid) is not { } healerUserId ||
            healerUserId == _killCreditSystem.GetRecipient(targetUid) ||
            !_playerManager.TryGetSessionById(healerUserId, out var session))
            return;

        // Every defender's share, not only the healer's: otherwise one shoots a defender and another heals them, and
        //      they take turns. Paying only for the attackers' share shuts out the world's too: a defender walks into
        //      barbed wire, and another heals them.
        var paidShare = ledgerComponent == null
            ? GetPaidShare([], 0f, 0f, blamedUserId: null, attackerDamageOnly: _medicalAttackerDamageOnly)
            : GetPaidShare(ledgerComponent.Damage,
                ledgerComponent.OtherDamage,
                ledgerComponent.AttackerDamage,
                blamedUserId: null,
                attackerDamageOnly: _medicalAttackerDamageOnly);

        var query = QueryActiveRules();
        while (query.MoveNext(out var ruleUid, out _, out var ruleComponent, out _))
        {
            if (!(ruleComponent.HealPointsPerDamage > 0f) ||
                !IsDefender(ruleComponent, targetUid) ||
                !IsDefenderPlayer(ruleComponent, healerUserId))
                continue;

            // Kept to the fraction: a small heal is worth less than a point, and many add up.
            var earned = ruleComponent.HealPointsRemainder.GetValueOrDefault(healerUserId) +
                         healed * ruleComponent.HealPointsPerDamage * paidShare;
            var points = (int)MathF.Floor(earned);
            ruleComponent.HealPointsRemainder[healerUserId] = earned - points;

            if (points > 0)
                AddPoints((ruleUid, ruleComponent), session, points, HealReason, GetReasonName(targetUid));
        }
    }

    #endregion
}
