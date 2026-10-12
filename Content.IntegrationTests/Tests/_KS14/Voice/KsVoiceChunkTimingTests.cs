using Content.Client._KS14.Voice;
using Content.Shared._KS14.Voice;

namespace Content.IntegrationTests.Tests._KS14.Voice;

/// <summary>
///     Pure tests for when voice playback starts its next chunk and how it lines the chunk up. No game instance needed.
/// </summary>
[TestFixture]
[TestOf(typeof(KsVoiceChunkTiming))]
public sealed class KsVoiceChunkTimingTests
{
    private const float Overlap = 0.02f;

    [Test]
    public void EarlyStartPadsToTheExactStartPoint()
    {
        // 10 ms before the next chunk should begin: that much leading silence, no skip.
        var (padSamples, seekSeconds) = KsVoiceChunkTiming.Align(remainingSeconds: Overlap + 0.01f, startAtSeconds: Overlap);

        Assert.Multiple(() =>
        {
            Assert.That(padSamples, Is.EqualTo(KsVoiceConstants.SampleRate / 100));
            Assert.That(seekSeconds, Is.Zero);
        });
    }

    [Test]
    public void LateStartSkipsExactlyTheLateness()
    {
        // 5 ms past the start point: skip the 5 ms of fade-in that should already have played.
        var (padSamples, seekSeconds) = KsVoiceChunkTiming.Align(remainingSeconds: Overlap - 0.005f, startAtSeconds: Overlap);

        Assert.Multiple(() =>
        {
            Assert.That(padSamples, Is.Zero);
            Assert.That(seekSeconds, Is.EqualTo(0.005f).Within(1e-5f));
        });
    }

    [Test]
    public void LateStartNeverSkipsAudioNobodyHeard()
    {
        // The current chunk ended 50 ms ago. Only its lookahead (the overlap) was heard; the rest must still play.
        var (padSamples, seekSeconds) = KsVoiceChunkTiming.Align(remainingSeconds: -0.05f, startAtSeconds: Overlap);

        Assert.Multiple(() =>
        {
            Assert.That(padSamples, Is.Zero);
            Assert.That(seekSeconds, Is.EqualTo(Overlap).Within(1e-6f));
        });

        // With no overlap there is nothing that was heard, so nothing to skip.
        Assert.That(KsVoiceChunkTiming.Align(remainingSeconds: -0.05f, startAtSeconds: 0f).SeekSeconds, Is.Zero);
    }

    [Test]
    public void StartsOnTheLastFrameBeforeTheStartPoint()
    {
        const float remaining = Overlap + 0.03f;

        Assert.Multiple(() =>
        {
            // At 144 FPS a later frame will still arrive before the start point, so wait for it.
            Assert.That(KsVoiceChunkTiming.ShouldStartNext(remaining, Overlap, expectedFrameSeconds: 1f / 144f), Is.False);

            // At 30 FPS the next frame lands after it, so start now and pad.
            Assert.That(KsVoiceChunkTiming.ShouldStartNext(remaining, Overlap, expectedFrameSeconds: 1f / 30f), Is.True);

            // Already past it: start now whatever the frame rate.
            Assert.That(KsVoiceChunkTiming.ShouldStartNext(Overlap - 0.001f, Overlap, expectedFrameSeconds: 0f), Is.True);
        });
    }
}
