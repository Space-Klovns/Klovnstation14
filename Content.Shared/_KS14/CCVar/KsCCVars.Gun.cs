using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Multiplier applied to impulse force when a projectile hits a structure.
    ///         Can be changed during runtime.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> GunImpulseMultiplier =
        CVarDef.Create("klovn.gun.impulse_multiplier", 0.125f, CVar.SERVER | CVar.REPLICATED);
    /// <summary>
    /// Controls how many hits a penetrating projectile has to do at the very least.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> GunPenetrationMinShots =
        CVarDef.Create("klovn.gun.penetration_min_shots", 2f, CVar.SERVER | CVar.REPLICATED);
}
