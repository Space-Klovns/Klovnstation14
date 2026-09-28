using Content.Shared._KS14.Voice;

namespace Content.Client._KS14.Voice;

/// <summary>
///     When to start the next voice chunk, and how to line it up with the one playing.
///
///     Playback can only act once per frame, so a chunk is never started at exactly the right moment. Two rules make
///         the frame rate irrelevant to what is heard:
///     <list type="number">
///         <item>
///             Start early rather than late: start the next chunk on the last frame before the current one reaches its
///                 start point, judging "before the next frame" by a pessimistic estimate of the frame time.
///         </item>
///         <item>
///             Then correct the error exactly. Starting early, the new chunk gets that much leading silence. Starting
///                 late (a hitch longer than the estimate), it skips ahead by the lateness. Either way, its first real
///                 sample plays exactly at the start point, so the crossfade lines up to the sample instead of combing,
///                 gapping or clicking.
///         </item>
///     </list>
/// </summary>
public static class KsVoiceChunkTiming
{
    /// <summary>
    ///     How many estimated frames ahead to look when deciding whether this frame is the last chance to start.
    /// </summary>
    public const float FrameSafetyFactor = 1.5f;

    /// <summary>
    ///     Whether the next chunk must start now. That's true when the current chunk, which has
    ///         <paramref name="remainingSeconds"/> left to play, would pass the point where the next should begin
    ///         (<paramref name="startAtSeconds"/> before its end) before another frame is likely to run.
    /// </summary>
    public static bool ShouldStartNext(float remainingSeconds, float startAtSeconds, float expectedFrameSeconds)
    {
        return remainingSeconds - expectedFrameSeconds * FrameSafetyFactor <= startAtSeconds;
    }

    /// <summary>
    ///     How to line up a chunk being started now so its first sample plays <paramref name="startAtSeconds"/> before
    ///         the current chunk ends. Returns the leading silence to add (early start), or how far to skip into it (late
    ///         start). The skip never exceeds <paramref name="startAtSeconds"/>: beyond that, the audio being skipped
    ///         was never played by anything, and it's better heard late than lost.
    /// </summary>
    public static (int PadSamples, float SeekSeconds) Align(float remainingSeconds, float startAtSeconds)
    {
        var offset = remainingSeconds - startAtSeconds;
        if (offset >= 0f)
            return ((int)MathF.Round(offset * (float)KsVoiceConstants.SampleRate), 0f);

        return (0, MathF.Min(-offset, startAtSeconds));
    }
}
