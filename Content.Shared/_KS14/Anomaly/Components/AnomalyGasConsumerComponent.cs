using Content.Shared.Atmos;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Anomaly.Components;

/// <summary>
/// This component allows an anomaly to interact with the gases in its environment.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AnomalyGasConsumerComponent : Component
{
    #region Configuration
    /// <summary>
    /// Pressure required (in kPa) to start triggering effects.
    /// </summary>
    [DataField("minPressureThreshold")]
    public float MinPressureThreshold = 10f;

    /// <summary>
    /// Pressure at which the gas effect reaches 100% scaling.
    /// </summary>
    [DataField("maxPressureCap")]
    public float MaxPressureCap = 500f;

    /// <summary>
    /// Baseline moles of dominant gas removed per second at 100% scaling.
    /// </summary>
    [DataField("consumptionRate")]
    public float ConsumptionRate = 1.0f;

    /// <summary>
    /// Penalty multiplier applied to the secondary gas effect.
    /// </summary>
    [DataField("secondaryGasPenalty")]
    public float SecondaryGasPenalty = 0.5f;

    /// <summary>
    /// How often the anomaly checks the atmosphere (in seconds).
    /// Capped between 0.5 and 2.0 in logic.
    /// </summary>
    [DataField("updateInterval")]
    public float UpdateInterval = 1.0f;
    #endregion

    #region Gameplay State
    /// <summary>
    /// The primary gas currently affecting the anomaly.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public Gas? PrimaryGas;

    /// <summary>
    /// The secondary gas currently affecting the anomaly.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public Gas? SecondaryGas;

    /// <summary>
    /// The strength of the primary gas effect (0.0 to 1.0), based on pressure.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public float PrimaryScalingFactor = 0f;

    /// <summary>
    /// The strength of the secondary gas effect (0.0 to 1.0), based on pressure.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public float SecondaryScalingFactor = 0f;

    [AutoNetworkedField] public float PointMultiplier = 1f;

    [AutoNetworkedField]
    public float PulseFrequencyMultiplier = 1f;

    [AutoNetworkedField]
    public float DecayBuffer = 1f;

    [AutoNetworkedField]
    public float PulsePowerMultiplier = 1f;
    #endregion
}
