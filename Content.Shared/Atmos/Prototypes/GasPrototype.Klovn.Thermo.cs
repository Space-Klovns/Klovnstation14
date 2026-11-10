namespace Content.Shared.Atmos.Prototypes;

public sealed partial class GasPrototype
{
    /// <summary>
    ///     Van der Waals attraction constant, in kPa L²/mol². Used only as a thermal correction (Joule–Thomson at
    ///         throttles) and for the critical temperature; pressure stays ideal-gas everywhere. Null is an ideal gas.
    /// </summary>
    [DataField]
    public float? KsVanDerWaalsA;

    /// <summary>
    ///     Van der Waals excluded volume, in L/mol. Null is an ideal gas.
    /// </summary>
    [DataField]
    public float? KsVanDerWaalsB;

    /// <summary>
    ///     Overrides the heat capacity ratio that compression heat derives from <see cref="MolarHeatCapacity"/>
    ///         (<c>1 + R / Cv</c>), for a gas whose atmos heat capacity is below the physical floor. Upstream's
    ///         <c>HeatCapacityRatio</c> contradicts its own heat capacities, so it isn't used.
    /// </summary>
    [DataField]
    public float? KsHeatCapacityRatio;
}
