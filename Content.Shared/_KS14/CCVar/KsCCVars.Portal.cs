using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Whether view portals draw the area around their linked portal at all. Read by the client renderer only.
    /// </summary>
    public static readonly CVarDef<bool> PortalRenderEnabled =
        CVarDef.Create("klovn.portal.render_enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     The most view portals the client draws the destination of in one frame; the nearest win.
    ///         Read by the client renderer only.
    /// </summary>
    public static readonly CVarDef<int> PortalMaxRendered =
        CVarDef.Create("klovn.portal.max_rendered", 4, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     The resolution each portal view is rendered at, relative to the main view; it covers the same world
    ///         area, so lower is blurrier and cheaper. Clamped to 0.1 - 1. Read by the client renderer only.
    /// </summary>
    public static readonly CVarDef<float> PortalResolutionScale =
        CVarDef.Create("klovn.portal.resolution_scale", 0.5f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Every how many frames a far or small portal view is re-rendered while the main eye is still; 1 is every
    ///         frame. A moving eye always refreshes, because the capture is placed by world position.
    ///         Read by the client renderer only.
    /// </summary>
    public static readonly CVarDef<int> PortalFarUpdateInterval =
        CVarDef.Create("klovn.portal.far_update_interval", 1, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Whether portal views are lit. Off renders them fullbright, which skips the light pipeline.
    ///         Read by the client renderer only.
    /// </summary>
    public static readonly CVarDef<bool> PortalLighting =
        CVarDef.Create("klovn.portal.lighting", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     How near, in world units, a player has to be to a view portal to be sent the area around its
    ///         linked portal.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> PortalPvsRange =
        CVarDef.Create("klovn.portal.pvs_range", 10f, CVar.SERVERONLY);

    /// <summary>
    ///     Scale of the PVS range around the viewer entity placed at a linked portal's destination, relative to
    ///         a normal player's. Lower is cheaper; the destination only needs to cover what a window shows.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> PortalViewPvsScale =
        CVarDef.Create("klovn.portal.view_pvs_scale", 0.5f, CVar.SERVERONLY);
}
