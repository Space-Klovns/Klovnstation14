namespace Content.Server.Atmos.Piping.Binary.Components;

/// <summary>
/// Defines a passive gate, which equalizes gas from
/// inlet to outlet, but does not allow gas to flow from outlet to inlet.
/// </summary>
[RegisterComponent]
public sealed partial class GasPassiveGateComponent : Component
{
    [DataField("inlet")]
    public string InletName = "inlet";

    [DataField("outlet")]
    public string OutletName = "outlet";

    [ViewVariables(VVAccess.ReadOnly)]
    [DataField]
    public float FlowRate;

    // KS14 start
    /// <summary>
    ///     KS14: units of liquid per second per square root kPa that cross this device by the orifice law, apart from its gas.
    /// </summary>
    [DataField]
    public float KsLiquidConductance = Content.Shared._KS14.Fluidmos.KsFluidmos.DefaultLiquidConductance;
    // KS14 end
}
