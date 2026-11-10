namespace Content.Client._KS14.AudioPitchStatusEffect;

/// <summary>
///     Put on an audio entity by <see cref="KsAudioPitchStatusEffectSystem"/> while its pitch is being warped, holding
///         that sound's own warp state. Removing it puts the sound back to its normal pitch.
/// </summary>
[RegisterComponent]
[Access(typeof(KsAudioPitchStatusEffectSystem))]
public sealed partial class KsPitchWarpedAudioComponent : Component
{
    /// <summary>
    ///     Offset into the drift noise, so that every sound drifts out of step with the others.
    /// </summary>
    [ViewVariables]
    public float Phase;

    /// <summary>
    ///     Where in the distortion range, from -1 to 1, the current lurch holds this sound. Only meaningful before
    ///         <see cref="LurchEndTime"/>.
    /// </summary>
    [ViewVariables]
    public float LurchValue;

    /// <summary>
    ///     When the current lurch ends, in real time. In the past when the sound is drifting.
    /// </summary>
    [ViewVariables]
    public TimeSpan LurchEndTime;
}
