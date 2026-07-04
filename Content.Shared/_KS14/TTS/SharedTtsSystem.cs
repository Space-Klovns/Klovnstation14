using Robust.Shared.Serialization;

namespace Content.Shared._KS14.TTS;

/// <summary>
///     This is how my voice sounds like.
/// </summary>
public abstract class SharedTtsSystem : EntitySystem;

[Serializable, NetSerializable]
public sealed class PlayTtsEvent : EntityEventArgs
{
    public NetEntity Source;
    public byte[] Data;
    public TtsFilteredCategory FilteredCategory = TtsFilteredCategory.DontProcess;

    public PlayTtsEvent(NetEntity source, byte[] data, TtsFilteredCategory category)
    {
        Source = source;
        Data = data;
        FilteredCategory = category;
    }
}

/// <summary>
///     Basically how this works is:
///         If a message is not filtered for any slurs:
///             It gets DontProcess, which means that
///             it will always be played for all clients.
///
///         If a message is filtered for slurs:
///             There will be two TTS-es processed, one filtered
///             (that gets Filtered category) and one unfiltered
///             (that gets WaitForFiltered).
///
///             Clients with slur filter on play the one that was filtered
///             and ignore the one with WaitForFiltered, and vice versa.
/// </summary>
[Serializable, NetSerializable]
public enum TtsFilteredCategory : byte
{
    /// <summary>
    ///     Play this TTS regardless of slur-filter setting;
    ///         this means there was nothing filtered in this tts.
    /// </summary>
    DontProcess,

    /// <summary>
    ///     If slur-filter is on: don't play this TTS, wait for a filtered
    ///         one to come through.
    ///
    ///     Otherwise if slur-filter is off, play this and don't wait for a filtered one.
    ///         Basically, means this tts contains slurs.
    /// </summary>
    WaitForFiltered,

    /// <summary>
    ///     If slur-filter is on: play this. Otherwise, don't play this and instead play one with WaitForFiltered.
    ///         Basically, means this tts is filtered of slurs.
    /// </summary>
    Filtered,
}
