namespace Content.Client._KS14.Voice;

/// <summary>
///     What this client knows about one talker: their audio in flight, and whether the local player muted them.
///         Client-only, added to the talker's entity when their voice first arrives or when they are muted.
/// </summary>
[RegisterComponent, Access(typeof(KsVoicePlaybackSystem))]
public sealed partial class KsVoicePlaybackComponent : Component
{
    /// <summary>
    ///     The local player muted this talker (the "Mute voice" verb). Kept for as long as the entity is known to this
    ///         client, which covers going out of view but not a round restart.
    /// </summary>
    [ViewVariables]
    public bool LocallyMuted;

    /// <summary>
    ///     Jitter buffer and playing chunks while the talker is heard; null between utterances.
    /// </summary>
    internal KsVoicePlaybackSystem.KsVoiceSpeaker? Speaker;
}
