namespace Content.Shared.Atmos.Components;

public sealed partial class FlammableComponent
{
    /// <summary>
    /// Joules of heat per second added to a burning entity's temperature, per fire stack.
    /// Upstream hardcoded this as 12500.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float HeatPerFireStack = 6500f;
}
