namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Endless mode: once the last phase is over (the uplink is gone), the players vote on whether to carry on. If they
///         do, the commander keeps sending waves, each a little stronger and a little sooner than the last, until the
///         defenders are overrun.
/// </summary>
public sealed partial class KsFieldCommanderRuleComponent
{
    #region Endless

    /// <summary>
    ///     Whether the round asks the players to carry on in endless mode once the last phase is over. If not, or the
    ///         vote fails, the round ends there as before.
    /// </summary>
    [DataField]
    public bool EndlessVote = true;

    /// <summary>
    ///     How long the vote runs.
    /// </summary>
    [DataField]
    public TimeSpan EndlessVoteDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     From the vote passing to the first endless wave.
    /// </summary>
    [DataField]
    public TimeSpan EndlessFirstWaveDelay = TimeSpan.FromSeconds(45);

    /// <summary>
    ///     Between the first endless waves. Each wave after it comes <see cref="EndlessIntervalScale"/> times sooner,
    ///         down to <see cref="EndlessMinInterval"/>.
    /// </summary>
    [DataField]
    public TimeSpan EndlessWaveInterval = TimeSpan.FromSeconds(110);

    [DataField]
    public float EndlessIntervalScale = 0.93f;

    [DataField]
    public TimeSpan EndlessMinInterval = TimeSpan.FromSeconds(40);

    /// <summary>
    ///     How much the last phase's wave budget grows with each endless wave: wave n of endless mode spends
    ///         <c>1 + n * EndlessBudgetGrowth</c> times as much.
    /// </summary>
    [DataField]
    public float EndlessBudgetGrowth = 0.2f;

    /// <summary>
    ///     How many more units may be alive at once with each endless wave, still capped by
    ///         <see cref="HardMaxAlive"/>.
    /// </summary>
    [DataField]
    public float EndlessMaxAliveGrowth = 1f;

    /// <summary>
    ///     Sent as the vote opens. Takes <c>seconds</c>.
    /// </summary>
    [DataField]
    public LocId? EndlessVoteIntel = "ks-fieldcommander-intel-endless-vote";

    /// <summary>
    ///     The commander's taunt, and the defenders' intel, as endless mode begins.
    /// </summary>
    [DataField]
    public LocId? EndlessAnnouncement = "ks-fieldcommander-endless";

    [DataField]
    public LocId? EndlessIntel = "ks-fieldcommander-intel-endless";

    /// <summary>
    ///     Whether the vote on endless mode is running now.
    /// </summary>
    [ViewVariables]
    public bool EndlessVoteRunning;

    /// <summary>
    ///     Whether the round is in endless mode.
    /// </summary>
    [ViewVariables]
    public bool Endless;

    /// <summary>
    ///     Waves sent since endless mode began.
    /// </summary>
    [ViewVariables]
    public int EndlessWavesSent;

    #endregion
}
