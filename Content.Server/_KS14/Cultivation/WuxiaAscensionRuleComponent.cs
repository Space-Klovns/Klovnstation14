namespace Content.Server._KS14.Cultivation;

/// <summary>
///     The Wuxia: Ascension gamemode. Everyone aboard may take up cultivation and they race for the peak; the first to
///         reach Nascent Soul or a Primordial Chaos Body is the Supreme Sovereign, free to hunt the rest until the shuttle
///         comes.
/// </summary>
[RegisterComponent]
public sealed partial class WuxiaAscensionRuleComponent : Component
{
    [DataField]
    public EntityUid? Winner;

    [DataField]
    public string? WinnerName;

    /// <summary>
    ///     How long after someone reaches the peak the round ends.
    /// </summary>
    [DataField]
    public TimeSpan RoundEndDelay = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     Extra cultivation speed for everyone, added to the insight and tempering multipliers.
    /// </summary>
    [DataField]
    public float SpeedBonus = 0.5f;
}

/// <summary>
///     The one who reached the peak first. Blows land on them at three-quarter strength.
/// </summary>
[RegisterComponent]
public sealed partial class SupremeSovereignComponent : Component
{
    [DataField]
    public float DamageMultiplier = 0.75f;
}

/// <summary>
///     The scroll everyone holds at the start of an Ascension round: choose the path of qi or the path of the body.
/// </summary>
[RegisterComponent]
public sealed partial class PathChoiceComponent : Component;

/// <summary>
///     Marks the mind role of someone racing for the peak in Wuxia: Ascension.
/// </summary>
[RegisterComponent]
public sealed partial class AscensionContenderRoleComponent : Component;

/// <summary>
///     Where heaven will receive an Ascension contender for their last step to the peak: a named place on the station,
///         chosen the first time they try.
/// </summary>
[RegisterComponent]
public sealed partial class AscensionSiteComponent : Component
{
    [ViewVariables]
    public EntityUid? Beacon;

    [ViewVariables]
    public string Name = string.Empty;
}
