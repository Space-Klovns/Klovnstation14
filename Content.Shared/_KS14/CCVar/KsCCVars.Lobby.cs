using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     The screen effect on the lobby art (KsLobbyGrade: darkening, curvature, subpixel mask, static), so the bright
    ///         menu reads over any art. Read by the lobby UI only. The name predates the main menu, which replaced dimming.
    /// </summary>
    public static readonly CVarDef<bool> LobbyAdaptiveContrast =
        CVarDef.Create("klovn.lobby.adaptive_contrast", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Lobby font family id (jura, bender). Unknown ids fall back to jura. Not archived: there is no options control for it.
    ///     Read by the client stylesheet only, which rebuilds when it changes.
    /// </summary>
    public static readonly CVarDef<string> LobbyFont =
        CVarDef.Create("klovn.lobby.font", "jura", CVar.CLIENTONLY);
}
