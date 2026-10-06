using Content.Shared.Localizations;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     The client's interface language. Independent of the server and chat translation.
    /// </summary>
    public static readonly CVarDef<string> ClientLocale =
        CVarDef.Create("klovn.client_locale", ContentLocalizationManager.DefaultCultureName, CVar.CLIENTONLY | CVar.ARCHIVE);
}
