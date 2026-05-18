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

    public PlayTtsEvent(NetEntity source, byte[] data)
    {
        Source = source;
        Data = data;
    }
}
