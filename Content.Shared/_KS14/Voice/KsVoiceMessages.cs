using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Voice;

/// <summary>
///     Server to client: one chunk of a talker's voice, encoded with <see cref="Codec"/>. Sent unreliably, since
///         audio that arrives late is useless; <see cref="Sequence"/> lets the client reorder and detect gaps.
/// </summary>
public sealed class KsVoiceFrameMessage : NetMessage
{
    /// <summary>
    ///     Largest payload accepted: an ADPCM chunk of <see cref="KsVoiceConstants.MaxChunkSamples"/>, which is also
    ///         the cap the Opus encoder is given.
    /// </summary>
    public static readonly int MaxPayloadBytes = KsVoiceAdpcm.EncodedSize(KsVoiceConstants.MaxChunkSamples);

    public override MsgGroups MsgGroup => MsgGroups.Command;

    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.Unreliable;

    public NetEntity Source;
    public ushort Sequence;
    public KsVoiceCodec Codec;

    /// <summary>
    ///     The first packet of a fresh encoder: listeners decode it, and what follows, with a fresh decoder.
    /// </summary>
    public bool StreamStart;

    public byte[] Payload = [];

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Source = buffer.ReadNetEntity();
        Sequence = buffer.ReadUInt16();
        Codec = (KsVoiceCodec)buffer.ReadByte();
        StreamStart = buffer.ReadBoolean();

        var length = (int)buffer.ReadUInt16();
        if (length > MaxPayloadBytes)
            throw new InvalidOperationException($"Voice payload of {length} bytes exceeds {MaxPayloadBytes}.");

        Payload = new byte[length];
        buffer.ReadBytes(Payload, 0, length);
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Source);
        buffer.Write(Sequence);
        buffer.Write((byte)Codec);
        buffer.Write(StreamStart);
        buffer.Write((ushort)Payload.Length);
        buffer.Write(Payload);
    }

    public override int EstimateBufferSize()
        => 10 + Payload.Length;
}

/// <summary>
///     Client to server: the push-to-talk key was pressed or released.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsVoicePushToTalkEvent(bool held) : EntityEventArgs
{
    public readonly bool Held = held;
}

/// <summary>
///     Client to server: send me my voice link, optionally invalidating the old one first.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsVoiceRequestLinkEvent(bool reset) : EntityEventArgs
{
    public readonly bool Reset = reset;
}

/// <summary>
///     Server to client: this session's personal voice link, or null with an error when none can be issued.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsVoiceLinkEvent(string? url, string? errorLocId) : EntityEventArgs
{
    public readonly string? Url = url;
    public readonly string? ErrorLocId = errorLocId;
}

/// <summary>
///     Server to client: whether this session currently has a microphone page connected.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsVoiceUplinkStatusEvent(bool connected) : EntityEventArgs
{
    public readonly bool Connected = connected;
}

/// <summary>
///     One relayed voice chunk, as recorded into replays: the same data as <see cref="KsVoiceFrameMessage"/>, as an
///         event, because replays only carry events. Never sent over the network; a replay plays it back as if it
///         had been.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsVoiceReplayFrameEvent(NetEntity source, ushort sequence, KsVoiceCodec codec, bool streamStart, byte[] payload)
    : EntityEventArgs
{
    public readonly NetEntity Source = source;
    public readonly ushort Sequence = sequence;
    public readonly KsVoiceCodec Codec = codec;
    public readonly bool StreamStart = streamStart;
    public readonly byte[] Payload = payload;
}
