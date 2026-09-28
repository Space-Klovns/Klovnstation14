using System.Collections.Generic;
using Content.Server._KS14.Voice;
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
}
