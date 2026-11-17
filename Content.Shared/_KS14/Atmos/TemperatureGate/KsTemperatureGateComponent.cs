using Content.Shared.Atmos;
using Content.Shared.Tools;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Atmos.TemperatureGate;

/// <summary>
///     A one-way gate that lets gas and liquid down from its inlet to its outlet while the inlet's temperature is inside
///         a band, set by a target and tolerance or by its two ends, depending on its <see cref="Mode"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
[Access(typeof(KsSharedTemperatureGateSystem))]
public sealed partial class KsTemperatureGateComponent : Component
{
    /// <seealso cref="KsSharedTemperatureGateSystem.SetEnabled"/>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public bool Enabled;

    /// <seealso cref="KsSharedTemperatureGateSystem.SetMode"/>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public KsTemperatureGateMode Mode = KsTemperatureGateMode.Tolerance;

    /// <summary>
    ///     Kelvin. In <see cref="KsTemperatureGateMode.Tolerance"/>, the middle of the band that passes.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public float TargetTemperature = Atmospherics.T0C;

    /// <summary>
    ///     Kelvin. In <see cref="KsTemperatureGateMode.Tolerance"/>, how far from <see cref="TargetTemperature"/> the gate
    ///         stays open, either way.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public float Tolerance = 10f;

    /// <summary>
    ///     Kelvin. In <see cref="KsTemperatureGateMode.Range"/>, the bottom of the range that passes.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public float RangeMinimum = Atmospherics.T0C;

    /// <summary>
    ///     Kelvin. In <see cref="KsTemperatureGateMode.Range"/>, the top of the range that passes.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public float RangeMaximum = Atmospherics.T20C;

    [DataField, AutoNetworkedField]
    public float MinTemperature = Atmospherics.TCMB;

    [DataField, AutoNetworkedField]
    public float MaxTemperature = 4500f;

    /// <summary>
    ///     If true, the gate is open outside its band rather than inside it.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Inverted;

    /// <summary>
    ///     Whether anything crossed the gate on its last update, which is what the sprite shows.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public bool Flowing;

    [DataField]
    public string InletName = "inlet";

    [DataField]
    public string OutletName = "outlet";

    /// <summary>
    ///     Units of liquid per second per square root kPa that cross the gate by the orifice law, apart from its gas.
    /// </summary>
    [DataField]
    public float LiquidConductance = Content.Shared._KS14.Fluidmos.KsFluidmos.DefaultLiquidConductance;

    /// <summary>
    ///     What it takes to swap <see cref="Inverted"/>.
    /// </summary>
    [DataField]
    public ProtoId<ToolQualityPrototype> InvertQuality = "Pulsing";
}

[Serializable, NetSerializable]
public enum KsTemperatureGateMode : byte
{
    /// <summary>
    ///     Open within <see cref="KsTemperatureGateComponent.Tolerance"/> of
    ///         <see cref="KsTemperatureGateComponent.TargetTemperature"/>.
    /// </summary>
    Tolerance,

    /// <summary>
    ///     Open between <see cref="KsTemperatureGateComponent.RangeMinimum"/> and
    ///         <see cref="KsTemperatureGateComponent.RangeMaximum"/>.
    /// </summary>
    Range,
}

[Serializable, NetSerializable]
public enum KsTemperatureGateUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class KsTemperatureGateToggleStatusMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled { get; } = enabled;
}

[Serializable, NetSerializable]
public sealed class KsTemperatureGateChangeTargetMessage(float temperature) : BoundUserInterfaceMessage
{
    public float Temperature { get; } = temperature;
}

[Serializable, NetSerializable]
public sealed class KsTemperatureGateChangeModeMessage(KsTemperatureGateMode mode) : BoundUserInterfaceMessage
{
    public KsTemperatureGateMode Mode { get; } = mode;
}

[Serializable, NetSerializable]
public sealed class KsTemperatureGateChangeToleranceMessage(float tolerance) : BoundUserInterfaceMessage
{
    public float Tolerance { get; } = tolerance;
}

[Serializable, NetSerializable]
public sealed class KsTemperatureGateChangeRangeMessage(float minimum, float maximum) : BoundUserInterfaceMessage
{
    public float Minimum { get; } = minimum;
    public float Maximum { get; } = maximum;
}

[Serializable, NetSerializable]
public enum KsTemperatureGateVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum KsTemperatureGateState : byte
{
    Off,
    On,
    Flowing,
}
