using Content.Klovn.Concentus;
using Content.Klovn.Concentus.Enums;
using Content.Klovn.Concentus.Structs;

namespace Content.Shared._KS14.Voice;

/// <summary>
///     How a relayed voice chunk is compressed. Carried by every frame, so a talker's stream can change codec (the
///         server's <c>klovn.voice.codec</c> changing) without anything going out of step.
/// </summary>
public enum KsVoiceCodec : byte
{
    /// <summary>
    ///     IMA ADPCM, <see cref="KsVoiceAdpcm"/>: 64 kbps, stateless per packet, audible hiss.
    /// </summary>
    Adpcm = 0,

    /// <summary>
    ///     Opus through the vendored Concentus port (<c>Content.Klovn.Concentus</c>): far cleaner at half the
    ///         bandwidth, but stateful, so it has to be decoded in order and costs more CPU.
    /// </summary>
    Opus = 1,
}

/// <summary>
///     Compresses one talker's stream, chunk by chunk. Encoders keep state between chunks, so each talker needs their
///         own, and chunks must be fed in order.
/// </summary>
public abstract class KsVoiceEncoder
{
    public abstract KsVoiceCodec Codec { get; }

    /// <summary>
    ///     Encodes one chunk of 20, 40 or 60 ms (<see cref="KsVoiceConstants.FrameSamples"/> times one to three).
    /// </summary>
    public abstract byte[] Encode(ReadOnlySpan<short> samples);

    public static KsVoiceEncoder Create(KsVoiceCodec codec, int opusBitrate, int opusComplexity)
    {
        return codec switch
        {
            KsVoiceCodec.Opus => new KsVoiceOpusEncoder(opusBitrate, opusComplexity),
            _ => new KsVoiceAdpcmEncoder(),
        };
    }
}

/// <summary>
///     Decompresses one talker's stream. Packets must be handed over in sequence order; a lost one is reported with
///         <see cref="Conceal"/> rather than skipped, so a stateful codec can paper over the gap.
/// </summary>
public abstract class KsVoiceDecoder
{
    public abstract KsVoiceCodec Codec { get; }

    /// <summary>
    ///     Decodes a packet into <paramref name="destination"/>, returning the samples written, or -1 if the packet is
    ///         malformed (which callers treat as lost).
    /// </summary>
    public abstract int Decode(ReadOnlySpan<byte> packet, Span<short> destination);

    /// <summary>
    ///     Fills in for a lost packet of <paramref name="sampleCount"/> samples. Returns the samples written, which is 0
    ///         for a codec with nothing better than silence to offer.
    /// </summary>
    public abstract int Conceal(int sampleCount, Span<short> destination);

    public static KsVoiceDecoder Create(KsVoiceCodec codec)
    {
        return codec switch
        {
            KsVoiceCodec.Opus => new KsVoiceOpusDecoder(),
            _ => new KsVoiceAdpcmDecoder(),
        };
    }
}

public sealed class KsVoiceAdpcmEncoder : KsVoiceEncoder
{
    private KsVoiceAdpcm.EncoderState _state;

    public override KsVoiceCodec Codec => KsVoiceCodec.Adpcm;

    public override byte[] Encode(ReadOnlySpan<short> samples)
    {
        var payload = new byte[KsVoiceAdpcm.EncodedSize(samples.Length)];
        KsVoiceAdpcm.Encode(ref _state, samples, payload);
        return payload;
    }
}

public sealed class KsVoiceAdpcmDecoder : KsVoiceDecoder
{
    public override KsVoiceCodec Codec => KsVoiceCodec.Adpcm;

    public override int Decode(ReadOnlySpan<byte> packet, Span<short> destination)
        => KsVoiceAdpcm.Decode(packet, destination);

    // Every ADPCM packet carries its own predictor state, so there is nothing to recover: the gap is just skipped.
    public override int Conceal(int sampleCount, Span<short> destination)
        => 0;
}

/// <summary>
///     Opus settings for voice: 16 kHz mono, the VOIP application (SILK, tuned for speech), one packet per chunk.
/// </summary>
public static class KsVoiceOpus
{
    public const int MinBitrate = 6000;
    public const int MaxBitrate = 64000;

    /// <summary>
    ///     Longest gap concealed in one go. Past this, synthesised audio is worse than a short silence.
    /// </summary>
    public const int MaxConcealSamples = KsVoiceConstants.SampleRate * 120 / 1000;

    public static int ClampBitrate(int bitrate)
        => Math.Clamp(bitrate, MinBitrate, MaxBitrate);

    public static int ClampComplexity(int complexity)
        => Math.Clamp(complexity, 0, 10);
}

public sealed class KsVoiceOpusEncoder : KsVoiceEncoder
{
    private readonly OpusEncoder _encoder;
    private readonly byte[] _scratch = new byte[KsVoiceFrameMessage.MaxPayloadBytes];

    public KsVoiceOpusEncoder(int bitrate, int complexity)
    {
        Bitrate = KsVoiceOpus.ClampBitrate(bitrate);
        Complexity = KsVoiceOpus.ClampComplexity(complexity);

        // Obsolete upstream in favour of OpusCodecFactory, which exists to pick native libopus when it can: not
        //      vendored, and not something the sandbox would allow.
#pragma warning disable CS0618
        _encoder = new OpusEncoder(KsVoiceConstants.SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP)
        {
            Bitrate = Bitrate,
            Complexity = Complexity,
            SignalType = OpusSignal.OPUS_SIGNAL_VOICE,
        };
#pragma warning restore CS0618
    }

    public override KsVoiceCodec Codec => KsVoiceCodec.Opus;

    public int Bitrate { get; }

    public int Complexity { get; }

    public override byte[] Encode(ReadOnlySpan<short> samples)
    {
        // Capped at the relay message's payload limit, which the encoder then respects by spending fewer bits.
        var length = _encoder.Encode(samples, samples.Length, _scratch, _scratch.Length);
        return _scratch.AsSpan(0, length).ToArray();
    }
}

public sealed class KsVoiceOpusDecoder : KsVoiceDecoder
{
#pragma warning disable CS0618 // see KsVoiceOpusEncoder
    private readonly OpusDecoder _decoder = new(KsVoiceConstants.SampleRate, 1);
#pragma warning restore CS0618

    public override KsVoiceCodec Codec => KsVoiceCodec.Opus;

    public override int Decode(ReadOnlySpan<byte> packet, Span<short> destination)
    {
        if (packet.IsEmpty)
            return -1;

        // Packets come from our own server, but a decoder fault must never take playback down with it.
        try
        {
            return _decoder.Decode(packet, destination, destination.Length);
        }
        catch (Exception exception) when (exception is OpusException or IndexOutOfRangeException or ArgumentException)
        {
            return -1;
        }
    }

    public override int Conceal(int sampleCount, Span<short> destination)
    {
        // Opus conceals in whole 10 ms steps.
        var tenMs = KsVoiceConstants.SampleRate / 100;
        sampleCount = Math.Min(Math.Min(sampleCount, KsVoiceOpus.MaxConcealSamples), destination.Length) / tenMs * tenMs;
        if (sampleCount <= 0)
            return 0;

        try
        {
            return _decoder.Decode(ReadOnlySpan<byte>.Empty, destination, sampleCount);
        }
        catch (OpusException)
        {
            return 0;
        }
    }
}
