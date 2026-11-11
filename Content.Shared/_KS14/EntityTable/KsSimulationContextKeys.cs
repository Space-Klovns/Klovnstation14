using Content.Shared.EntityTable;

namespace Content.Shared._KS14.EntityTable;

/// <summary>
///     <see cref="EntityTableContext"/> keys that let a simulated round stand in for the live state that the gamerule
///         table conditions read: the player count, the round duration and the rules run so far. A real round
///         never sets them, so the conditions fall back to the live state.
/// </summary>
public static class KsSimulationContextKeys
{
    /// <summary>
    ///     An <see cref="int"/> read by <see cref="Content.Shared.EntityTable.Conditions.PlayerCountCondition"/>.
    /// </summary>
    public const string PlayerCount = "KsSimulatedPlayerCount";

    /// <summary>
    ///     A <see cref="System.TimeSpan"/> read by <see cref="Content.Shared.EntityTable.Conditions.RoundDurationCondition"/>.
    /// </summary>
    public const string RoundDuration = "KsSimulatedRoundDuration";

    /// <summary>
    ///     An <see cref="System.Collections.Generic.IReadOnlyList{T}"/> of <c>(TimeSpan, string)</c>, in the shape of
    ///         <see cref="Content.Shared.GameTicking.SharedGameTicker.AllPreviousGameRules"/>, read by
    ///         <see cref="Content.Shared.EntityTable.Conditions.MaxRuleOccurenceCondition"/>.
    /// </summary>
    public const string PreviousGameRules = "KsSimulatedPreviousGameRules";
}
