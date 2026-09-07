// KS14: added in this fork
namespace Content.Shared.Atmos.Components;

public sealed partial class MapAtmosphereComponent
{
    // See AtmosphereSystem.Klovn.SpacingThrust.cs.

    /// <summary>
    /// Multiplier on the recoil a grid on this map gets from gas crossing its boundary.
    /// Gas flowing in and out of a grid mostly cancels out on a map that has a real atmosphere, but a planet's
    /// worth of air also means drag and a surface to sit on, neither of which is simulated - so set this to 0 on
    /// maps where grids have no business being shoved around by their own ventilation.
    /// </summary>
    [DataField]
    public float KsGridThrustModifier = 1f;
}
