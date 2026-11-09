using Content.Client._KS14.Tgui;

namespace Content.Client.Stylesheets.Stylesheets;

public partial class NanotrasenStylesheet
{
    /// <summary>
    ///     Whether this sheet is built in the tgui style. Set by initializer, so it is in place before any base constructor,
    ///         and an instance field because several clients share a process in tests.
    /// </summary>
    private readonly bool _ksTgui = KsTguiTheme.ReadEnabled();

    /// <summary>
    ///     The window theme this sheet is built for: the default one, or the one its config names. Read as the sheetlets are
    ///         built, so it is set at the top of the constructor.
    /// </summary>
    private KsTguiThemePalette _ksPalette = KsTguiThemePalette.Nanotrasen;
}
