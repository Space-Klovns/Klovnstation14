using System.Collections.Generic;
using Content.Server._KS14.Voice;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Voice;

namespace Content.IntegrationTests.Tests._KS14.Voice;

/// <summary>
///     Pure tests for the voice codec, the moderation processor and public URL resolution. No game instance needed.
/// </summary>
[TestFixture]
[TestOf(typeof(KsVoiceAdpcm))]
public sealed class KsVoiceCodecTests
{
    private static short[] Sine(int count, float frequency, float amplitude, int phaseOffset = 0)
    {
        var samples = new short[count];
        for (var i = 0; i < count; i++)
        {
            var t = (float)(i + phaseOffset) / (float)KsVoiceConstants.SampleRate;
            samples[i] = (short)(amplitude * 32767f * MathF.Sin(2f * MathF.PI * frequency * t));
        }

        return samples;
    }

    private static double SnrDb(short[] reference, short[] decoded, int skip)
    {
        double signal = 0, noise = 0;
        for (var i = skip; i < reference.Length; i++)
        {
            signal += (double)reference[i] * (double)reference[i];
            var error = (double)reference[i] - (double)decoded[i];
            noise += error * error;
        }

        return 10d * Math.Log10(signal / Math.Max(noise, 1d));
    }

    [Test]
    public void AdpcmRoundTripKeepsSpeechBand()
    {
        var input = Sine(KsVoiceConstants.MaxChunkSamples, frequency: 440f, amplitude: 0.5f);
        var state = new KsVoiceAdpcm.EncoderState();
        var packet = new byte[KsVoiceAdpcm.EncodedSize(input.Length)];
        KsVoiceAdpcm.Encode(ref state, input, packet);

        var output = new short[input.Length];
        var decoded = KsVoiceAdpcm.Decode(packet, output);

        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.EqualTo(input.Length));
            Assert.That(packet, Has.Length.LessThanOrEqualTo(KsVoiceFrameMessage.MaxPayloadBytes));
            // Skip the first few ms while the step size adapts from zero.
            Assert.That(SnrDb(input, output, skip: 64), Is.GreaterThan(20d));
        });
    }

    [Test]
    public void AdpcmPacketDecodesAloneAfterALostPredecessor()
    {
        var first = Sine(KsVoiceConstants.MaxChunkSamples, frequency: 300f, amplitude: 0.6f);
        var second = Sine(KsVoiceConstants.MaxChunkSamples, frequency: 300f, amplitude: 0.6f, phaseOffset: KsVoiceConstants.MaxChunkSamples);

        var state = new KsVoiceAdpcm.EncoderState();
        var firstPacket = new byte[KsVoiceAdpcm.EncodedSize(first.Length)];
        var secondPacket = new byte[KsVoiceAdpcm.EncodedSize(second.Length)];
        KsVoiceAdpcm.Encode(ref state, first, firstPacket);
        KsVoiceAdpcm.Encode(ref state, second, secondPacket);

        // The first packet is "lost": decode the second one with no history at all.
        var output = new short[second.Length];
        KsVoiceAdpcm.Decode(secondPacket, output);

        Assert.That(SnrDb(second, output, skip: 0), Is.GreaterThan(20d),
            "each packet must carry the encoder state it starts from");
    }

    [Test]
    public void AdpcmRejectsMalformedPackets()
    {
        var output = new short[KsVoiceConstants.MaxChunkSamples];

        Assert.Multiple(() =>
        {
            Assert.That(KsVoiceAdpcm.Decode(new byte[2], output), Is.EqualTo(-1), "shorter than a header");
            Assert.That(KsVoiceAdpcm.Decode(new byte[] { 0, 0, 200, 0x11 }, output), Is.EqualTo(-1), "step index out of range");
            Assert.That(KsVoiceAdpcm.Decode(new byte[KsVoiceAdpcm.HeaderBytes + 1000], output), Is.EqualTo(-1), "too long for the destination");
        });
    }

    private static readonly int OpusComplexity = KsCCVars.VoiceOpusComplexity.DefaultValue;

    /// <summary>
    ///     Voiced, speech-shaped audio: a harmonic series on a gliding pitch, weighted by three formants, in syllable-like
    ///         bursts. Opus's speech coder models exactly this, so a pure tone would flatter or confuse it.
    /// </summary>
    private static short[] SpeechLike(int count)
    {
        var raw = new double[count];
        var phase = 0d;
        var peak = 0d;
        for (var i = 0; i < count; i++)
        {
            var t = (double)i / (double)KsVoiceConstants.SampleRate;
            var pitch = 140d + 30d * Math.Sin(2d * Math.PI * 1.3d * t);
            phase += 2d * Math.PI * pitch / (double)KsVoiceConstants.SampleRate;

            var value = 0d;
            for (var harmonic = 1; harmonic <= 25; harmonic++)
            {
                var frequency = (double)harmonic * pitch;
                var formants = Math.Exp(-Math.Pow((frequency - 500d) / 200d, 2d)) +
                               0.7d * Math.Exp(-Math.Pow((frequency - 1500d) / 300d, 2d)) +
                               0.4d * Math.Exp(-Math.Pow((frequency - 2500d) / 400d, 2d));
                value += formants * Math.Sin((double)harmonic * phase) / (double)harmonic;
            }

            raw[i] = value * Math.Max(0d, Math.Sin(2d * Math.PI * 3d * t));
            peak = Math.Max(peak, Math.Abs(raw[i]));
        }

        var samples = new short[count];
        for (var i = 0; i < count; i++)
            samples[i] = (short)(0.5d * 32767d * raw[i] / peak);

        return samples;
    }

    /// <summary>
    ///     Runs <paramref name="input"/> through a codec in 60 ms chunks, the way the relay does.
    /// </summary>
    private static (short[] Decoded, int PayloadBytes) RoundTrip(KsVoiceCodec codec, short[] input, int opusBitrate = 32000)
    {
        var encoder = KsVoiceEncoder.Create(codec, opusBitrate, OpusComplexity);
        var decoder = KsVoiceDecoder.Create(codec);
        var output = new List<short>();
        var scratch = new short[KsVoiceOpus.MaxConcealSamples];
        var payloadBytes = 0;

        for (var offset = 0; offset + KsVoiceConstants.MaxChunkSamples <= input.Length; offset += KsVoiceConstants.MaxChunkSamples)
        {
            var payload = encoder.Encode(input.AsSpan(offset, KsVoiceConstants.MaxChunkSamples));
            payloadBytes += payload.Length;

            var decoded = decoder.Decode(payload, scratch);
            Assert.That(decoded, Is.EqualTo(KsVoiceConstants.MaxChunkSamples), $"{codec} chunk at {offset}");
            output.AddRange(scratch.AsSpan(0, decoded).ToArray());
        }

        return (output.ToArray(), payloadBytes);
    }

    /// <summary>
    ///     How closely the decoded audio follows the input, at the codec's own delay: the Pearson correlation at the
    ///         best lag within 40 ms, and the SNR there.
    /// </summary>
    private static (double Correlation, double SnrDb, int Lag) Compare(short[] reference, short[] decoded)
    {
        var best = (Correlation: double.MinValue, SnrDb: 0d, Lag: 0);
        for (var lag = 0; lag <= KsVoiceConstants.SampleRate / 25; lag++)
        {
            double sumXy = 0, sumXx = 0, sumYy = 0, noise = 0;
            var length = Math.Min(reference.Length, decoded.Length - lag);
            for (var i = KsVoiceConstants.FrameSamples; i < length; i++)
            {
                var x = (double)reference[i];
                var y = (double)decoded[i + lag];
                sumXy += x * y;
                sumXx += x * x;
                sumYy += y * y;
                noise += (x - y) * (x - y);
            }

            var correlation = sumXy / Math.Sqrt(Math.Max(sumXx * sumYy, 1d));
            if (correlation > best.Correlation)
                best = (correlation, 10d * Math.Log10(sumXx / Math.Max(noise, 1d)), lag);
        }

        return best;
    }

    [Test]
    [TestOf(typeof(KsVoiceOpusEncoder))]
    public void OpusRoundTripKeepsSpeech()
    {
        var input = SpeechLike(KsVoiceConstants.SampleRate * 2);
        var (opus, opusBytes) = RoundTrip(KsVoiceCodec.Opus, input);
        var (adpcm, adpcmBytes) = RoundTrip(KsVoiceCodec.Adpcm, input);
        var opusMatch = Compare(input, opus);
        var adpcmMatch = Compare(input, adpcm);

        var seconds = (double)opus.Length / (double)KsVoiceConstants.SampleRate;
        var opusKbps = (double)opusBytes * 8d / seconds / 1000d;
        var adpcmKbps = (double)adpcmBytes * 8d / seconds / 1000d;
        TestContext.Out.WriteLine($"opus: {opusKbps:0.0} kbps, correlation {opusMatch.Correlation:0.000}, SNR {opusMatch.SnrDb:0.0} dB at lag {opusMatch.Lag}");
        TestContext.Out.WriteLine($"adpcm: {adpcmKbps:0.0} kbps, correlation {adpcmMatch.Correlation:0.000}, SNR {adpcmMatch.SnrDb:0.0} dB at lag {adpcmMatch.Lag}");

        Assert.Multiple(() =>
        {
            // Opus reshapes the waveform rather than copying it (it codes what's audible), so its SNR is lower than
            //      ADPCM's even though it sounds cleaner; correlation at its own delay is the fair measure. Measured
            //      at about 0.98.
            Assert.That(opusMatch.Correlation, Is.GreaterThan(0.9d), "the decoded audio follows the input");
            Assert.That(opusKbps, Is.LessThan(40d), "at about the configured 32 kbps");
            Assert.That(opusKbps, Is.LessThan(adpcmKbps * 0.7d), "well under ADPCM's bandwidth");
        });
    }

    [Test]
    [TestOf(typeof(KsVoiceOpusEncoder))]
    public void OpusEncodesEveryChunkLength()
    {
        var encoder = new KsVoiceOpusEncoder(32000, OpusComplexity);
        var decoder = new KsVoiceOpusDecoder();
        var scratch = new short[KsVoiceOpus.MaxConcealSamples];

        Assert.Multiple(() =>
        {
            // The page sends one to three 20 ms frames per message: 20, 40 and 60 ms are all legal Opus frames.
            for (var frames = 1; frames <= KsVoiceConstants.MaxFramesPerUplinkMessage; frames++)
            {
                var samples = SpeechLike(frames * KsVoiceConstants.FrameSamples);
                var payload = encoder.Encode(samples);
                Assert.That(payload, Has.Length.InRange(1, KsVoiceFrameMessage.MaxPayloadBytes), $"{frames} frames");
                Assert.That(decoder.Decode(payload, scratch), Is.EqualTo(samples.Length), $"{frames} frames decode to the same length");
            }
        });
    }

    [Test]
    [TestOf(typeof(KsVoiceOpusDecoder))]
    public void OpusConcealsALostPacket()
    {
        var input = SpeechLike(KsVoiceConstants.MaxChunkSamples * 6);
        var encoder = new KsVoiceOpusEncoder(32000, OpusComplexity);
        var decoder = new KsVoiceOpusDecoder();
        var scratch = new short[KsVoiceOpus.MaxConcealSamples];

        var packets = new List<byte[]>();
        for (var offset = 0; offset < input.Length; offset += KsVoiceConstants.MaxChunkSamples)
            packets.Add(encoder.Encode(input.AsSpan(offset, KsVoiceConstants.MaxChunkSamples)));

        for (var i = 0; i < 3; i++)
            decoder.Decode(packets[i], scratch);

        // Packet 3 is lost.
        var concealed = decoder.Conceal(KsVoiceConstants.MaxChunkSamples, scratch);
        var energy = 0d;
        for (var i = 0; i < concealed; i++)
            energy += (double)scratch[i] * (double)scratch[i];

        var afterLoss = decoder.Decode(packets[4], scratch);

        Assert.Multiple(() =>
        {
            Assert.That(concealed, Is.EqualTo(KsVoiceConstants.MaxChunkSamples), "the gap is filled for its whole length");
            Assert.That(energy, Is.GreaterThan(0d), "with something, not silence: the voice carries on through the gap");
            Assert.That(afterLoss, Is.EqualTo(KsVoiceConstants.MaxChunkSamples), "and the stream carries on after it");
            Assert.That(new KsVoiceAdpcmDecoder().Conceal(KsVoiceConstants.MaxChunkSamples, scratch), Is.Zero,
                "ADPCM has nothing to conceal with, so its gaps are skipped as before");
        });
    }

    [Test]
    [TestOf(typeof(KsVoiceOpusDecoder))]
    public void OpusRejectsMalformedPackets()
    {
        var decoder = new KsVoiceOpusDecoder();
        var scratch = new short[KsVoiceOpus.MaxConcealSamples];

        Assert.Multiple(() =>
        {
            Assert.That(decoder.Decode(ReadOnlySpan<byte>.Empty, scratch), Is.EqualTo(-1), "empty");
            // Code 3 (an arbitrary number of frames) with a frame count of zero is invalid.
            Assert.That(decoder.Decode(new byte[] { 0x03, 0x00 }, scratch), Is.EqualTo(-1), "zero frames");
        });
    }

    [Test]
    [TestOf(typeof(KsVoiceUplinkManager))]
    public void CodecNames()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KsVoiceUplinkManager.ParseCodec(KsCCVars.VoiceCodec.DefaultValue), Is.EqualTo(KsVoiceCodec.Opus), "the default");
            Assert.That(KsVoiceUplinkManager.ParseCodec(" Opus "), Is.EqualTo(KsVoiceCodec.Opus));
            Assert.That(KsVoiceUplinkManager.ParseCodec("adpcm"), Is.EqualTo(KsVoiceCodec.Adpcm));
            Assert.That(KsVoiceUplinkManager.ParseCodec("mp3"), Is.Null);
        });
    }

    /// <summary>
    ///     Not a pass/fail check: how long Opus takes per chunk, for the design doc. Encoding runs on each talker's page
    ///         connection thread on the server; decoding runs on every listening client's main thread.
    /// </summary>
    [Test]
    [TestOf(typeof(KsVoiceOpusEncoder))]
    public void OpusCost()
    {
        var input = SpeechLike(KsVoiceConstants.SampleRate * 10);
        var encoder = new KsVoiceOpusEncoder(32000, OpusComplexity);
        var decoder = new KsVoiceOpusDecoder();
        var scratch = new short[KsVoiceOpus.MaxConcealSamples];
        var chunks = input.Length / KsVoiceConstants.MaxChunkSamples;

        // Once through to warm up the JIT, then timed.
        var packets = new byte[chunks][];
        for (var pass = 0; pass < 2; pass++)
        {
            var encodeWatch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < chunks; i++)
                packets[i] = encoder.Encode(input.AsSpan(i * KsVoiceConstants.MaxChunkSamples, KsVoiceConstants.MaxChunkSamples));
            encodeWatch.Stop();

            var decodeWatch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < chunks; i++)
                decoder.Decode(packets[i], scratch);
            decodeWatch.Stop();

            if (pass == 0)
                continue;

            var encodeUs = encodeWatch.Elapsed.TotalMilliseconds * 1000d / (double)chunks;
            var decodeUs = decodeWatch.Elapsed.TotalMilliseconds * 1000d / (double)chunks;
            TestContext.Out.WriteLine($"per 60 ms chunk: encode {encodeUs:0} us, decode {decodeUs:0} us " +
                                      $"({encodeUs / 600d:0.0}% and {decodeUs / 600d:0.0}% of a core per talker)");

            Assert.That(encodeWatch.Elapsed + decodeWatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)),
                "faster than real time, at the very least");
        }
    }

    [Test]
    [TestOf(typeof(KsVoiceProcessor))]
    public void LimiterCapsOutputAtTheCeiling()
    {
        var settings = new KsVoiceProcessorSettings(CeilingDb: -6f, AbuseRmsDb: 0f, AbuseClipRatio: 2f, AbuseSeconds: 0f);
        var processor = new KsVoiceProcessor(settings);
        var ceiling = 32767f * MathF.Pow(10f, -6f / 20f);

        var peak = 0;
        for (var chunk = 0; chunk < 20; chunk++)
        {
            // Full-scale square wave: the worst thing a microphone page can send.
            var samples = new short[KsVoiceConstants.MaxChunkSamples];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = (i / 20) % 2 == 0 ? short.MaxValue : short.MinValue;

            processor.Process(samples);
            foreach (var sample in samples)
                peak = Math.Max(peak, Math.Abs((int)sample));
        }

        Assert.That(peak, Is.LessThanOrEqualTo((int)MathF.Ceiling(ceiling)));
    }

    [Test]
    [TestOf(typeof(KsVoiceProcessor))]
    public void SustainedClippingTriggersAutoMuteOnce()
    {
        var settings = new KsVoiceProcessorSettings(CeilingDb: -6f, AbuseRmsDb: -9f, AbuseClipRatio: 0.05f, AbuseSeconds: 3f);
        var processor = new KsVoiceProcessor(settings);

        var chunksPerSecond = KsVoiceConstants.SampleRate / KsVoiceConstants.MaxChunkSamples;
        var triggeredAt = new List<int>();

        for (var chunk = 0; chunk < chunksPerSecond * 5; chunk++)
        {
            var samples = new short[KsVoiceConstants.MaxChunkSamples];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = (i / 20) % 2 == 0 ? short.MaxValue : short.MinValue;

            if (processor.Process(samples).AbuseTriggered)
                triggeredAt.Add(chunk);
        }

        Assert.Multiple(() =>
        {
            Assert.That(triggeredAt, Has.Count.EqualTo(1), "one trigger for five seconds of abuse against a 3s threshold");
            Assert.That(triggeredAt[0], Is.InRange(chunksPerSecond * 3 - 1, chunksPerSecond * 3 + 1));
        });
    }

    [Test]
    [TestOf(typeof(KsVoiceUplinkManager))]
    public void FinishedWebsocketsCanStillBeReleased()
    {
        // Releasing a finished websocket's TCP connection reaches a private engine field by reflection. If an engine
        //      update renames it, voice keeps working but every closed page's socket lingers in CLOSE_WAIT for the life
        //      of the server, with one log line to show for it. This is the only thing that would notice.
        Assert.That(KsVoiceUplinkManager.CanReleaseListenerConnections, Is.True,
            "StatusHost+ContextImpl._context has changed; update KsVoiceUplinkManager.GetConnectionRelease");
    }

    [Test]
    [TestOf(typeof(KsVoiceProcessor))]
    public void AbuseThresholdLongerThanTheWindowStillTriggers()
    {
        // 15 s asked for, 10 s remembered: without clamping, the count can never reach the threshold.
        var settings = new KsVoiceProcessorSettings(CeilingDb: -6f, AbuseRmsDb: -9f, AbuseClipRatio: 0.05f, AbuseSeconds: 15f, AbuseWindowSeconds: 10f);
        var processor = new KsVoiceProcessor(settings);

        var chunksPerSecond = KsVoiceConstants.SampleRate / KsVoiceConstants.MaxChunkSamples;
        var triggeredAt = -1;

        for (var chunk = 0; chunk < chunksPerSecond * 12 && triggeredAt < 0; chunk++)
        {
            var samples = new short[KsVoiceConstants.MaxChunkSamples];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = (i / 20) % 2 == 0 ? short.MaxValue : short.MinValue;

            if (processor.Process(samples).AbuseTriggered)
                triggeredAt = chunk;
        }

        // A whole 10 s window, in chunks (index of the chunk that completes it).
        var windowChunk = (int)MathF.Ceiling(10f * (float)KsVoiceConstants.SampleRate / (float)KsVoiceConstants.MaxChunkSamples) - 1;
        Assert.That(triggeredAt, Is.InRange(windowChunk - 1, windowChunk + 1),
            "a threshold longer than the window means a whole window of abuse, not never");
    }

    [Test]
    [TestOf(typeof(KsVoiceProcessor))]
    public void UntrackedAudioNeverTriggersButIsStillLimited()
    {
        var settings = new KsVoiceProcessorSettings(CeilingDb: -6f, AbuseRmsDb: -9f, AbuseClipRatio: 0.05f, AbuseSeconds: 3f);
        var processor = new KsVoiceProcessor(settings);
        var ceiling = (int)MathF.Ceiling(32767f * MathF.Pow(10f, -6f / 20f));

        var triggered = false;
        var peak = 0;
        for (var chunk = 0; chunk < 200; chunk++)
        {
            var samples = new short[KsVoiceConstants.MaxChunkSamples];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = (i / 20) % 2 == 0 ? short.MaxValue : short.MinValue;

            triggered |= processor.Process(samples, trackAbuse: false).AbuseTriggered;
            foreach (var sample in samples)
                peak = Math.Max(peak, Math.Abs((int)sample));
        }

        Assert.Multiple(() =>
        {
            Assert.That(triggered, Is.False, "audio that isn't transmitted must not count towards a mute");
            Assert.That(peak, Is.LessThanOrEqualTo(ceiling));
        });
    }

    [Test]
    [TestOf(typeof(KsVoiceProcessor))]
    public void NormalSpeechLevelsNeverTrigger()
    {
        var settings = new KsVoiceProcessorSettings(CeilingDb: -6f, AbuseRmsDb: -9f, AbuseClipRatio: 0.05f, AbuseSeconds: 3f);
        var processor = new KsVoiceProcessor(settings);

        var triggered = false;
        for (var chunk = 0; chunk < 200; chunk++)
        {
            // About -16 dBFS RMS: loud, clear speech.
            var samples = Sine(KsVoiceConstants.MaxChunkSamples, frequency: 220f, amplitude: 0.22f, phaseOffset: chunk * KsVoiceConstants.MaxChunkSamples);
            triggered |= processor.Process(samples).AbuseTriggered;
        }

        Assert.That(triggered, Is.False);
    }

    [Test]
    [TestOf(typeof(KsVoiceLinkManager))]
    public void PublicUrlResolution()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KsVoiceLinkManager.ResolvePublicBaseUrl("https://voice.example.com/", "", ""),
                Is.EqualTo("https://voice.example.com"), "explicit url wins, trailing slash trimmed");
            Assert.That(KsVoiceLinkManager.ResolvePublicBaseUrl("", "ss14s://ks14.example.com", "http://localhost:1212/"),
                Is.EqualTo("https://ks14.example.com"), "ss14s maps to https on 443");
            Assert.That(KsVoiceLinkManager.ResolvePublicBaseUrl("", "ss14://1.2.3.4", ""),
                Is.EqualTo("http://1.2.3.4:1212"), "ss14 maps to http on the default status port");
            Assert.That(KsVoiceLinkManager.ResolvePublicBaseUrl("", "ss14://1.2.3.4:5000", ""),
                Is.EqualTo("http://1.2.3.4:5000"));
            Assert.That(KsVoiceLinkManager.ResolvePublicBaseUrl("", "", "http://localhost:1212/"),
                Is.EqualTo("http://localhost:1212"), "falls back to the transfer endpoint");
            Assert.That(KsVoiceLinkManager.ResolvePublicBaseUrl("not a url", "garbage", "ftp://x"), Is.Null);
        });
    }

    [Test]
    [TestOf(typeof(KsVoiceLinkManager))]
    public void PublicPagePathResolution()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KsVoiceLinkManager.ResolvePublicPagePath(KsCCVars.VoicePublicPath.DefaultValue), Is.EqualTo(KsVoiceLinkManager.PagePath),
                "the cvar's default is where the status host serves the page");
            Assert.That(KsVoiceLinkManager.ResolvePublicPagePath(""), Is.EqualTo(KsVoiceLinkManager.PagePath), "empty means the default");
            Assert.That(KsVoiceLinkManager.ResolvePublicPagePath("/"), Is.EqualTo("/"), "the root of the public URL");
            Assert.That(KsVoiceLinkManager.ResolvePublicPagePath("talk"), Is.EqualTo("/talk/"), "slashes added");
            Assert.That(KsVoiceLinkManager.ResolvePublicPagePath(" //a//b.c-d_e~f/ "), Is.EqualTo("/a/b.c-d_e~f/"), "tidied");

            // Anything that isn't a plain path is refused, and the default used instead.
            Assert.That(KsVoiceLinkManager.ResolvePublicPagePath("https://voice.example.com/talk"), Is.Null, "a full URL");
            Assert.That(KsVoiceLinkManager.ResolvePublicPagePath("/a/../b"), Is.Null, "a parent segment");
            Assert.That(KsVoiceLinkManager.ResolvePublicPagePath("/talk?x=1"), Is.Null, "a query");
            Assert.That(KsVoiceLinkManager.ResolvePublicPagePath("/talk#x"), Is.Null, "a fragment, which would swallow the token");
            Assert.That(KsVoiceLinkManager.ResolvePublicPagePath("/my talk"), Is.Null, "whitespace");
        });
    }
}
