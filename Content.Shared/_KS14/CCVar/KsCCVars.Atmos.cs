using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Whether gas crossing a grid's boundary pushes that grid, as it would a rocket.
    ///     Covers both air venting into space and air blowing in from a map that has an atmosphere.
    /// </summary>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<bool> AtmosSpacingThrust =
        CVarDef.Create("klovn.atmos.spacing_thrust", true, CVar.SERVERONLY);

    /// <summary>
    ///     Scales the impulse produced by <see cref="AtmosSpacingThrust"/>.
    /// </summary>
    /// <remarks>
    ///     <para>1 is the impulse the gas actually carries away, but a grid's physics mass is nothing like the mass
    ///         of the station it is drawn as: grid chunk fixtures use <c>SharedShuttleSystem.TileDensityMultiplier</c>,
    ///         so a tile of hull weighs 0.5 kg to the physics engine, against the 800 kg
    ///         <c>ContentTileDefinition.Mass</c> gives it for shuttle impacts. A tile of room air weighs 3 kg. At 1,
    ///         venting one tile of air moves a 5000-tile station at 0.56 m/s and venting a room reaches 11 m/s.</para>
    ///
    ///     <para>Scaling by 1/1600 would put the impulse back in proportion to that 800 kg/tile figure, which is the
    ///         honest answer - and an invisible one, since a realistic station shrugs a breach off. The default is
    ///         16x that instead, enough that a serious breach visibly shoves the station over a few seconds while a
    ///         small leak still does nothing.</para>
    /// </remarks>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<float> AtmosSpacingThrustMultiplier =
        CVarDef.Create("klovn.atmos.spacing_thrust_multiplier", 0.008f, CVar.SERVERONLY);

    /// <summary>
    ///     How long, in seconds, a grid takes to be handed the impulse it is owed - the time constant of the
    ///     reservoir drain, so 63% arrives within one of these and 95% within three.
    ///     Momentum is conserved regardless of this value; it only decides how abrupt the shove feels.
    ///     Zero or less applies each impulse the moment it is worked out, which reads as a jolt.
    /// </summary>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<float> AtmosSpacingThrustSmoothing =
        CVarDef.Create("klovn.atmos.spacing_thrust_smoothing", 0.5f, CVar.SERVERONLY);
}
