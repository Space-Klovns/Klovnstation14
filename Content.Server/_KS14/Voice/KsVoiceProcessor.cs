using Content.Shared._KS14.Voice;

namespace Content.Server._KS14.Voice;

/// <summary>
///     Tunables for <see cref="KsVoiceProcessor"/>, taken from the <c>klovn.voice.*</c> cvars.
/// </summary>
public readonly record struct KsVoiceProcessorSettings(
    float CeilingDb,
    float AbuseRmsDb,
    float AbuseClipRatio,
    float AbuseSeconds,
    float AbuseWindowSeconds = 10f);

/// <summary>
///     What <see cref="KsVoiceProcessor.Process"/> found in one chunk.
/// </summary>
public readonly record struct KsVoiceProcessResult(float InputRmsDb, bool AbuseTriggered);

/// <summary>
///     Server-side moderation of one talker's microphone audio, applied before anything is relayed.
///
///     <list type="number">
///         <item>A DC blocker removes constant offsets that would otherwise eat headroom.</item>
///         <item>
///             Each 20 ms frame is classified as abusive when its RMS or its share of clipped samples is above the
///                 configured thresholds. Once abusive frames add up to <see cref="KsVoiceProcessorSettings.AbuseSeconds"/>
///                 within the last <see cref="KsVoiceProcessorSettings.AbuseWindowSeconds"/> of transmitted audio, the
///                 result reports <see cref="KsVoiceProcessResult.AbuseTriggered"/> once and the window starts over.
///                 The window counts audio time, not wall time, so silence neither excuses nor accumulates anything.
///         </item>
///         <item>
///             A peak limiter caps the output at the ceiling: the envelope follows every peak instantly and decays
///                 slowly, and the gain is ceiling / envelope whenever the envelope is above it, so no output sample
///                 can exceed the ceiling.
///         </item>
///     </list>
///
///     Not thread-safe; each uplink connection owns one.
/// </summary>
public sealed class KsVoiceProcessor
{
    /// <summary>
    ///     Input samples at or beyond this magnitude count as clipped.
    /// </summary>
    public const short ClipThreshold = 32000;

    private const float DcBlockerPole = 0.995f;
    private const float EnvelopeReleaseSeconds = 0.15f;

    private readonly float _envelopeRelease = MathF.Exp(-1f / (EnvelopeReleaseSeconds * (float)KsVoiceConstants.SampleRate));

    private KsVoiceProcessorSettings _settings;
    private bool[] _abusiveFrames = [];
    private int _abusiveFrameCursor;
    private int _abusiveFrameCount;

    private float _dcPreviousInput;
    private float _dcPreviousOutput;
    private float _envelope;

    public KsVoiceProcessor(KsVoiceProcessorSettings settings)
    {
        Settings = settings;
    }

    public KsVoiceProcessorSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;

            var windowFrames = Math.Max(1, (int)MathF.Round(value.AbuseWindowSeconds * (float)KsVoiceConstants.SampleRate / (float)KsVoiceConstants.FrameSamples));
            if (_abusiveFrames.Length == windowFrames)
                return;

            _abusiveFrames = new bool[windowFrames];
            _abusiveFrameCursor = 0;
            _abusiveFrameCount = 0;
        }
    }

    /// <summary>
    ///     Processes <paramref name="samples"/> in place.
    /// </summary>
    /// <param name="samples">The audio, limited in place.</param>
    /// <param name="trackAbuse">
    ///     Whether this audio counts towards the abuse window. Only audio that is actually being transmitted
    ///         should: a page left open in a loud room, with push-to-talk up, must never earn its player a mute.
    /// </param>
    public KsVoiceProcessResult Process(Span<short> samples, bool trackAbuse = true)
    {
        var ceiling = 32767f * MathF.Pow(10f, Math.Min(_settings.CeilingDb, 0f) / 20f);
        var abuseRms = 32767f * MathF.Pow(10f, _settings.AbuseRmsDb / 20f);
        // The window only remembers so much, so more abuse than fits in it could never be counted and the auto-mute
        //      would silently never fire. Asking for longer than the window means "abusive the whole time".
        var abuseFramesNeeded = Math.Min(
            (int)MathF.Ceiling(_settings.AbuseSeconds * (float)KsVoiceConstants.SampleRate / (float)KsVoiceConstants.FrameSamples),
            _abusiveFrames.Length);

        var triggered = false;
        var totalSquares = 0d;

        for (var frameStart = 0; frameStart < samples.Length; frameStart += KsVoiceConstants.FrameSamples)
        {
            var frame = samples.Slice(frameStart, Math.Min(KsVoiceConstants.FrameSamples, samples.Length - frameStart));

            var frameSquares = 0d;
            var clipped = 0;

            for (var i = 0; i < frame.Length; i++)
            {
                var input = (float)frame[i];
                if (Math.Abs(input) >= (float)ClipThreshold)
                    clipped++;

                var filtered = input - _dcPreviousInput + DcBlockerPole * _dcPreviousOutput;
                _dcPreviousInput = input;
                _dcPreviousOutput = filtered;

                frameSquares += (double)filtered * (double)filtered;

                var magnitude = Math.Abs(filtered);
                _envelope = Math.Max(magnitude, _envelope * _envelopeRelease);

                var gain = _envelope > ceiling ? ceiling / _envelope : 1f;
                frame[i] = (short)Math.Clamp(filtered * gain, -ceiling, ceiling);
            }

            totalSquares += frameSquares;

            var frameRms = (float)Math.Sqrt(frameSquares / (double)frame.Length);
            var abusive = frameRms > abuseRms || (float)clipped / (float)frame.Length > _settings.AbuseClipRatio;
            if (trackAbuse && RecordFrame(abusive, abuseFramesNeeded))
                triggered = true;
        }

        var rms = samples.Length == 0 ? 0f : (float)Math.Sqrt(totalSquares / (double)samples.Length);
        var rmsDb = rms <= 0f ? -120f : 20f * MathF.Log10(rms / 32767f);

        return new KsVoiceProcessResult(rmsDb, triggered);
    }

    private bool RecordFrame(bool abusive, int abuseFramesNeeded)
    {
        if (_abusiveFrames[_abusiveFrameCursor])
            _abusiveFrameCount--;

        _abusiveFrames[_abusiveFrameCursor] = abusive;
        if (abusive)
            _abusiveFrameCount++;

        _abusiveFrameCursor = (_abusiveFrameCursor + 1) % _abusiveFrames.Length;

        if (_settings.AbuseSeconds <= 0f || _abusiveFrameCount < abuseFramesNeeded)
            return false;

        Array.Clear(_abusiveFrames);
        _abusiveFrameCount = 0;
        return true;
    }
}
