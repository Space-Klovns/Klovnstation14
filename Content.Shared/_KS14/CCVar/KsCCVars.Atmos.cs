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
    ///     1 is the physically correct value; turn it down if stations end up drifting more than is fun.
    /// </summary>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<float> AtmosSpacingThrustMultiplier =
        CVarDef.Create("klovn.atmos.spacing_thrust_multiplier", 1f, CVar.SERVERONLY);
}
