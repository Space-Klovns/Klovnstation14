namespace Content.Shared._KS14.EnergyShield;

/// <summary>
///     An energy shield backed by a battery instead of by damage thresholds: hits drain charge, and the
///         shield shuts off and refuses to come back on while the battery is flat.
/// </summary>
/// <remarks>
///     Recharging is not this component's job - pair it with a <c>BatterySelfRecharger</c> for that.
///     Deliberately not networked: nothing client-side reads anything here, and the charge the client
///         does need is already replicated on the battery itself.
/// </remarks>
[RegisterComponent]
[Access(typeof(RechargeableEnergyShieldSystem))]
public sealed partial class RechargeableEnergyShieldComponent : Component
{
    /// <summary>
    ///     Units of charge drained per point of damage the shield soaks.
    /// </summary>
    [DataField]
    public float DamageToChargeRatio = 1f;
}
