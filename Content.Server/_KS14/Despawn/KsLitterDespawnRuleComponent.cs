namespace Content.Server._KS14.Despawn;

/// <summary>
///     While this game rule is active, the battlefield cleans itself up: spent casings, loose guns and magazines and
///         the commander's dead units dissolve away after a while (<see cref="KsLitterDespawnSystem"/>).
/// </summary>
[RegisterComponent, Access(typeof(KsLitterDespawnSystem))]
public sealed partial class KsLitterDespawnRuleComponent : Component
{
    /// <summary>
    ///     How long a spent casing lies on the floor before it dissolves.
    /// </summary>
    [DataField]
    public TimeSpan CasingDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     How long a gun or magazine lies loose on the floor, off any table or rack, before it dissolves. Counted from
    ///         when it was dropped.
    /// </summary>
    [DataField]
    public TimeSpan ItemDelay = TimeSpan.FromSeconds(300);

    /// <summary>
    ///     How long a dead Field Commander unit lies there before it dissolves. Player bodies never do.
    /// </summary>
    [DataField]
    public TimeSpan BodyDelay = TimeSpan.FromSeconds(300);

    /// <summary>
    ///     How long the dissolve itself takes, or the dissolve's own default.
    /// </summary>
    [DataField]
    public TimeSpan? DissolveDuration;
}
