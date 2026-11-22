namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Keeps RCDs from being used while a wave is on the field, for a commander that asks for it (see
///         <see cref="KsFieldCommanderRuleComponent.BlockRcdDuringWaves"/>). The block itself is the map's, networked,
///         so each client predicts the refusal.
/// </summary>
public sealed partial class KsFieldCommanderRuleSystem
{
    #region RCDs

    /// <summary>
    ///     Turns <see cref="KsFieldCommanderRuleComponent.BlockRcdDuringWaves"/> on or off, at once.
    /// </summary>
    public void SetBlockRcdDuringWaves(Entity<KsFieldCommanderRuleComponent> rule, bool block)
    {
        rule.Comp.BlockRcdDuringWaves = block;
        RefreshRcdBlock(rule);
    }

    /// <summary>
    ///     Blocks or allows RCDs on the battlefield's map by whether a wave is on the field: called whenever a unit is
    ///         sent or killed, and with the orders, which catches a unit that left some other way and the switch being
    ///         changed in VV.
    /// </summary>
    private void RefreshRcdBlock(Entity<KsFieldCommanderRuleComponent> rule)
    {
        if (rule.Comp.WaveStatusMapUid is not { } mapUid || TerminatingOrDeleted(mapUid))
            return;

        var blocked = rule.Comp.BlockRcdDuringWaves && CountLivingUnits(rule.Owner, null) > 0;
        _noBuildZoneSystem.SetBlocked(mapUid, blocked ? rule.Comp.RcdBlockedMessage : (LocId?)null);
    }

    /// <summary>
    ///     A unit was just sent, so a wave is on the field: the same as <see cref="RefreshRcdBlock"/> without counting
    ///         every unit again for each one of a wave.
    /// </summary>
    private void BlockRcdForSentUnit(KsFieldCommanderRuleComponent component)
    {
        if (component.BlockRcdDuringWaves && component.WaveStatusMapUid is { } mapUid && !TerminatingOrDeleted(mapUid))
            _noBuildZoneSystem.SetBlocked(mapUid, component.RcdBlockedMessage);
    }

    private void ClearRcdBlock(KsFieldCommanderRuleComponent component)
    {
        if (component.WaveStatusMapUid is { } mapUid && !TerminatingOrDeleted(mapUid))
            _noBuildZoneSystem.SetBlocked(mapUid, null);
    }

    #endregion
}
