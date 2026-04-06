using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Whether or not should speczones should load in the next (or first) round.
    ///         Has no effect on the current round when changed.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<bool> SpeczonesEnabled =
        CVarDef.Create("klovn.speczones.enabled", true, CVar.SERVER);
}
