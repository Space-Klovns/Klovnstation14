using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Sensors;

[Serializable, NetSerializable]
public enum KsDatalinkTransmitterUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum KsDatalinkReceiverUiKey : byte
{
    Key,
}

/// <summary>Valid for both transmitter and receiver UIs.</summary>
[Serializable, NetSerializable]
public sealed class KsDatalinkToggleMessage : BoundUserInterfaceMessage;

/// <summary>
///     Clamped server-side to
///         [<see cref="KsDatalink.MinFrequency"/>, <see cref="KsDatalink.MaxFrequency"/>].
/// </summary>
[Serializable, NetSerializable]
public sealed class KsDatalinkSetFrequencyMessage(int frequency) : BoundUserInterfaceMessage
{
    public readonly int Frequency = frequency;
}

/// <summary>Clamped server-side to [0, 1].</summary>
[Serializable, NetSerializable]
public sealed class KsDatalinkSetPowerMessage(float powerFraction) : BoundUserInterfaceMessage
{
    public readonly float PowerFraction = powerFraction;
}

[Serializable, NetSerializable]
public sealed class KsDatalinkTransmitterBuiState(
    bool enabled,
    bool powered,
    int frequency,
    float powerFraction,
    float maxRange,
    bool allFrequencies,
    bool unlimitedRange) : BoundUserInterfaceState
{
    public readonly bool Enabled = enabled;
    public readonly bool Powered = powered;
    public readonly int Frequency = frequency;
    public readonly float PowerFraction = powerFraction;
    public readonly float MaxRange = maxRange;

    /// <summary>Mapping-set: heard on every channel, so the tuned frequency is moot.</summary>
    public readonly bool AllFrequencies = allFrequencies;

    /// <summary>Mapping-set: sector-wide reach, so the effective range is moot.</summary>
    public readonly bool UnlimitedRange = unlimitedRange;

    public float EffectiveRange => MaxRange * PowerFraction;
}

[Serializable, NetSerializable]
public sealed class KsDatalinkReceiverBuiState(
    bool enabled,
    bool powered,
    int frequency,
    int heardTransmitters) : BoundUserInterfaceState
{
    public readonly bool Enabled = enabled;
    public readonly bool Powered = powered;
    public readonly int Frequency = frequency;

    /// <summary>
    ///     Transmitters ingested last sensor tick. Deliberately just a count: it must
    ///         not leak who or where.
    /// </summary>
    public readonly int HeardTransmitters = heardTransmitters;
}
