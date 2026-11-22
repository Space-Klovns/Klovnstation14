namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleComponent
{
    #region RCDs

    /// <summary>
    ///     Whether nobody may use an RCD while a wave is on the field, which is for as long as any of the commander's
    ///         units lives. Change it in a running round through
    ///         <see cref="KsFieldCommanderRuleSystem.SetBlockRcdDuringWaves"/>.
    /// </summary>
    [DataField]
    public bool BlockRcdDuringWaves;

    /// <summary>
    ///     What someone trying an RCD is told while <see cref="BlockRcdDuringWaves"/> keeps them from it.
    /// </summary>
    [DataField]
    public LocId RcdBlockedMessage = "ks-fieldcommander-rcd-blocked";

    #endregion
}
