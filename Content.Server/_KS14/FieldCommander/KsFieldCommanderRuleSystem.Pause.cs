using Robust.Shared.Spawners;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Pause

    /// <summary>
    ///     The time on the commander's clock: now, or the moment it was paused. Everything it schedules is counted from
    ///         here, so that what is scheduled while paused is not pushed back again when it unpauses.
    /// </summary>
    private TimeSpan GetCommanderTime(KsFieldCommanderRuleComponent component)
    {
        return component.PausedAt ?? _gameTiming.CurTime;
    }

    public bool IsPaused(KsFieldCommanderRuleComponent component)
    {
        return component.PausedAt != null;
    }

    /// <summary>
    ///     Stops or restarts the commander's clock (see <see cref="KsFieldCommanderRuleComponent.PausedAt"/>). On the
    ///         way back every timer it keeps is moved on by however long it was paused, and the uplink's countdown,
    ///         held while paused, starts again where it stopped.
    /// </summary>
    public void SetPaused(Entity<KsFieldCommanderRuleComponent> rule, bool paused)
    {
        var component = rule.Comp;
        if (paused == IsPaused(component))
            return;

        var curTime = _gameTiming.CurTime;

        if (paused)
        {
            component.PausedAt = curTime;
            HoldUplinkCountdown(component);
            UpdateWaveStatus(component);
            return;
        }

        var pausedFor = curTime - component.PausedAt!.Value;
        component.PausedAt = null;
        ShiftTimers(rule, pausedFor);
        ReleaseUplinkCountdown(component);
        UpdateWaveStatus(component);

        if (component.PausedEndlessVoteResult is { } carryOn)
        {
            component.PausedEndlessVoteResult = null;
            OnEndlessVoteFinished(rule.Owner, carryOn);
        }
    }

    /// <summary>
    ///     Moves every absolute time the commander keeps on by <paramref name="delta"/>, and holds its timeline where
    ///         it was.
    /// </summary>
    private void ShiftTimers(Entity<KsFieldCommanderRuleComponent> rule, TimeSpan delta)
    {
        var component = rule.Comp;

        component.TimeOffset -= delta;
        component.NextOrderTime += delta;

        if (component.NextWaveTime != TimeSpan.MaxValue)
            component.NextWaveTime += delta;

        if (component.UplinkBombardmentTime is { } bombardmentTime)
            component.UplinkBombardmentTime = bombardmentTime + delta;

        // So that the round end says how long the uplink stood, not counting the pause.
        if (component.UplinkDeployedTime is { } deployedTime && component.UplinkDestroyedTime == null)
            component.UplinkDeployedTime = deployedTime + delta;

        if (component.MobikArrivalTime is { } mobikArrivalTime)
            component.MobikArrivalTime = mobikArrivalTime + delta;

        for (var i = 0; i < component.PendingIntel.Count; i++)
        {
            var intel = component.PendingIntel[i];
            component.PendingIntel[i] = intel with { At = intel.At + delta };
        }

        // Stuck checks only run with the orders, so a pause is no time spent stuck: nor time a nudge was held back,
        //      which would otherwise be made in sight the moment it resumes, nor time out of the damage grace.
        var query = EntityQueryEnumerator<KsFieldCommanderUnitComponent>();
        while (query.MoveNext(out var unitComponent))
        {
            if (unitComponent.RuleUid != rule.Owner)
                continue;

            unitComponent.LastProgressTime += delta;

            if (unitComponent.NudgeHeldSince is { } heldSince)
                unitComponent.NudgeHeldSince = heldSince + delta;

            if (unitComponent.LastDamagedTime is { } damagedTime)
                unitComponent.LastDamagedTime = damagedTime + delta;
        }
    }

    /// <summary>
    ///     Takes the uplink's countdown off it, so that it does not go off while the commander is paused.
    /// </summary>
    private void HoldUplinkCountdown(KsFieldCommanderRuleComponent component)
    {
        if (component.UplinkUid is not { } uplinkUid ||
            TerminatingOrDeleted(uplinkUid) ||
            !TryComp<TimedDespawnComponent>(uplinkUid, out var timedDespawnComponent))
            return;

        component.PausedUplinkLifetime = timedDespawnComponent.Lifetime;
        RemComp(uplinkUid, timedDespawnComponent);
    }

    private void ReleaseUplinkCountdown(KsFieldCommanderRuleComponent component)
    {
        if (component.PausedUplinkLifetime is not { } lifetime)
            return;

        component.PausedUplinkLifetime = null;
        if (component.UplinkUid is not { } uplinkUid || TerminatingOrDeleted(uplinkUid))
            return;

        EnsureComp<TimedDespawnComponent>(uplinkUid).Lifetime = lifetime;
    }

    #endregion
}
