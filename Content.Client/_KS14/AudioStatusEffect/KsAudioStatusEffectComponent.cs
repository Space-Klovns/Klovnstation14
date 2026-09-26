using Robust.Shared.Audio;

namespace Content.Client._KS14.AudioStatusEffect;

/// <summary>
///     Put on a status effect entity. While the effect is active on the local player, <see cref="Sound"/> plays
///         globally for them, and is stopped once the effect ends.
/// </summary>
/// <remarks>
///     The sound plays once per effect entity. Set <c>loop: true</c> in its params for it to last as long as the
///         effect does.
/// </remarks>
[RegisterComponent]
public sealed partial class KsAudioStatusEffectComponent : Component
{
    /// <summary>
    ///     Sound played while the effect is active. Every effect entity plays its own.
    /// </summary>
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    /// <summary>
    ///     If true, the sound's gain is scaled every frame by the ratio of this effect's duration still left, fading
    ///         it out to silence as the effect expires. An effect with no end time plays at full volume.
    /// </summary>
    [DataField]
    public bool FadeWithTimeLeft;
}
