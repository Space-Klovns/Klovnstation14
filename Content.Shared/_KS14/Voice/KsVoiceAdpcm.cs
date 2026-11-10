namespace Content.Shared._KS14.Voice;

/// <summary>
///     IMA ADPCM (4 bits per sample) used to compress relayed voice. Chosen because the client sandbox has no
///         audio codec that content can reach, and this one is small enough to own outright.
///
///     A packet is self-contained: a 3-byte header holding the encoder state at the start of the packet (predictor,
///         step index) followed by two samples per byte, low nibble first. A lost packet therefore never desyncs the
///         decoder of the packets after it.
/// </summary>
public static class KsVoiceAdpcm
{
    public const int HeaderBytes = 3;

    private static readonly int[] IndexTable = [-1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8];

    private static readonly int[] StepTable =
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107,
        118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963,
        1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894,
        6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794,
        32767,
    ];

    /// <summary>
    ///     Size of the packet produced for <paramref name="sampleCount"/> samples. Odd counts are padded by one sample.
    /// </summary>
    public static int EncodedSize(int sampleCount)
        => HeaderBytes + (sampleCount + 1) / 2;

    /// <summary>
    ///     Number of samples a packet of <paramref name="packetBytes"/> bytes decodes to, or 0 if it is too small.
    /// </summary>
    public static int DecodedSampleCount(int packetBytes)
        => packetBytes <= HeaderBytes ? 0 : (packetBytes - HeaderBytes) * 2;

    /// <summary>
    ///     Continuous encoder state for one talker.
    /// </summary>
    public struct EncoderState
    {
        public int Predictor;
        public int StepIndex;
    }

    /// <summary>
    ///     Encodes <paramref name="samples"/> into <paramref name="destination"/>, which must be at least
    ///         <see cref="EncodedSize"/> bytes. Returns the number of bytes written.
    /// </summary>
    public static int Encode(ref EncoderState state, ReadOnlySpan<short> samples, Span<byte> destination)
    {
        var size = EncodedSize(samples.Length);
        if (destination.Length < size)
            throw new ArgumentException("Destination too small for encoded ADPCM.", nameof(destination));

        destination[0] = (byte)(state.Predictor & 0xFF);
        destination[1] = (byte)((state.Predictor >> 8) & 0xFF);
        destination[2] = (byte)state.StepIndex;

        var predictor = state.Predictor;
        var stepIndex = state.StepIndex;

        for (var i = 0; i < samples.Length; i += 2)
        {
            var low = EncodeSample(samples[i], ref predictor, ref stepIndex);
            var high = i + 1 < samples.Length
                ? EncodeSample(samples[i + 1], ref predictor, ref stepIndex)
                : EncodeSample((short)predictor, ref predictor, ref stepIndex);

            destination[HeaderBytes + i / 2] = (byte)(low | (high << 4));
        }

        state.Predictor = predictor;
        state.StepIndex = stepIndex;
        return size;
    }

    /// <summary>
    ///     Decodes a packet into <paramref name="destination"/>. Returns the number of samples written, or -1 if the
    ///         packet is malformed.
    /// </summary>
    public static int Decode(ReadOnlySpan<byte> packet, Span<short> destination)
    {
        var sampleCount = DecodedSampleCount(packet.Length);
        if (sampleCount == 0 || destination.Length < sampleCount)
            return -1;

        var predictor = (int)(short)(packet[0] | (packet[1] << 8));
        var stepIndex = (int)packet[2];
        if (stepIndex >= StepTable.Length)
            return -1;

        for (var i = 0; i < sampleCount; i += 2)
        {
            var packed = packet[HeaderBytes + i / 2];
            destination[i] = DecodeNibble(packed & 0x0F, ref predictor, ref stepIndex);
            destination[i + 1] = DecodeNibble(packed >> 4, ref predictor, ref stepIndex);
        }

        return sampleCount;
    }

    private static int EncodeSample(short sample, ref int predictor, ref int stepIndex)
    {
        var step = StepTable[stepIndex];
        var difference = (int)sample - predictor;

        var nibble = 0;
        if (difference < 0)
        {
            nibble = 8;
            difference = -difference;
        }

        // Quantise the difference to three magnitude bits, accumulating the reconstructed delta exactly as the
        //      decoder will, so encoder and decoder predictors never drift apart.
        var delta = step >> 3;
        if (difference >= step)
        {
            nibble |= 4;
            difference -= step;
            delta += step;
        }

        step >>= 1;
        if (difference >= step)
        {
            nibble |= 2;
            difference -= step;
            delta += step;
        }

        step >>= 1;
        if (difference >= step)
        {
            nibble |= 1;
            delta += step;
        }

        predictor = (nibble & 8) != 0 ? predictor - delta : predictor + delta;
        predictor = Math.Clamp(predictor, (int)short.MinValue, (int)short.MaxValue);
        stepIndex = Math.Clamp(stepIndex + IndexTable[nibble], 0, StepTable.Length - 1);

        return nibble;
    }

    private static short DecodeNibble(int nibble, ref int predictor, ref int stepIndex)
    {
        var step = StepTable[stepIndex];

        var delta = step >> 3;
        if ((nibble & 4) != 0)
            delta += step;
        if ((nibble & 2) != 0)
            delta += step >> 1;
        if ((nibble & 1) != 0)
            delta += step >> 2;

        predictor = (nibble & 8) != 0 ? predictor - delta : predictor + delta;
        predictor = Math.Clamp(predictor, (int)short.MinValue, (int)short.MaxValue);
        stepIndex = Math.Clamp(stepIndex + IndexTable[nibble], 0, StepTable.Length - 1);

        return (short)predictor;
    }
}
