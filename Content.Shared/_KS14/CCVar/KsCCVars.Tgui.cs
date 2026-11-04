using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Restyle the game's stock windows, buttons and panels after /tg/'s tgui. The ported tgui windows look the same
    ///         either way. Read by the client stylesheet only, which rebuilds when it changes.
    /// </summary>
    public static readonly CVarDef<bool> TguiTheme =
        CVarDef.Create("klovn.ui.tgui_theme", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
