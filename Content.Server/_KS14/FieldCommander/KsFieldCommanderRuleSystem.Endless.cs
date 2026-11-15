using Content.Server.Voting;
using Content.Server.Voting.Managers;
using Content.Shared._KS14.Voting;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    public static readonly LocId EndlessVoteTitle = "ks-fieldcommander-endless-vote-title";
    private static readonly LocId EndlessVoteInitiator = "ks-fieldcommander-endless-vote-initiator";
    private static readonly LocId EndlessVoteYes = "ks-fieldcommander-endless-vote-yes";
    private static readonly LocId EndlessVoteNo = "ks-fieldcommander-endless-vote-no";

    [Dependency] private IVoteManager _voteManager = default!;

    #region Endless

    /// <summary>
    ///     The uplink is gone, so the last phase is over. Rather than let the round end there, holds it open and asks the
    ///         players whether to carry on in endless mode. False, and the round ends as it would have, if it doesn't ask.
    /// </summary>
    private bool TryStartEndlessVote(Entity<KsFieldCommanderRuleComponent> rule)
    {
        var component = rule.Comp;
        if (!component.EndlessVote ||
            component.Endless ||
            component.EndlessVoteRunning ||
            // The scenario would otherwise end the round over the lost uplink as soon as this returns.
            !_scenarioSystem.SetObjectivePending(component.Faction, pending: true))
            return false;

        component.EndlessVoteRunning = true;

        var options = new VoteOptions
        {
            // Not formatted here, where it would be in the server's language: each client resolves them.
            Title = KsVoteText.Deferred(EndlessVoteTitle),
            InitiatorText = KsVoteText.Deferred(EndlessVoteInitiator),
            Duration = component.EndlessVoteDuration,
            Options =
            {
                (KsVoteText.Deferred(EndlessVoteYes), true),
                (KsVoteText.Deferred(EndlessVoteNo), false),
            },
        };

        var ruleUid = rule.Owner;
        var vote = _voteManager.CreateVote(options);
        vote.OnFinished += (_, args) => OnEndlessVoteFinished(ruleUid, args.Winner is true);
        vote.OnCancelled += _ => OnEndlessVoteFinished(ruleUid, carryOn: false);

        Transmit(component, taunt: null, major: false, component.EndlessVoteIntel,
            ("seconds", (int)component.EndlessVoteDuration.TotalSeconds));
        return true;
    }

    /// <summary>
    ///     Carries on in endless mode if the vote passed. If it failed, was tied or was cancelled, lets the round end
    ///         over the uplink, as it would have without the vote.
    /// </summary>
    private void OnEndlessVoteFinished(EntityUid ruleUid, bool carryOn)
    {
        // The round may have ended under the vote, with the defenders overrun while it ran.
        if (!TryComp<KsFieldCommanderRuleComponent>(ruleUid, out var component) ||
            !component.EndlessVoteRunning ||
            !GameTicker.IsGameRuleActive(ruleUid))
            return;

        component.EndlessVoteRunning = false;

        if (carryOn)
        {
            StartEndless((ruleUid, component));
            return;
        }

        _scenarioSystem.SetObjectivePending(component.Faction, pending: false);
        _scenarioSystem.CheckObjectiveRoundEnd();
    }

    /// <summary>
    ///     Starts endless mode now. The commander's side keeps counting as holding an objective, so the round only ends
    ///         once the defenders are overrun or lose their own objectives.
    /// </summary>
    public void StartEndless(Entity<KsFieldCommanderRuleComponent> rule)
    {
        var component = rule.Comp;
        if (component.Endless)
            return;

        _scenarioSystem.SetObjectivePending(component.Faction, pending: true);

        component.Endless = true;
        component.EndlessWavesSent = 0;
        component.CarriedBudget = 0f;
        SetNextWave(component, _gameTiming.CurTime + component.EndlessFirstWaveDelay);

        Transmit(component, component.EndlessAnnouncement, major: true, component.EndlessIntel);
    }

    /// <summary>
    ///     When the wave after one sent at <paramref name="curTime"/> is due: the phase's own interval, or in endless
    ///         mode one that shortens with every wave.
    /// </summary>
    private static TimeSpan GetNextWaveTime(KsFieldCommanderRuleComponent component, KsFieldCommanderPhase phase, TimeSpan curTime)
    {
        if (!component.Endless)
            return ScheduleWave(phase, curTime + phase.WaveInterval);

        var interval = component.EndlessWaveInterval * MathF.Pow(component.EndlessIntervalScale, component.EndlessWavesSent);
        return curTime + (interval > component.EndlessMinInterval ? interval : component.EndlessMinInterval);
    }

    /// <summary>
    ///     How many times the phase's budget an endless wave spends; 1 outside endless mode.
    /// </summary>
    public static float GetEndlessBudgetScale(KsFieldCommanderRuleComponent component)
    {
        return component.Endless ? 1f + component.EndlessWavesSent * component.EndlessBudgetGrowth : 1f;
    }

    /// <summary>
    ///     How many more units may be alive at once in endless mode; 0 outside it.
    /// </summary>
    private static int GetEndlessExtraAlive(KsFieldCommanderRuleComponent component)
    {
        return component.Endless ? (int)MathF.Floor(component.EndlessWavesSent * component.EndlessMaxAliveGrowth) : 0;
    }

    #endregion
}
