using Robust.Shared.Timing;

namespace Content.Server._KS14.Alligators;

/// <summary>
///     Stores the timing and target of an alligator's leg latch and death roll.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(KsAlligatorSystem))]
public sealed partial class KsAlligatorComponent : Component
{
    /// <summary>
    ///     How long the alligator must hold the victim before it can sever the leg.
    /// </summary>
    [DataField]
    public TimeSpan RollDuration = TimeSpan.FromSeconds(4);

    /// <summary>
    ///     Time between the scheduled end of a roll and the next allowed latch.
    ///     Interrupting a roll does not shorten this lockout.
    /// </summary>
    [DataField]
    public TimeSpan RollRecovery = TimeSpan.FromSeconds(1.5);

    /// <summary>
    ///     How long a bite keeps a fatigued standing target down, giving the alligator time to latch.
    /// </summary>
    [DataField]
    public TimeSpan BiteKnockdown = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     The target selected for the latch. Set before the buckle attempt and cleared if it fails.
    /// </summary>
    public EntityUid? Victim;

    /// <summary>
    ///     The specific leg grabbed at the start of the roll. Losing it early cancels the roll.
    /// </summary>
    public EntityUid? Leg;

    /// <summary>
    ///     The earliest game time at which the current roll can sever the leg.
    /// </summary>
    [AutoPausedField]
    public TimeSpan RollEndsAt;

    /// <summary>
    ///     The earliest game time at which the alligator can latch again.
    /// </summary>
    [AutoPausedField]
    public TimeSpan NextRollAt;
}
