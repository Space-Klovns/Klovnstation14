using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Master switch. Off means every message takes the vanilla path byte for byte.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> LanguageEnabled =
        CVarDef.Create("klovn.language.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     The language entities without authored knowledge implicitly know; never obfuscated.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<string> LanguageFallback =
        CVarDef.Create("klovn.language.fallback", "KsLangCommon", CVar.SERVERONLY);
}
