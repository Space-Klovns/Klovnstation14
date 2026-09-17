using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Downward acceleration applied to entities transiting between z-levels, in z-levels per second squared.
    ///         Only applies where there is gravity: the grid under the entity if it has any, otherwise the map.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelTransitGravity =
        CVarDef.Create("klovn.zlevel.transit_gravity", 1.8f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Maximum absolute vertical speed of a transiting entity, in z-levels per second.
    ///         Without this a long fall would skip entire z-levels in a single tick.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelTransitTerminalVelocity =
        CVarDef.Create("klovn.zlevel.transit_terminal_velocity", 10f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Impact speed, in z-levels per second, at or above which landing is damaging by default.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelTransitImpactVelocity =
        CVarDef.Create("klovn.zlevel.transit_impact_velocity", 0.9f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     How long, in seconds, something is stunned for after being landed on.
    /// </summary>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<float> ZLevelTransitCrushStun =
        CVarDef.Create("klovn.zlevel.transit_crush_stun", 2f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     How long, in seconds, something stays knocked down after a transit ends.
    ///     An entity is kept down for the whole transit regardless; this is only the sprawl on landing.
    /// </summary>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<float> ZLevelTransitLandingKnockdown =
        CVarDef.Create("klovn.zlevel.transit_landing_knockdown", 1f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     How often, in seconds, each player's z-level view subscriber is moved to track them.
    ///     This is what loads the z-level below into a client's PVS, and transit crossings are predicted
    ///         against it, so a slow interval shows up as the level below being briefly unpopulated.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelPvsUpdateInterval =
        CVarDef.Create("klovn.zlevel.pvs_update_interval", 0.25f, CVar.SERVERONLY);
}
