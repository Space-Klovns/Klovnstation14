namespace Content.IntegrationTests.Tests._KS14.TTS;

/// <summary>
///     Reference clips from outside our own code, so the Ogg Opus reader is tested against what a real encoder writes
///         rather than only against our own writer.
/// </summary>
internal static class KsTtsFixtures
{
    /// <summary>
    ///     0.5 s at 48 kHz, mono: 0.3 × (0.6 sin 440 Hz + 0.4 sin 880 Hz), from phase 0. See <see cref="ReferenceSample"/>.
    /// </summary>
    public const int ReferenceSampleCount = 24000;

    /// <summary>
    ///     <see cref="ReferenceSampleCount"/> samples of the signal above, encoded by libopus 1.4 through opus-tools 0.2:
    ///         <c>opusenc --music --bitrate 64 --comp 10</c>. Pre-skip 312; one page each for the two headers and the audio. The music tuning keeps it in CELT,
    ///         which preserves the waveform, so the decode can be compared sample for sample.
    /// </summary>
    public static readonly byte[] ReferenceOggOpus = Convert.FromBase64String(
        "T2dnUwACAAAAAAAAAACGfTVNAAAAAKR7/OYBE09wdXNIZWFkAQE4AYC7AAAAAABPZ2dTAAAAAAAAAAAAAIZ9NU0BAAAAQHrIiwP///5PcHVzVG" +
        "Fncx0AAABsaWJvcHVzIDEuNCwgbGlib3B1c2VuYyAwLjIuMQIAAAAjAAAARU5DT0RFUj1vcHVzZW5jIGZyb20gb3B1cy10b29scyAwLjIuAAAA" +
        "RU5DT0RFUl9PUFRJT05TPS0tbXVzaWMgLS1iaXRyYXRlIDY0IC0tY29tcCAxMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "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" +
        "IdxQd0pJW9k3LQ==");

    public static float ReferenceSample(int index)
    {
        var time = (float)index / 48000f;
        return 0.3f * (0.6f * MathF.Sin(2f * MathF.PI * 440f * time) + 0.4f * MathF.Sin(2f * MathF.PI * 880f * time));
    }
}
