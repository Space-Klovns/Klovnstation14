using System.Buffers.Binary;
using System.IO;
using Content.Shared._KS14.TTS;
using NVorbis;

namespace Content.Server._KS14.TTS;

/// <summary>
///     Re-encodes what the TTS endpoint returned into Ogg Opus, for <c>klovn.tts.codec transcode</c>. Takes Ogg Vorbis
///         or 16-bit PCM WAV, mixes it to mono, resamples it to the nearest rate Opus encodes at, and encodes it with
///         <see cref="KsTtsOpus.Encode"/>. Stateless and thread-safe; <see cref="TtsSystem"/> runs it on the thread pool.
/// </summary>
/// <remarks>
///     This only saves bandwidth, and moves the client's decode off the game thread. It can't put back what a lossy
///         source already threw away, so an endpoint that can serve Opus itself (<c>klovn.tts.codec opus</c>) or WAV is
///         the way to better quality.
/// </remarks>
public static class KsTtsTranscoder
{
    /// <summary>
    ///     Taps either side of each output sample in the resampler's windowed-sinc kernel.
    /// </summary>
    private const int ResamplerHalfWidth = 16;

    public static bool TryTranscode(byte[] data, int bitrate, int complexity, out byte[] opus)
    {
        opus = [];

        if (!TryDecodeToMono(data, out var samples, out var sampleRate))
            return false;

        var encodingRate = KsTtsOpus.EncodingRate(sampleRate);
        var resampled = Resample(samples, sampleRate, encodingRate);

        var pcm = new short[resampled.Length];
        for (var i = 0; i < resampled.Length; i++)
            pcm[i] = (short)Math.Clamp((int)MathF.Round(resampled[i] * short.MaxValue), short.MinValue, short.MaxValue);

        opus = KsTtsOpus.Encode(pcm, encodingRate, bitrate, complexity);
        return true;
    }

    /// <summary>
    ///     Decodes Ogg Vorbis or PCM WAV to mono floats in [-1, 1].
    /// </summary>
    public static bool TryDecodeToMono(byte[] data, out float[] samples, out int sampleRate)
    {
        if (KsTtsOpus.Identify(data) == TtsCodec.Vorbis)
            return TryDecodeVorbis(data, out samples, out sampleRate);

        return TryDecodeWav(data, out samples, out sampleRate);
    }

    private static bool TryDecodeVorbis(byte[] data, out float[] samples, out int sampleRate)
    {
        samples = [];
        sampleRate = 0;

        try
        {
            using var reader = new VorbisReader(new MemoryStream(data), leaveOpen: false);
            reader.Initialize();

            var channels = reader.Channels;
            sampleRate = reader.SampleRate;
            if (channels < 1 || sampleRate <= 0)
                return false;

            var maxFrames = (long)sampleRate * KsTtsOpus.MaxDurationSeconds;
            var mono = new List<float>((int)Math.Clamp(reader.TotalSamples, 0, maxFrames));
            var buffer = new float[4096 * channels];

            while (mono.Count < maxFrames)
            {
                // Never ask for more than the stream reports remaining: VorbisPizza can loop forever on some files
                //      otherwise (see the engine's AudioLoaderOgg, space-station-14#22676).
                var remaining = reader.TotalSamples - reader.SamplePosition;
                if (remaining <= 0)
                    break;

                var want = (int)Math.Min(remaining, buffer.Length / channels) * channels;
                var frames = reader.ReadSamples(buffer.AsSpan(0, want));
                if (frames <= 0)
                    break;

                for (var frame = 0; frame < frames; frame++)
                {
                    var sum = 0f;
                    for (var channel = 0; channel < channels; channel++)
                        sum += buffer[frame * channels + channel];

                    mono.Add(sum / channels);
                }
            }

            samples = mono.ToArray();
            return samples.Length > 0;
        }
        // Everything, deliberately: this is a third-party decoder fed whatever the endpoint sent, and what it throws on
        //      a bad file isn't documented. A failed transcode falls back to sending the original.
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    ///     RIFF WAVE, 16-bit integer PCM. The only uncompressed shape a speech endpoint realistically serves.
    /// </summary>
    private static bool TryDecodeWav(byte[] data, out float[] samples, out int sampleRate)
    {
        samples = [];
        sampleRate = 0;

        var span = data.AsSpan();
        if (span.Length < 12 || !span.StartsWith("RIFF"u8) || !span[8..].StartsWith("WAVE"u8))
            return false;

        var channels = 0;
        var bitsPerSample = 0;
        var offset = 12;

        while (offset + 8 <= span.Length)
        {
            var id = span.Slice(offset, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(span[(offset + 4)..]);
            var body = offset + 8;
            if (size < 0)
                return false;

            // Streaming encoders write a placeholder size for data; take what is actually there.
            var available = Math.Min(size, span.Length - body);

            if (id.SequenceEqual("fmt "u8) && available >= 16)
            {
                var format = BinaryPrimitives.ReadUInt16LittleEndian(span[body..]);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(span[(body + 2)..]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(span[(body + 4)..]);
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(span[(body + 14)..]);

                // 1 is integer PCM; 0xFFFE (extensible) carries it too, for the channel counts we accept.
                if (format != 1 && format != 0xFFFE)
                    return false;
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (channels < 1 || bitsPerSample != 16 || sampleRate <= 0)
                    return false;

                var frames = Math.Min(available / (2 * channels), sampleRate * KsTtsOpus.MaxDurationSeconds);
                samples = new float[frames];
                for (var frame = 0; frame < frames; frame++)
                {
                    var sum = 0f;
                    for (var channel = 0; channel < channels; channel++)
                        sum += BinaryPrimitives.ReadInt16LittleEndian(span[(body + (frame * channels + channel) * 2)..]);

                    samples[frame] = sum / channels / 32768f;
                }

                return frames > 0;
            }

            // Chunks are padded to an even length.
            offset = body + size + (size & 1);
        }

        return false;
    }

    /// <summary>
    ///     Band-limited resampling with a Blackman-windowed sinc. When going down in rate the cutoff drops with it, so
    ///         nothing above the new Nyquist frequency folds back as aliasing.
    /// </summary>
    public static float[] Resample(float[] input, int fromRate, int toRate)
    {
        if (fromRate == toRate || input.Length == 0)
            return input;

        var step = (double)fromRate / toRate;
        var cutoff = Math.Min(1.0, (double)toRate / fromRate) * 0.95;
        var outputLength = (int)((long)input.Length * toRate / fromRate);
        var output = new float[outputLength];

        for (var i = 0; i < outputLength; i++)
        {
            var center = i * step;
            var first = (int)Math.Floor(center) - ResamplerHalfWidth + 1;
            var sum = 0.0;

            for (var tap = first; tap < first + ResamplerHalfWidth * 2; tap++)
            {
                if (tap < 0 || tap >= input.Length)
                    continue;

                var distance = center - tap;
                var window = BlackmanWindow(distance / ResamplerHalfWidth);
                sum += input[tap] * cutoff * Sinc(cutoff * distance) * window;
            }

            output[i] = (float)sum;
        }

        return output;
    }

    private static double Sinc(double x)
        => Math.Abs(x) < 1e-9 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);

    /// <summary>
    ///     A Blackman window over [-1, 1], zero outside it.
    /// </summary>
    private static double BlackmanWindow(double x)
    {
        if (x <= -1.0 || x >= 1.0)
            return 0.0;

        var phase = Math.PI * (x + 1.0);
        return 0.42 - 0.5 * Math.Cos(phase) + 0.08 * Math.Cos(2.0 * phase);
    }
}
