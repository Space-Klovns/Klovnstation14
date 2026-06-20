using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Is the WORDFILTER... enabled?
    ///
    ///     Changes only really apply when entitysystems start for the first time; i.e., when the server first starts.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> WordFilterEnabled =
        CVarDef.Create("klovn.ic.wordfilter_enabled", true, CVar.ARCHIVE | CVar.REPLICATED | CVar.SERVER);
}
