namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Assets the commander sends whatever its budget would rather buy.
/// </summary>
public sealed partial class KsFieldCommanderAsset
{
    /// <summary>
    ///     Every this many waves (counting all of the round's, from the first), one purchase of this comes with the wave,
    ///         before anything is bought at random. Zero for never.
    /// </summary>
    /// <remarks>
    ///     A guaranteed purchase is charged to the wave's budget, even past what it has, but still keeps to the alive
    ///         caps: it is left out of a wave with no room for it.
    /// </remarks>
    [DataField]
    public int GuaranteedEveryWaves;

    /// <summary>
    ///     Whether one purchase of this comes with the first wave of every phase from <see cref="MinPhase"/> on, as
    ///         <see cref="GuaranteedEveryWaves"/> does.
    /// </summary>
    [DataField]
    public bool GuaranteedOnPhaseEnter;

    /// <summary>
    ///     Taken out of the commander's hands: never bought, guaranteed or queued again. Assets are never removed,
    ///         as units in the field point at theirs by index, so this is how one is retired.
    /// </summary>
    [DataField]
    public bool Disabled;
}

public sealed partial class KsFieldCommanderRuleComponent
{
    /// <summary>
    ///     Budget waves sent since the current phase began, for <see cref="KsFieldCommanderAsset.GuaranteedOnPhaseEnter"/>:
    ///         queued waves don't count, as they buy no guaranteed assets.
    /// </summary>
    [ViewVariables]
    public int PhaseWavesSent;
}
