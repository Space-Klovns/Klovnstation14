using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Fluidmos.Devices;

[Serializable, NetSerializable]
public enum KsExpansionValveUiKey : byte
{
    Key,
}

/// <summary>
///     What the expansion valve's window shows. <see cref="OutletBoilingPoint"/> and <see cref="VaporFraction"/> are NaN
///         while there is no refrigerant to measure.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsExpansionValveBoundUserInterfaceState(
    float opening,
    bool automatic,
    float pressureSetpoint,
    float vaporFraction,
    float outletPressure,
    float outletBoilingPoint,
    float flowRate) : BoundUserInterfaceState
{
    /// <summary>
    ///     From 0 (shut) to 1 (wide open).
    /// </summary>
    public readonly float Opening = opening;

    public readonly bool Automatic = automatic;

    /// <summary>
    ///     kPa automatic mode holds the outlet at.
    /// </summary>
    public readonly float PressureSetpoint = pressureSetpoint;

    /// <summary>
    ///     Share of the liquid crossing the valve that flashes to vapor, from 0 to 1.
    /// </summary>
    public readonly float VaporFraction = vaporFraction;

    public readonly float OutletPressure = outletPressure;

    /// <summary>
    ///     The refrigerant's boiling point, in kelvin, at the outlet's pressure.
    /// </summary>
    public readonly float OutletBoilingPoint = outletBoilingPoint;

    /// <summary>
    ///     Units of liquid per second.
    /// </summary>
    public readonly float FlowRate = flowRate;
}

[Serializable, NetSerializable]
public sealed class KsExpansionValveSetOpeningMessage(float opening) : BoundUserInterfaceMessage
{
    public readonly float Opening = opening;
}

[Serializable, NetSerializable]
public sealed class KsExpansionValveSetAutomaticMessage(bool automatic) : BoundUserInterfaceMessage
{
    public readonly bool Automatic = automatic;
}

[Serializable, NetSerializable]
public sealed class KsExpansionValveSetPressureMessage(float pressure) : BoundUserInterfaceMessage
{
    public readonly float Pressure = pressure;
}
