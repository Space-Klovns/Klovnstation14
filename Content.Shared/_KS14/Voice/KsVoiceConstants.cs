namespace Content.Shared._KS14.Voice;

/// <summary>
///     Format constants shared by the microphone page, the server relay and client playback.
/// </summary>
public static class KsVoiceConstants
{
    /// <summary>
    ///     Mono sample rate of all voice audio, in Hz.
    /// </summary>
    public const int SampleRate = 16000;

    /// <summary>
    ///     Samples in one 20 ms frame, the unit the microphone page sends in.
    /// </summary>
    public const int FrameSamples = SampleRate / 50;

    /// <summary>
    ///     Most frames a single uplink message may carry.
    /// </summary>
    public const int MaxFramesPerUplinkMessage = 3;

    /// <summary>
    ///     Most samples a single uplink message or relay chunk may carry (60 ms), which keeps an ADPCM relay
    ///         message comfortably under the default network MTU.
    /// </summary>
    public const int MaxChunkSamples = FrameSamples * MaxFramesPerUplinkMessage;

    /// <summary>
    ///     Version byte at the start of every binary uplink message.
    /// </summary>
    public const byte UplinkProtocolVersion = 1;

    /// <summary>
    ///     Bytes before the samples in a binary uplink message: version, reserved, 16-bit sequence.
    /// </summary>
    public const int UplinkHeaderBytes = 4;
}
