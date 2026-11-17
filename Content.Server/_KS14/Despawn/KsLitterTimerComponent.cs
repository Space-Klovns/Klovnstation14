namespace Content.Server._KS14.Despawn;

/// <summary>
///     Something that dissolves when its time comes, unless it is picked up first (or, for a gun, found lying on a
///         table). Added and removed by <see cref="KsLitterDespawnSystem"/> only while a litter rule is active.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(KsLitterDespawnSystem))]
public sealed partial class KsLitterTimerComponent : Component
{
    [DataField, AutoPausedField]
    public TimeSpan DissolveAt;

    /// <summary>
    ///     How long the timer was started for, to start it over for something found on a table.
    /// </summary>
    [DataField]
    public TimeSpan Delay;

    /// <summary>
    ///     What put the timer there, which decides what cancels it.
    /// </summary>
    [DataField]
    public KsLitterKind Kind;

    /// <summary>
    ///     How long the dissolve takes, from the rule that started this timer.
    /// </summary>
    [DataField]
    public TimeSpan? DissolveDuration;
}

public enum KsLitterKind
{
    /// <summary>
    ///     A spent casing: left alone if it has been picked up, or is found lying on a table, and dissolved otherwise.
    /// </summary>
    Casing,

    /// <summary>
    ///     A gun or magazine on the floor: cancelled by picking it up, and put off for as long as it lies on a table.
    /// </summary>
    Item,

    /// <summary>
    ///     A dead unit: cancelled by coming back to life.
    /// </summary>
    Body,
}
