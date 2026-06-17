using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Can jetpacks fly on grids? Gravity limitations still apply.
    ///
    ///     Technically, if this is false, jetpacks can only fly when theres no gravitycomponent
    ///         somewhere. Not exactly if theres a grid.
    /// </summary>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<bool> JetpacksCanFlyOnGrids =
        CVarDef.Create("klovn.jetpacks.flyongrids", true, CVar.ARCHIVE | CVar.REPLICATED | CVar.SERVER);
}
