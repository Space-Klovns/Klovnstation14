using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.Prototypes;

/// <summary>
/// This is a prototype for...
/// </summary>
[Prototype]
public sealed partial class PacketFrequencyPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("minFrequency")]
    public int MinimalFrequency = 0;

    [DataField("maxFrequency")]
    public int MaximalFrequency = 1;

    [DataField]
    public int Frequency;
}
