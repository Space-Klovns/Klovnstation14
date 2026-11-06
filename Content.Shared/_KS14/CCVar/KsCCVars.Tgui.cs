using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Draw the UI in the tgui style rather than Baron. Read by the client UI only, which restyles live when it changes.
    /// </summary>
    public static readonly CVarDef<bool> TguiStyle =
        CVarDef.Create("klovn.ui.tgui", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     The tgui chat panel's pages, one per <c>;</c>: <c>id,channels,hideUnread,name</c>. Read and written by the client UI only.
    /// </summary>
    public static readonly CVarDef<string> TguiChatPages =
        CVarDef.Create("klovn.ui.tgui.chat_pages", "", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     The tgui chat panel's message font size, in points. tgui-panel's own default is 13, at line height 1.2.
    /// </summary>
    public static readonly CVarDef<int> TguiChatFontSize =
        CVarDef.Create("klovn.ui.tgui.chat_font_size", 13, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Where the floating say window was dragged to, as <c>x,y</c> fractions of the screen; empty is its default spot.
    /// </summary>
    public static readonly CVarDef<string> TguiSayPosition =
        CVarDef.Create("klovn.ui.tgui.say_position", "", CVar.CLIENTONLY | CVar.ARCHIVE);
}
