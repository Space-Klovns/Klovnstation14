using System.Buffers.Binary;
using System.Text;
using Content.Klovn.Concentus;
using Content.Klovn.Concentus.Enums;
using Content.Klovn.Concentus.Structs;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.TTS;

/// <summary>
///     What a TTS clip is, so the client knows how to decode it. The server works this out from the clip itself
///         (<see cref="KsTtsOpus.Identify"/>) rather than from its own settings, so a clip is always labelled for what
///         the endpoint actually sent.
/// </summary>
[Serializable, NetSerializable]
public enum TtsCodec : byte
{
    /// <summary>
    ///     Ogg Vorbis, decoded by <c>IAudioManager.LoadAudioOggVorbis</c>. That has to run on the game thread.
    /// </summary>
    Vorbis = 0,

    /// <summary>
    ///     Ogg Opus (RFC 7845), which the engine can't play. <see cref="KsTtsOpus"/> decodes it in content through the
    ///         vendored Concentus port, off the game thread, and the engine only gets the samples.
    /// </summary>
    Opus = 1,
}

/// <summary>
///     Reads and writes Ogg Opus files (RFC 7845) with the vendored Concentus port (<c>Content.Klovn.Concentus</c>):
///         clients decode what the endpoint or the server's transcoder produced, and the transcoder encodes. Everything
///         here is pure content code and holds no state, so it is safe on any thread, which is the point: clients
///         decode on the thread pool.
/// </summary>
public static class KsTtsOpus
{
    /// <summary>
    ///     Ogg Opus timestamps are always at 48 kHz, whatever the encoder was fed, so that is what we decode at.
    /// </summary>
    public const int SampleRate = 48000;

    /// <summary>
    ///     Longest clip the decoder will produce. Well past anything the TTS endpoint is asked for, and there so a bad
    ///         file can't make a client allocate without bound.
    /// </summary>
    public const int MaxDurationSeconds = 60;

    /// <summary>
    ///     The longest a single Opus packet can decode to: 120 ms (RFC 6716 §3.2.5).
    /// </summary>
    private const int MaxPacketSamples = SampleRate * 120 / 1000;

    public const int MinBitrate = 6000;
    public const int MaxBitrate = 128000;

    /// <summary>
    ///     The largest packet Opus can produce (RFC 6716 §3.2.1).
    /// </summary>
    private const int MaxPacketBytes = 1275;

    /// <summary>
    ///     20 ms packets: the Opus default, and the size its speech tuning assumes.
    /// </summary>
    private const int PacketMilliseconds = 20;

    /// <summary>
    ///     Half a second of audio per Ogg page. A page costs 27 bytes plus its lacing; fewer, fuller pages waste less.
    /// </summary>
    private const int PacketsPerPage = 25;

    private const int PageHeaderBytes = 27;

    private static readonly int[] EncoderRates = [8000, 12000, 16000, 24000, 48000];

    // Arrays, not u8 literals: a u8 literal compiles to a pointer into the assembly's static data, which ILVerify
    //      rejects when the client loads Content.Shared (only SandboxTest notices).
    private static readonly byte[] CapturePattern = Encoding.ASCII.GetBytes("OggS");
    private static readonly byte[] OpusHeadMagic = Encoding.ASCII.GetBytes("OpusHead");
    private static readonly byte[] OpusTagsMagic = Encoding.ASCII.GetBytes("OpusTags");
    private static readonly byte[] VorbisHeadMagic = Encoding.ASCII.GetBytes("\u0001vorbis");
    private static readonly byte[] Vendor = Encoding.ASCII.GetBytes("Klovnstation 14 (Concentus)");

    /// <summary>
    ///     What an Ogg file carries, from its first packet. Null for anything that isn't Ogg Opus or Ogg Vorbis.
    /// </summary>
    public static TtsCodec? Identify(ReadOnlySpan<byte> data)
    {
        if (data.Length < PageHeaderBytes || !data.StartsWith(CapturePattern))
            return null;

        var segmentCount = data[26];
        var payload = data[Math.Min(data.Length, PageHeaderBytes + segmentCount)..];

        if (payload.StartsWith(OpusHeadMagic))
            return TtsCodec.Opus;

        if (payload.StartsWith(VorbisHeadMagic))
            return TtsCodec.Vorbis;

        return null;
    }

    /// <summary>
    ///     Decodes an Ogg Opus file to mono 16-bit samples at <see cref="SampleRate"/>, mixing stereo down: TTS is
    ///         played at the speaker, and OpenAL only positions mono sources. Returns false, rather than throwing, for
    ///         anything malformed.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> data, out short[] samples)
    {
        samples = [];

        var packets = new List<byte[]>();
        if (!TryDemultiplex(data, packets, out var lastGranule) || packets.Count < 2)
            return false;

        // Identification header (RFC 7845 §5.1).
        var head = packets[0];
        if (head.Length < 19 || !head.AsSpan().StartsWith(OpusHeadMagic) || head[8] >> 4 != 0)
            return false;

        var channels = (int)head[9];
        var preSkip = (int)BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(10));
        var outputGain = BinaryPrimitives.ReadInt16LittleEndian(head.AsSpan(16));
        var mappingFamily = head[18];

        // Family 0 is plain mono or stereo; the others are multichannel layouts no TTS voice produces.
        if (mappingFamily != 0 || channels is < 1 or > 2)
            return false;

        // The second packet is the comment header, which nothing here needs.

        // The final page's granule position is where the stream ends, counted from before the pre-skip; anything
        //      decoded past it is padding the encoder added to fill its last packet.
        var maxSamples = SampleRate * MaxDurationSeconds;
        var endSample = lastGranule >= 0 ? (int)Math.Min(lastGranule, maxSamples + preSkip) : maxSamples + preSkip;
        if (endSample <= preSkip)
            return false;

#pragma warning disable CS0618 // see KsVoiceOpusEncoder: the factory upstream prefers exists to load native libopus
        var decoder = new OpusDecoder(SampleRate, channels);
#pragma warning restore CS0618

        var decoded = new short[endSample];
        var frame = new short[MaxPacketSamples * channels];
        var written = 0;

        try
        {
            for (var i = 2; i < packets.Count && written < decoded.Length; i++)
            {
                var count = decoder.Decode(packets[i], frame, MaxPacketSamples);
                var take = Math.Min(count, decoded.Length - written);

                if (channels == 1)
                {
                    frame.AsSpan(0, take).CopyTo(decoded.AsSpan(written));
                }
                else
                {
                    for (var sample = 0; sample < take; sample++)
                        decoded[written + sample] = (short)((frame[sample * 2] + frame[sample * 2 + 1]) / 2);
                }

                written += take;
            }
        }
        catch (Exception exception) when (exception is OpusException or IndexOutOfRangeException or ArgumentException)
        {
            return false;
        }

        if (written <= preSkip)
            return false;

        samples = decoded.AsSpan(preSkip, written - preSkip).ToArray();

        // Q7.8 dB, to be applied on top of the decoded audio (RFC 7845 §5.1). Almost always 0.
        if (outputGain != 0)
        {
            var gain = MathF.Pow(10f, outputGain / (20f * 256f));
            for (var sample = 0; sample < samples.Length; sample++)
                samples[sample] = (short)Math.Clamp((int)MathF.Round(samples[sample] * gain), short.MinValue, short.MaxValue);
        }

        return true;
    }

    public static int ClampBitrate(int bitrate)
        => Math.Clamp(bitrate, MinBitrate, MaxBitrate);

    public static int ClampComplexity(int complexity)
        => Math.Clamp(complexity, 0, 10);

    /// <summary>
    ///     The rate to encode a clip recorded at <paramref name="sourceRate"/>: the lowest Opus supports that loses
    ///         nothing, so a 22.05 kHz voice is encoded at 24 kHz and a 16 kHz one stays at 16 kHz.
    /// </summary>
    public static int EncodingRate(int sourceRate)
    {
        foreach (var rate in EncoderRates)
        {
            if (rate >= sourceRate)
                return rate;
        }

        return EncoderRates[^1];
    }

    /// <summary>
    ///     Encodes mono samples into an Ogg Opus file. <paramref name="sampleRate"/> must be one Opus encodes at
    ///         (<see cref="EncodingRate"/> picks one; resampling to it is the caller's job).
    /// </summary>
    public static byte[] Encode(ReadOnlySpan<short> samples, int sampleRate, int bitrate, int complexity)
    {
        if (Array.IndexOf(EncoderRates, sampleRate) < 0)
            throw new ArgumentException($"Opus can't encode at {sampleRate} Hz", nameof(sampleRate));

        if (samples.Length > sampleRate * MaxDurationSeconds)
            throw new ArgumentException($"Clip is longer than {MaxDurationSeconds} s", nameof(samples));

#pragma warning disable CS0618 // see TryDecode
        var encoder = new OpusEncoder(sampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP)
        {
            Bitrate = ClampBitrate(bitrate),
            Complexity = ClampComplexity(complexity),
            SignalType = OpusSignal.OPUS_SIGNAL_VOICE,
        };
#pragma warning restore CS0618

        // Granule positions and the pre-skip are counted at 48 kHz whatever the encoder ran at (RFC 7845 §4).
        var granuleScale = SampleRate / sampleRate;
        var packetSamples = sampleRate * PacketMilliseconds / 1000;
        var lookahead = encoder.Lookahead;

        // The decoder lags the input by the lookahead, so the input is padded by as much again: without it the last
        //      few milliseconds would still be inside the encoder when the packets run out.
        var packetCount = (samples.Length + lookahead + packetSamples - 1) / packetSamples;
        var input = new short[packetCount * packetSamples];
        samples.CopyTo(input);

        var writer = new OggWriter();

        var head = new byte[19];
        OpusHeadMagic.CopyTo(head);
        head[8] = 1; // version
        head[9] = 1; // channels
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(10), (ushort)(lookahead * granuleScale));
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(12), (uint)sampleRate);
        // Output gain 0 and mapping family 0 are already zero.
        // Not [head]: a collection expression into a List compiles to CollectionsMarshal, which the sandbox refuses.
        writer.WritePage(new List<byte[]> { head }, granule: 0, beginning: true, end: false);

        var tags = new byte[8 + 4 + Vendor.Length + 4];
        OpusTagsMagic.CopyTo(tags, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(tags.AsSpan(8), (uint)Vendor.Length);
        Vendor.CopyTo(tags, 12);
        writer.WritePage(new List<byte[]> { tags }, granule: 0, beginning: false, end: false);

        // The end of the stream, from before the pre-skip, so a decoder can trim the padding back off.
        var endGranule = (long)(lookahead + samples.Length) * granuleScale;

        var page = new List<byte[]>();
        var packet = new byte[MaxPacketBytes];
        for (var i = 0; i < packetCount; i++)
        {
            var length = encoder.Encode(input.AsSpan(i * packetSamples, packetSamples), packetSamples, packet, packet.Length);
            page.Add(packet.AsSpan(0, length).ToArray());

            var last = i == packetCount - 1;
            if (page.Count < PacketsPerPage && !last)
                continue;

            var granule = last ? endGranule : (long)(i + 1) * packetSamples * granuleScale;
            writer.WritePage(page, granule, beginning: false, end: last);
            page.Clear();
        }

        return writer.ToArray();
    }

    /// <summary>
    ///     Splits an Ogg file into the packets of its first logical stream, joining packets that span pages.
    /// </summary>
    /// <param name="lastGranule">The granule position of the stream's last page, or -1 if none was set.</param>
    private static bool TryDemultiplex(ReadOnlySpan<byte> data, List<byte[]> packets, out long lastGranule)
    {
        lastGranule = -1;
        uint? streamSerial = null;
        var pending = new List<byte>();
        var offset = 0;

        while (offset < data.Length)
        {
            var page = data[offset..];
            if (page.Length < PageHeaderBytes || !page.StartsWith(CapturePattern) || page[4] != 0)
                return false;

            var granule = BinaryPrimitives.ReadInt64LittleEndian(page[6..]);
            var serial = BinaryPrimitives.ReadUInt32LittleEndian(page[14..]);
            var segmentCount = page[26];
            var lacing = page.Slice(PageHeaderBytes, Math.Min(segmentCount, page.Length - PageHeaderBytes));
            if (lacing.Length < segmentCount)
                return false;

            var bodyLength = 0;
            foreach (var segment in lacing)
                bodyLength += segment;

            var bodyStart = PageHeaderBytes + segmentCount;
            if (page.Length < bodyStart + bodyLength)
                return false;

            offset += bodyStart + bodyLength;

            // Only the first stream: a TTS clip has one, and a chained or multiplexed file is not something to guess at.
            streamSerial ??= serial;
            if (serial != streamSerial)
                continue;

            var body = page.Slice(bodyStart, bodyLength);
            var position = 0;
            foreach (var segment in lacing)
            {
                for (var i = 0; i < segment; i++)
                    pending.Add(body[position + i]);

                position += segment;

                // A lacing value under 255 ends a packet; 255 means it carries on into the next segment or page.
                if (segment < 255)
                {
                    packets.Add(pending.ToArray());
                    pending.Clear();
                }
            }

            // -1 marks a page on which no packet finishes.
            if (granule != -1)
                lastGranule = granule;
        }

        return true;
    }

    /// <summary>
    ///     Builds an Ogg file of a single logical stream, page by page (RFC 3533).
    /// </summary>
    private sealed class OggWriter
    {
        private const uint Serial = 0x4B533134; // "KS14"

        private static readonly uint[] CrcTable = BuildCrcTable();

        private readonly List<byte> _output = new();
        private uint _sequence;

        /// <summary>
        ///     Writes <paramref name="packets"/> as one page. Each must fit on it whole: at most 255 lacing values
        ///         between them, which <see cref="PacketsPerPage"/> packets of at most <see cref="MaxPacketBytes"/> do.
        /// </summary>
        public void WritePage(List<byte[]> packets, long granule, bool beginning, bool end)
        {
            var lacing = new List<byte>();
            var bodyLength = 0;
            foreach (var packet in packets)
            {
                // A packet is laced as runs of 255 closed by a value under 255, which is 0 for an exact multiple.
                for (var remaining = packet.Length; ; remaining -= 255)
                {
                    lacing.Add((byte)Math.Min(remaining, 255));
                    if (remaining < 255)
                        break;
                }

                bodyLength += packet.Length;
            }

            if (lacing.Count > 255)
                throw new InvalidOperationException("Too many packets for one Ogg page");

            var page = new byte[PageHeaderBytes + lacing.Count + bodyLength];
            CapturePattern.CopyTo(page);
            page[5] = (byte)((beginning ? 0x02 : 0) | (end ? 0x04 : 0));
            BinaryPrimitives.WriteInt64LittleEndian(page.AsSpan(6), granule);
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(14), Serial);
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(18), _sequence++);
            page[26] = (byte)lacing.Count;
            lacing.CopyTo(page, PageHeaderBytes);

            var position = PageHeaderBytes + lacing.Count;
            foreach (var packet in packets)
            {
                packet.CopyTo(page, position);
                position += packet.Length;
            }

            // Computed over the whole page with its own field still zero.
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(22), Crc(page));
            _output.AddRange(page);
        }

        public byte[] ToArray()
            => _output.ToArray();

        /// <summary>
        ///     Ogg's CRC-32: polynomial 0x04C11DB7, not reflected, no initial or final XOR.
        /// </summary>
        private static uint Crc(ReadOnlySpan<byte> data)
        {
            var crc = 0u;
            foreach (var value in data)
                crc = (crc << 8) ^ CrcTable[(byte)(crc >> 24) ^ value];

            return crc;
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (var i = 0u; i < 256; i++)
            {
                var entry = i << 24;
                for (var bit = 0; bit < 8; bit++)
                    entry = (entry & 0x80000000) != 0 ? (entry << 1) ^ 0x04C11DB7 : entry << 1;

                table[i] = entry;
            }

            return table;
        }
    }
}
