using System.Buffers.Binary;
using System.IO;
using Content.Server._KS14.TTS;
using Content.Shared._KS14.TTS;

namespace Content.IntegrationTests.Tests._KS14.TTS;

/// <summary>
///     Pure tests for the TTS Opus path: the Ogg Opus reader and writer, and the server's transcoder. No game instance
///         needed.
/// </summary>
[TestFixture]
[TestOf(typeof(KsTtsOpus))]
public sealed class KsTtsCodecTests
{
    private static short[] Sine(int count, int sampleRate, float frequency, float amplitude)
    {
        var samples = new short[count];
        for (var i = 0; i < count; i++)
            samples[i] = (short)(amplitude * 32767f * MathF.Sin(2f * MathF.PI * frequency * (float)i / (float)sampleRate));

        return samples;
    }

    private static double Rms(ReadOnlySpan<short> samples)
    {
        var sum = 0d;
        foreach (var sample in samples)
            sum += (double)sample * (double)sample;

        return Math.Sqrt(sum / Math.Max(samples.Length, 1));
    }

    /// <summary>
    ///     The frequency of a pure tone, from its rising zero crossings.
    /// </summary>
    private static double ToneFrequency(ReadOnlySpan<short> samples, int sampleRate)
    {
        var crossings = 0;
        for (var i = 1; i < samples.Length; i++)
        {
            if (samples[i - 1] < 0 && samples[i] >= 0)
                crossings++;
        }

        return (double)crossings * (double)sampleRate / (double)samples.Length;
    }

    private static byte[] ReadResource(string path)
    {
        // Tests run from the build output; the resources are in the repository.
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Resources")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "couldn't find the Resources directory");
        return File.ReadAllBytes(Path.Combine(directory!.FullName, "Resources", path));
    }

    [Test]
    public void DecodesALibopusFile()
    {
        Assert.That(KsTtsOpus.TryDecode(KsTtsFixtures.ReferenceOggOpus, out var samples), Is.True);

        // Exactly the encoded length: the pre-skip is dropped from the front, and the end trimmed to the final
        //      granule position rather than left padded out to a whole packet.
        Assert.That(samples, Has.Length.EqualTo(KsTtsFixtures.ReferenceSampleCount));

        // CELT at 64 kbps keeps the waveform, so an off-by-one in the pre-skip or a dropped packet shows up here.
        double signal = 0, noise = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var reference = (double)KsTtsFixtures.ReferenceSample(i) * 32767d;
            signal += reference * reference;
            noise += ((double)samples[i] - reference) * ((double)samples[i] - reference);
        }

        var snr = 10d * Math.Log10(signal / Math.Max(noise, 1d));
        Assert.That(snr, Is.GreaterThan(15d), $"decoded audio should match the source; SNR {snr:F1} dB");
    }

    [Test]
    public void RoundTripsItsOwnFiles([Values(16000, 24000, 48000)] int sampleRate)
    {
        var source = Sine(sampleRate * 3 / 2, sampleRate, frequency: 300f, amplitude: 0.4f);
        var file = KsTtsOpus.Encode(source, sampleRate, bitrate: 32000, complexity: 5);

        Assert.That(KsTtsOpus.Identify(file), Is.EqualTo(TtsCodec.Opus));
        Assert.That(KsTtsOpus.TryDecode(file, out var decoded), Is.True);

        // Always decoded at 48 kHz, so a 1.5 s clip is 72000 samples whatever it was encoded at.
        Assert.That(decoded, Has.Length.EqualTo(KsTtsOpus.SampleRate * 3 / 2));

        // SILK doesn't keep phase, so compare what it must keep: loudness and pitch. Skip the first 20 ms, where
        //      the encoder is still settling.
        var settled = decoded.AsSpan(KsTtsOpus.SampleRate / 50);
        var sourceRms = Rms(source);
        Assert.That(Rms(settled), Is.EqualTo(sourceRms).Within(30).Percent);
        Assert.That(ToneFrequency(settled, KsTtsOpus.SampleRate), Is.EqualTo(300d).Within(3).Percent);
    }

    [Test]
    public void WritesValidOggPages()
    {
        // Loud noise at the top bitrate: packets over 255 bytes, so the lacing has to continue within a page.
        var random = new Random(1);
        var source = new short[24000 * 2];
        for (var i = 0; i < source.Length; i++)
            source[i] = (short)random.Next(-20000, 20000);

        var file = KsTtsOpus.Encode(source, sampleRate: 24000, bitrate: KsTtsOpus.MaxBitrate, complexity: 0);

        var pages = 0;
        var offset = 0;
        while (offset < file.Length)
        {
            Assert.That(file.AsSpan(offset).StartsWith("OggS"u8), $"page {pages} starts with the capture pattern");

            var segmentCount = file[offset + 26];
            var bodyLength = 0;
            for (var i = 0; i < segmentCount; i++)
                bodyLength += file[offset + 27 + i];

            var pageLength = 27 + segmentCount + bodyLength;

            // Recompute the CRC with the field zeroed, as a player would check it.
            var page = file.AsSpan(offset, pageLength).ToArray();
            var stored = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(22));
            page.AsSpan(22, 4).Clear();
            Assert.That(OggCrc(page), Is.EqualTo(stored), $"page {pages} CRC");

            var flags = file[offset + 5];
            Assert.That((flags & 0x02) != 0, Is.EqualTo(offset == 0), $"page {pages} beginning-of-stream flag");
            Assert.That((flags & 0x04) != 0, Is.EqualTo(offset + pageLength == file.Length), $"page {pages} end-of-stream flag");

            offset += pageLength;
            pages++;
        }

        Assert.That(offset, Is.EqualTo(file.Length));
        Assert.That(pages, Is.GreaterThan(3), "two header pages and several audio pages");
        Assert.That(KsTtsOpus.TryDecode(file, out var decoded), Is.True);
        Assert.That(decoded, Has.Length.EqualTo(KsTtsOpus.SampleRate * 2));
    }

    private static uint OggCrc(ReadOnlySpan<byte> data)
    {
        // Bit by bit, independently of the table the writer uses.
        var crc = 0u;
        foreach (var value in data)
        {
            crc ^= (uint)value << 24;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7 : crc << 1;
        }

        return crc;
    }

    [Test]
    public void IdentifiesContainers()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KsTtsOpus.Identify(KsTtsFixtures.ReferenceOggOpus), Is.EqualTo(TtsCodec.Opus));
            Assert.That(KsTtsOpus.Identify(ReadResource("Audio/Voice/Talk/lizard.ogg")), Is.EqualTo(TtsCodec.Vorbis));
            Assert.That(KsTtsOpus.Identify("RIFF\0\0\0\0WAVE"u8.ToArray()), Is.Null);
            Assert.That(KsTtsOpus.Identify([]), Is.Null);
        });
    }

    [Test]
    public void RejectsMalformedFilesWithoutThrowing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KsTtsOpus.TryDecode([], out _), Is.False, "empty");
            Assert.That(KsTtsOpus.TryDecode(ReadResource("Audio/Voice/Talk/lizard.ogg"), out _), Is.False, "Vorbis");
            Assert.That(KsTtsOpus.TryDecode(KsTtsFixtures.ReferenceOggOpus.AsSpan(0, 100), out _), Is.False, "truncated headers");
        });

        // Corrupt bytes throughout, including the headers: nothing may escape as an exception, since clients decode
        //      whatever the server relays.
        var random = new Random(7);
        for (var round = 0; round < 500; round++)
        {
            var corrupt = (byte[])KsTtsFixtures.ReferenceOggOpus.Clone();
            for (var i = 0; i < 4; i++)
                corrupt[random.Next(corrupt.Length)] = (byte)random.Next(256);

            Assert.DoesNotThrow(() => KsTtsOpus.TryDecode(corrupt, out _), $"round {round}");
        }
    }

    [Test]
    public void TranscodesVorbis()
    {
        var vorbis = ReadResource("Audio/Voice/Talk/lizard.ogg");
        Assert.That(KsTtsTranscoder.TryDecodeToMono(vorbis, out var source, out var sourceRate), Is.True);

        Assert.That(KsTtsTranscoder.TryTranscode(vorbis, bitrate: 32000, complexity: 5, out var opus), Is.True);
        Assert.That(KsTtsOpus.Identify(opus), Is.EqualTo(TtsCodec.Opus));
        Assert.That(KsTtsOpus.TryDecode(opus, out var decoded), Is.True);

        var expected = (double)source.Length * KsTtsOpus.SampleRate / sourceRate;
        Assert.That((double)decoded.Length, Is.EqualTo(expected).Within(KsTtsOpus.SampleRate / 100),
            "same duration, to within 10 ms");
    }

    [Test]
    public void TranscodesWav()
    {
        const int rate = 22050;
        var tone = Sine(rate, rate, frequency: 500f, amplitude: 0.5f);

        var wav = new byte[44 + tone.Length * 2];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), wav.Length - 8);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1); // mono
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), rate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), rate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), tone.Length * 2);
        for (var i = 0; i < tone.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(44 + i * 2), tone[i]);

        Assert.That(KsTtsTranscoder.TryTranscode(wav, bitrate: 32000, complexity: 5, out var opus), Is.True);
        Assert.That(KsTtsOpus.TryDecode(opus, out var decoded), Is.True);

        Assert.That(decoded, Has.Length.EqualTo(KsTtsOpus.SampleRate));
        Assert.That(ToneFrequency(decoded.AsSpan(KsTtsOpus.SampleRate / 50), KsTtsOpus.SampleRate),
            Is.EqualTo(500d).Within(3).Percent);
    }

    [Test]
    public void ResamplesWithoutShiftingPitchOrLevel()
    {
        const int fromRate = 22050;
        const int toRate = 24000;

        var input = new float[fromRate];
        for (var i = 0; i < input.Length; i++)
            input[i] = 0.5f * MathF.Sin(2f * MathF.PI * 1000f * (float)i / (float)fromRate);

        var output = KsTtsTranscoder.Resample(input, fromRate, toRate);
        Assert.That(output, Has.Length.EqualTo(toRate));

        // Against the ideal tone at the new rate, away from the edges where the kernel runs off the input.
        double signal = 0, noise = 0;
        for (var i = 100; i < output.Length - 100; i++)
        {
            var ideal = 0.5d * Math.Sin(2d * Math.PI * 1000d * (double)i / (double)toRate);
            signal += ideal * ideal;
            noise += (output[i] - ideal) * (output[i] - ideal);
        }

        var snr = 10d * Math.Log10(signal / noise);
        Assert.That(snr, Is.GreaterThan(40d), $"resampled tone SNR {snr:F1} dB");
    }

    [Test]
    public void PicksTheLowestLosslessOpusRate()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KsTtsOpus.EncodingRate(16000), Is.EqualTo(16000));
            Assert.That(KsTtsOpus.EncodingRate(22050), Is.EqualTo(24000));
            Assert.That(KsTtsOpus.EncodingRate(44100), Is.EqualTo(48000));
            Assert.That(KsTtsOpus.EncodingRate(96000), Is.EqualTo(48000));
        });
    }
}
