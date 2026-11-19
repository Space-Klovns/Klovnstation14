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
    ///     <see cref="KsCCVars.FieldCommanderHealAttackerDamageOnly"/>: whether healing pays only for the attackers' share
    ///         of the damage (<see cref="GetAttackerShare"/>), rather than for everything but the defenders'.
    /// </summary>
    private bool _healAttackerDamageOnly;

    /// <summary>
    ///     How much of everything a defender took was <paramref name="userId"/>'s doing, 0 to 1.
    /// </summary>
    public static float GetBlameShare(Dictionary<NetUserId, float> damage, float otherDamage, NetUserId userId)
    {
        var total = otherDamage;
        foreach (var dealt in damage.Values)
            total += dealt;

        if (total <= 0f)
            return 0f;

        return Math.Clamp(damage.GetValueOrDefault(userId) / total, 0f, 1f);
    }

    /// <summary>
    ///     How much of everything a defender took was any defender's doing, 0 to 1.
    /// </summary>
    public static float GetDefenderShare(KsFieldCommanderDamageLedgerComponent ledgerComponent)
    {
        var defenderDamage = GetDefenderDamage(ledgerComponent);
        var total = defenderDamage + ledgerComponent.OtherDamage;
        return total <= 0f ? 0f : Math.Clamp(defenderDamage / total, 0f, 1f);
    }

    /// <summary>
    ///     How much of everything a defender took was the commander's side's doing, 0 to 1: not the defenders', the
    ///         world's or their own.
    /// </summary>
    public static float GetAttackerShare(KsFieldCommanderDamageLedgerComponent ledgerComponent)
    {
        var total = GetDefenderDamage(ledgerComponent) + ledgerComponent.OtherDamage;
        return total <= 0f ? 0f : Math.Clamp(ledgerComponent.AttackerDamage / total, 0f, 1f);
    }

    /// <summary>
    ///     Keeps who brought a defender down on their body, from the ledger about to be wiped, for whoever revives them.
    /// </summary>
    private void RecordDeath(EntityUid targetUid, KsFieldCommanderDamageLedgerComponent? ledgerComponent)
    {
        var recordComponent = EnsureComp<KsFieldCommanderDeathRecordComponent>(targetUid);
        recordComponent.Damage.Clear();
        recordComponent.OtherDamage = 0f;

        if (ledgerComponent == null)
            return;

        foreach (var (userId, dealt) in ledgerComponent.Damage)
            recordComponent.Damage[userId] = dealt;

        recordComponent.OtherDamage = ledgerComponent.OtherDamage;
    }

    /// <summary>
    ///     Pays whoever brought a dead defender back (a defibrillator names its user), less their share of what killed
    ///         them, once per <see cref="KsFieldCommanderRuleComponent.ReviveCooldown"/> for each defender. Nothing for
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

            var share = GetBlameShare(recordComponent.Damage, recordComponent.OtherDamage, reviverUserId);
            var points = (int)MathF.Round(ruleComponent.RevivePoints * (1f - share));
            recordComponent.LastRevivePaidTime = curTime;

            AddPoints((ruleUid, ruleComponent), session, points, ReviveReason, GetReasonName(targetUid));
        }
    }

    /// <summary>
    ///     Pays whoever healed a living defender for each point healed, less the defenders' share, anyone's, of the
    ///         damage the defender has taken; or, with <see cref="_healAttackerDamageOnly"/>, times the attackers' share,
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
        float paidShare;
        if (_healAttackerDamageOnly)
            paidShare = ledgerComponent == null ? 0f : GetAttackerShare(ledgerComponent);
        else
            paidShare = ledgerComponent == null ? 1f : 1f - GetDefenderShare(ledgerComponent);

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
