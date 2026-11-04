namespace Content.Server._KS14.NPC.Recovery;

/// <summary>
///     Lets an NPC that has just used its meds wait for them to work. A medipen is injected at once but heals over time,
///         so for a while afterwards it stays where it retreated to and only defends itself. See
///         <see cref="NpcRecoverySystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcRecoverySystem))]
public sealed partial class NpcRecoveryComponent : Component
{
    /// <summary>
    ///     Back to normal once it has this share of its health left, from 0 to 1, measured against its critical
    ///         threshold.
    /// </summary>
    [DataField]
    public float RecoveredFraction = 0.7f;

    /// <summary>
    ///     Back to normal after this long, however hurt it still is.
    /// </summary>
    [DataField]
    public TimeSpan MaxDuration = TimeSpan.FromSeconds(25);
}
