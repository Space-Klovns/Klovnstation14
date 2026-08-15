using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Sensors.Prototypes;

/// <summary>
///     One defensive-posture level the RWR readout escalates through (CALM, CAUTION,
///         DANGER). A posture is met when the active threat count reaches
///         <see cref="MinThreats"/>, or when any lit channel's priority reaches
///         <see cref="MinChannelPriority"/> (so one high-priority contact can force
///         escalation on its own). The met posture with the highest
///         <see cref="Order"/> wins; ship a base posture met at zero threats so the
///         readout always resolves.
/// </summary>
[Prototype]
public sealed partial class KsPosturePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The posture's readout label ("CAUTION").</summary>
    [DataField(required: true)]
    public LocId Label;

    /// <summary>Severity rank: of all met postures the highest Order shows.</summary>
    [DataField]
    public int Order;

    /// <summary>Met once this many threat contacts are active.</summary>
    [DataField]
    public int MinThreats;

    /// <summary>Alternatively met when any lit channel's priority reaches this; null = count-driven only.</summary>
    [DataField]
    public int? MinChannelPriority;

    /// <summary>The readout's tint at this posture.</summary>
    [DataField]
    public Color Color = Color.FromHex("#FFC000");
}
