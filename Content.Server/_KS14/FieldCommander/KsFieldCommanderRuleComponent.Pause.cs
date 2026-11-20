namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Pausing stops the commander's clock only: no phase, wave, order, intelligence or bombardment comes due, but its
///         units in the field and the defenders fight on.
/// </summary>
public sealed partial class KsFieldCommanderRuleComponent
{
    /// <summary>
    ///     When the commander was paused, or null while it runs. Read-only in VV: unpausing has to shift every timer,
    ///         which <see cref="KsFieldCommanderRuleSystem.SetPaused"/> does.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan? PausedAt;

    /// <summary>
    ///     What was left of the uplink's countdown when it was paused, held off the uplink until it unpauses.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float? PausedUplinkLifetime;

    /// <summary>
    ///     The outcome of an endless vote that ended while paused, acted on as it unpauses.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool? PausedEndlessVoteResult;
}
