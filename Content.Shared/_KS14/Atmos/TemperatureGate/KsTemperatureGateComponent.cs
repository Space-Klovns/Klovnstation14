using Content.Shared.Atmos;
using Content.Shared.Tools;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Atmos.TemperatureGate;

/// <summary>
///     A one-way gate that lets gas and liquid down from its inlet to its outlet only while the inlet is colder than its
///         target temperature, or hotter when <see cref="Inverted"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
[Access(typeof(KsSharedTemperatureGateSystem))]
public sealed partial class KsTemperatureGateComponent : Component
{
    /// <seealso cref="KsSharedTemperatureGateSystem.SetEnabled"/>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public bool Enabled;

    /// <summary>
    ///     Kelvin. The gate compares its inlet against this.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TargetTemperature = Atmospherics.T0C;

    [DataField, AutoNetworkedField]
    public float MinTemperature = Atmospherics.TCMB;

    [DataField, AutoNetworkedField]
    public float MaxTemperature = 4500f;

    /// <summary>
    ///     If true, the gate opens above <see cref="TargetTemperature"/> rather than below it.
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
