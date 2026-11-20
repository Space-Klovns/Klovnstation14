namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Waves written out in advance, by an admin or the rule's own YAML, that are sent instead of what the budget
///         would buy.
/// </summary>
public sealed partial class KsFieldCommanderRuleComponent
{
    /// <summary>
    ///     Sent in order, one in place of each wave the budget would have bought, until none is left. A queued wave
    ///         costs nothing, buys no guaranteed assets and follows no doctrine. Read-only in VV: the rule system's
    ///         <see cref="KsFieldCommanderRuleSystem.TryQueueWave"/> checks what goes in.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public List<KsFieldCommanderQueuedWave> QueuedWaves = [];
}

/// <summary>
///     A wave written out in advance.
/// </summary>
[DataDefinition]
public sealed partial class KsFieldCommanderQueuedWave
{
    [DataField]
    public List<KsFieldCommanderQueuedPurchase> Purchases = [];

    /// <summary>
    ///     The commander's taunt with the wave, in place of a random one, sent exactly as written: admins type it,
    ///         so it is not a locale id and is not translated.
    /// </summary>
    [DataField]
    public string? Announcement;
}

[DataDefinition]
public sealed partial class KsFieldCommanderQueuedPurchase
{
    /// <summary>
    ///     Index into <see cref="KsFieldCommanderRuleComponent.Assets"/>.
    /// </summary>
    [DataField(required: true)]
    public int Asset;

    /// <summary>
    ///     How many purchases of it: each brings the asset's <see cref="KsFieldCommanderAsset.Count"/>.
    /// </summary>
    [DataField]
    public int Count = 1;
}
