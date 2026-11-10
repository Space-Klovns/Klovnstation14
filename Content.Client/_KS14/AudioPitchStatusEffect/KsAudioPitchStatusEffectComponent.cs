namespace Content.Client._KS14.AudioPitchStatusEffect;

/// <summary>
///     Put on a status effect entity. While the effect is active on the local player, the pitch of every sound playing
///         on their client wanders and lurches between <see cref="MinMultiplier"/> and <see cref="MaxMultiplier"/>
///         times its normal pitch, each sound out of step with the rest.
/// </summary>
/// <remarks>
///     If several of these are active at once, the one with the most of its duration left is used.
/// </remarks>
[RegisterComponent]
public sealed partial class KsAudioPitchStatusEffectComponent : Component
{
    /// <summary>
    ///     The lowest a sound's pitch is multiplied by. Between 0 and 1.
    /// </summary>
    [DataField]
    public float MinMultiplier = 0.35f;

    /// <summary>
    ///     The highest a sound's pitch is multiplied by. 1 or more.
    /// </summary>
    [DataField]
    public float MaxMultiplier = 1.9f;

    /// <summary>
    ///     How fast each sound's pitch drifts, in noise cycles per second.
    /// </summary>
    [DataField]
    public float WanderSpeed = 0.6f;

    /// <summary>
    ///     Average number of times per second that a sound stops drifting and snaps straight to a random pitch instead,
    ///         for somewhere from <see cref="MinLurchDuration"/> to <see cref="MaxLurchDuration"/>.
    /// </summary>
    [DataField]
    public float LurchChance = 0.15f;

    [DataField]
    public TimeSpan MinLurchDuration = TimeSpan.FromSeconds(0.15);

    [DataField]
    public TimeSpan MaxLurchDuration = TimeSpan.FromSeconds(0.6);

    /// <summary>
    ///     If true, the distortion is scaled by the ratio of this effect's duration still left, easing every sound back
    ///         to its normal pitch as the effect expires. An effect with no end time distorts at full strength.
    /// </summary>
    [DataField]
    public bool FadeWithTimeLeft = true;
}
