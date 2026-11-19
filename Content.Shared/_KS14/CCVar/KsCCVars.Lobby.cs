using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     The lobby art's backdrop effect, whichever layout shows: the tactical layout dims bright art under its text
    ///         (KsLobbyContrast), the Kharkiv one puts its screen effect on it (KsLobbyGrade). Read by the lobby UI only.
    /// </summary>
    public static readonly CVarDef<bool> LobbyAdaptiveContrast =
        CVarDef.Create("klovn.lobby.adaptive_contrast", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Lobby font family id (jura, bender). Unknown ids fall back to jura. Not archived: there is no options control for it.
    ///     Read by the client stylesheet only, which rebuilds when it changes.
    /// </summary>
    public static readonly CVarDef<string> LobbyFont =
        CVarDef.Create("klovn.lobby.font", "jura", CVar.CLIENTONLY);

    /// <summary>
    ///     Which lobby layout every client shows: an id from <see cref="_KS14.Lobby.KsLobbyLayouts"/>. The default,
    ///         <c>auto</c>, is the tactical layout except on clown holidays (<see cref="_KS14.Lobby.KsLobbyLayouts.IsKharkivSeason"/>,
    ///         by each client's own date); the other ids force one. Unknown ids act as <c>auto</c>. Set by the server
    ///         (admins: <c>lobbylayout</c>), not by players; clients switch live.
    /// </summary>
    public static readonly CVarDef<string> LobbyLayout =
        CVarDef.Create("klovn.lobby.layout", Content.Shared._KS14.Lobby.KsLobbyLayouts.Auto, CVar.SERVER | CVar.REPLICATED);
}
