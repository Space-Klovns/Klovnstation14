using Content.Client._KS14.Tgui;

namespace Content.Client.Stylesheets.Stylesheets;

public partial class NanotrasenStylesheet
{
    /// <summary>
    ///     Whether this sheet is built in the tgui style. Set by initializer, so it is in place before any base constructor,
    ///         and an instance field because several clients share a process in tests.
    /// </summary>
    private readonly bool _ksTgui = KsTguiTheme.ReadEnabled();
}
