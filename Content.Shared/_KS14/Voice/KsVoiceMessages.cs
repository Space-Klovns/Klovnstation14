using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Voice;

/// <summary>
///     Server to client: one chunk of a talker's voice, ADPCM-encoded (see <see cref="KsVoiceAdpcm"/>).
///         Sent unreliably, since audio that arrives late is useless; <see cref="Sequence"/> lets the client
///         reorder and detect gaps.
/// </summary>
public sealed class KsVoiceFrameMessage : NetMessage
{
    /// <summary>
    ///     Largest ADPCM payload accepted, matching <see cref="KsVoiceConstants.MaxChunkSamples"/>.
    /// </summary>
    public static readonly int MaxPayloadBytes = KsVoiceAdpcm.EncodedSize(KsVoiceConstants.MaxChunkSamples);

    public override MsgGroups MsgGroup => MsgGroups.Command;

    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.Unreliable;

    public NetEntity Source;
    public ushort Sequence;
    public byte[] Payload = [];

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Source = buffer.ReadNetEntity();
        Sequence = buffer.ReadUInt16();

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
        buffer.Write((ushort)Payload.Length);
        buffer.Write(Payload);
    }

    public override int EstimateBufferSize()
        => 8 + Payload.Length;
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
