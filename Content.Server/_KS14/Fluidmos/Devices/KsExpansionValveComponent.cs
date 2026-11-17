using Content.Shared._KS14.Fluidmos;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._KS14.Fluidmos.Devices;

/// <summary>
///     A refrigeration expansion valve: lets liquid through by the orifice law as far as it is open, as if fed from the
///         bottom of a condenser's receiver, and vapor through whatever of the orifice the liquid leaves. Crossing to the
///         low side, the liquid flashes and chills it. In automatic mode it opens and closes itself to hold its outlet at
///         a set pressure, and so the evaporator at that pressure's boiling point.
/// </summary>
/// <remarks>
///     Automatic mode is a constant-pressure valve rather than a thermostatic one because it needs only the net it is
///         attached to: every radiator splits an evaporator into its own nets, so the valve never sees the evaporator's
///         outlet, where a thermostatic valve's bulb would read the superheat.
/// </remarks>
[RegisterComponent, Access(typeof(KsExpansionValveSystem))]
public sealed partial class KsExpansionValveComponent : Component
{
    [DataField]
    public string InletName = "inlet";

    [DataField]
    public string OutletName = "outlet";

    /// <summary>
    ///     From 0 (shut) to 1 (wide open). Scales <see cref="Conductance"/>.
    /// </summary>
    [DataField]
    public float Opening = 0.5f;

    /// <summary>
    ///     Units per second per √kPa when wide open.
    /// </summary>
    [DataField]
    public float Conductance = KsFluidmos.DefaultLiquidConductance;

    [DataField]
    public bool Automatic;

    /// <summary>
    ///     kPa that automatic mode holds the outlet at.
    /// </summary>
    [DataField]
    public float PressureSetpoint = 200f;

    [DataField]
    public float MaxPressureSetpoint = Content.Shared.Atmos.Atmospherics.MaxOutputPressure;

    /// <summary>
    ///     How fast automatic mode moves the opening: per second, at an error the size of the setpoint.
    /// </summary>
    [DataField]
    public float AutomaticGain = 0.5f;

    [DataField]
    public TimeSpan UiUpdateInterval = TimeSpan.FromSeconds(1);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextUiUpdate;

    [ViewVariables]
    public float VaporFraction = float.NaN;

    /// <summary>
    ///     The refrigerant's boiling point at the outlet's pressure: the coldest the evaporator gets.
    /// </summary>
    [ViewVariables]
    public float OutletBoilingPoint = float.NaN;

    [ViewVariables]
    public float FlowRate;
}
