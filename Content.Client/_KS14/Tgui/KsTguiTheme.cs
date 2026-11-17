using Content.Client._KS14.BaronUI;
using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Fonts;
using Content.Shared._KS14.CCVar;
using Robust.Client.ResourceManagement;
using Robust.Shared.Configuration;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     The stylesheets' side of the tgui toggle. A stylesheet has no injection of its own for these, so this resolves what
///         it needs while one is being built.
/// </summary>
public static class KsTguiTheme
{
    public static bool ReadEnabled()
    {
        return IoCManager.Resolve<IConfigurationManager>().GetCVar(KsCCVars.TguiStyle);
    }

    /// <summary>
    ///     Whether a bound interface opens its ported tgui window rather than the stock one. Both themes open
    ///         the ported windows, Baron skinning them through its own rules (<c>KsBaronUiSheetlet.Tgui.cs</c>), so one
    ///         window carries every feature. The stock windows are kept, unopened, so upstream merges stay small.
    /// </summary>
    /// <remarks>
    ///     Only the choice of window reads this. How a window looks or feels still follows <see cref="ReadEnabled"/>.
    /// </remarks>
    public static bool UsePortedWindows()
    {
        return true;
    }

    /// <summary>
    ///     The sheetlet that fills Baron's slot in a stylesheet.
    /// </summary>
    public static Type ThemeSheetlet(bool enabled)
    {
        return enabled ? typeof(KsTguiSheetlet) : typeof(KsBaronUiSheetlet);
    }

    /// <summary>
    ///     The stack behind <see cref="BaseStylesheet.BaseFont"/>, so that everything asking it for a font follows the theme.
    /// </summary>
    public static NotoFontFamilyStack CreateBaseFont(IResourceCache resourceCache, bool enabled)
    {
        var stack = new NotoFontFamilyStack(resourceCache);
        if (enabled)
            stack.KsFontOverride = IoCManager.Resolve<KsTguiFonts>().GetFont;

        return stack;
    }
}
