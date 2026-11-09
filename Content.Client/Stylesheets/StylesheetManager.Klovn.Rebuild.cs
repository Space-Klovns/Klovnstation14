using Content.Client._KS14.Tgui;
using Content.Shared._KS14.Tgui;
using Robust.Client.UserInterface;

namespace Content.Client.Stylesheets;

// Partial so the fork can rebuild the Nanotrasen sheet at runtime (lobby font switching, the tgui toggle).
public sealed partial class StylesheetManager
{
    /// <summary>
    ///     Rebuilds <see cref="SheetNanotrasen"/> and makes it the UI's stylesheet. Windows that hold
    ///     <see cref="SheetSystem"/> are not affected; only roots without their own stylesheet pick the new one up.
    /// </summary>
    public void KsRebuildNanotrasen()
    {
        _ksThemedSheets.Clear();

        var baseSheet = new Stylesheets.NanotrasenStylesheet(new BaseStylesheet.NoConfig(), this);
        Stylesheets[baseSheet.StylesheetName] = baseSheet.Stylesheet;
        SheetNanotrasen = baseSheet.Stylesheet;
        _userInterfaceManager.Stylesheet = SheetNanotrasen;
    }

    /// <summary>
    ///     Rebuilds both live sheets. Controls that hold the old <see cref="SheetSystem"/> keep it until they're moved off it,
    ///         so a caller that needs them restyled has to do that.
    /// </summary>
    public void KsRebuildAll()
    {
        _ksThemedSheets.Clear();

        var systemSheet = new Stylesheets.SystemStylesheet(new BaseStylesheet.NoConfig(), this);
        Stylesheets[systemSheet.StylesheetName] = systemSheet.Stylesheet;
        SheetSystem = systemSheet.Stylesheet;

        KsRebuildNanotrasen();
    }

    private readonly Dictionary<KsTguiWindowTheme, Stylesheet> _ksThemedSheets = new();

    /// <summary>
    ///     The Nanotrasen sheet built for a window theme, which a window sets as its own <c>Control.Stylesheet</c>. Built on
    ///         first use and held until the sheets are rebuilt. A sheet is whole, since a control's stylesheet replaces its
    ///         parent's, and not a layer over it.
    /// </summary>
    public Stylesheet KsGetThemedSheet(KsTguiWindowTheme theme)
    {
        if (!_ksThemedSheets.TryGetValue(theme, out var sheet))
        {
            sheet = new Stylesheets.NanotrasenStylesheet(new KsTguiThemeConfig(theme), this).Stylesheet;
            _ksThemedSheets[theme] = sheet;
        }

        return sheet;
    }
}
