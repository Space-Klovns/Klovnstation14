using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Whether critical mobs go through soft crit (crawling, whispering) before hard crit.
    ///     When off, crit works like it does upstream.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> SoftCritEnabled =
        CVarDef.Create("klovn.softcrit.enabled", true, CVar.ARCHIVE | CVar.REPLICATED | CVar.SERVER);
}
