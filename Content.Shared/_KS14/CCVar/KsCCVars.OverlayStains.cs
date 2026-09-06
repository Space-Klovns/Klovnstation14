using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Should overlay stains be drawn more expensively?
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<bool> ComplexStainDrawing =
        CVarDef.Create("klovn.stains.complexdrawing", false, CVar.CLIENT | CVar.CLIENTONLY); // TODO LCDC FUCK: FIX THIS ASAP

    /// <summary>
    ///     Width, in degrees, of the arc a stain is visible within. Stains fade out towards the edges of it,
    ///         the same way directional wallmounts do, and are not drawn at all outside of it.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> StainArcDegrees =
        CVarDef.Create("klovn.stains.arc_degrees", 180f, CVar.SERVER | CVar.REPLICATED);
}
