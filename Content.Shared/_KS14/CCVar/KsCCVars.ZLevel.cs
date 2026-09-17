using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Downward acceleration applied to entities transiting between z-levels, in z-levels per second squared.
    ///         Only applies on z-levels whose map has enabled gravity.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelTransitGravity =
        CVarDef.Create("klovn.zlevel.transit_gravity", 9.8f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Maximum absolute vertical speed of a transiting entity, in z-levels per second.
    ///         Without this a long fall would skip entire z-levels in a single tick.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelTransitTerminalVelocity =
        CVarDef.Create("klovn.zlevel.transit_terminal_velocity", 20f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Impact speed, in z-levels per second, at or above which landing is damaging by default.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelTransitImpactVelocity =
        CVarDef.Create("klovn.zlevel.transit_impact_velocity", 3.5f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     How often, in seconds, each player's z-level view subscriber is moved to track them.
    ///     This is what loads the z-level below into a client's PVS, and transit crossings are predicted
    ///         against it, so a slow interval shows up as the level below being briefly unpopulated.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelPvsUpdateInterval =
        CVarDef.Create("klovn.zlevel.pvs_update_interval", 0.25f, CVar.SERVERONLY);

    /// <summary>
    ///     Tiles a transiting entity's sprite is lifted per unit of transit height.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelTransitHeightOffset =
        CVarDef.Create("klovn.zlevel.transit_height_offset", 0.7f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
