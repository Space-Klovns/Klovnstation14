using Content.Shared.Atmos;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Anomaly.Prototypes;

/// <summary>
/// Defines the effects a specific gas has on an anomaly when it is present in the atmosphere.
/// </summary>
[Prototype]
public sealed partial class AnomalyGasEffectPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The gas type this effect applies to.
    /// </summary>
    [DataField("gas", required: true)]
    public Gas Gas;

    /// <summary>
    /// How much the stability changes per second at max scaling.
    /// </summary>
    [DataField("stabilityModifier")]
    public float StabilityModifier = 0f;

    /// <summary>
    /// How much the severity changes per second at max scaling.
    /// </summary>
    [DataField("severityModifier")]
    public float SeverityModifier = 0f;

    /// <summary>
    /// How much the health changes per second at max scaling (for healing or draining).
    /// </summary>
    [DataField("healthModifier")]
    public float HealthModifier = 0f;

    /// <summary>
    /// The multiplier applied to research point generation.
    /// </summary>
    [DataField("pointMultiplier")]
    public float PointMultiplier = 1f;

    /// <summary>
    /// The multiplier applied to the time between pulses.
    /// </summary>
    [DataField("pulseFrequencyMultiplier")]
    public float PulseFrequencyMultiplier = 1f;

    /// <summary>
    /// A buffer that reduces health decay when the anomaly is in the decay range.
    /// 1.0 means normal decay, 0.2 means 80% reduction.
    /// </summary>
    [DataField("decayBuffer")]
    public float DecayBuffer = 1f;

    /// <summary>
    /// Multiplier for the power/radius of the anomaly's pulse effects.
    /// </summary>
    [DataField("pulsePowerMultiplier")]
    public float PulsePowerMultiplier = 1f;
}
