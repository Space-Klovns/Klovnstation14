using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.TTS;

/// <summary>
///     Where the server is with baking voice previews.
/// </summary>
public enum KsTtsPreviewStatus : byte
{
    /// <summary>
    ///     TTS hasn't been on since the server started, so nothing has been baked.
    /// </summary>
    Unavailable = 0,

    /// <summary>
    ///     Previews are being requested from the TTS endpoint. Each arrives as soon as it is baked.
    /// </summary>
    Baking = 1,

    /// <summary>
    ///     Every selectable voice has a preview.
    /// </summary>
    Ready = 2,

    /// <summary>
    ///     The endpoint failed for some voices. The server retries them after a while.
    /// </summary>
    Failed = 3,
}

/// <summary>
///     One voice's preview: the fixed preview line (<c>tts-preview-text</c>) said in that voice, exactly as a TTS clip
///         would arrive in game.
/// </summary>
public readonly record struct KsTtsPreview(ProtoId<TtsVoicePrototype> Voice, TtsCodec Codec, byte[] Data);

/// <summary>
///     Server to client: voice previews, and where baking stands. A client gets every preview baked so far when it
///         connects, then each new one as it is baked. A prototype reload that invalidates previews sends the full set
///         again, with <see cref="Replace"/>.
/// </summary>
public sealed class KsTtsPreviewMessage : NetMessage
{
    /// <summary>
    ///     A clip is a few seconds of speech, tens of kilobytes. This is far past that, and only bounds what a bad
    ///         message can make a client allocate.
    /// </summary>
    public const int MaxPreviewBytes = 1024 * 1024;

    public override MsgGroups MsgGroup => MsgGroups.Command;

    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;

    /// <summary>
    ///     A sequence channel of their own. Everything reliable and ordered queues behind whatever went before it on
    ///         its channel, and a connecting client's previews add up to hundreds of kilobytes: on the default channel
    ///         they would hold up the messages that actually get the player into the game.
    /// </summary>
    public override int SequenceChannel => 2;

    public KsTtsPreviewStatus Status;

    /// <summary>
    ///     True if <see cref="Previews"/> is every preview there is, and anything the client holds that isn't in it is
    ///         to be dropped. False if they are additions.
    /// </summary>
    public bool Replace;

    public List<KsTtsPreview> Previews = new();

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Status = (KsTtsPreviewStatus)buffer.ReadByte();
        Replace = buffer.ReadBoolean();

        var count = buffer.ReadVariableInt32();
        Previews = new List<KsTtsPreview>(Math.Clamp(count, 0, 1024));
        for (var i = 0; i < count; i++)
        {
            var voice = buffer.ReadString();
            var codec = (TtsCodec)buffer.ReadByte();
            var length = buffer.ReadVariableInt32();
            if (length < 0 || length > MaxPreviewBytes)
                throw new InvalidOperationException($"Voice preview of {length} bytes exceeds {MaxPreviewBytes}.");

            var data = new byte[length];
            buffer.ReadBytes(data, 0, length);
            Previews.Add(new KsTtsPreview(voice, codec, data));
        }
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write((byte)Status);
        buffer.Write(Replace);
        buffer.WriteVariableInt32(Previews.Count);
        foreach (var preview in Previews)
        {
            buffer.Write(preview.Voice.Id);
            buffer.Write((byte)preview.Codec);
            buffer.WriteVariableInt32(preview.Data.Length);
            buffer.Write(preview.Data);
        }
    }

    public override int EstimateBufferSize()
    {
        var size = 6;
        foreach (var preview in Previews)
            size += preview.Voice.Id.Length * 2 + preview.Data.Length + 8;

        return size;
    }
}
