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

    /// <summary>
    ///     The chance that someone who goes down while firing a full-auto gun clenches on it and keeps firing.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> GunTriggerSpasmChance =
        CVarDef.Create("klovn.gun.trigger_spasm_chance", 0.220f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     The master switch for projectile damage falloff: off, nothing falls off. On, a projectile falls off if it
    ///         qualifies, see <see cref="ProjectileDamageFalloffEverywhere"/>. Read by shared code, and the client
    ///         predicts hits, so it is replicated.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<bool> ProjectileDamageFalloff =
        CVarDef.Create("klovn.projectile.damage_falloff", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Which projectiles qualify for falloff while <see cref="ProjectileDamageFalloff"/> is on. True: every
    ///         projectile carrying a falloff component (the ballistic bullets). False (the default): only those fired by
    ///         a gun with a KsGunProjectileFalloff component, such as the Field Commander bots' guns.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<bool> ProjectileDamageFalloffEverywhere =
        CVarDef.Create("klovn.projectile.damage_falloff_everywhere", false, CVar.SERVER | CVar.REPLICATED);
}
