namespace Content.Shared._KS14.Tgui;

/// <summary>
///     A tgui window theme: tgui's <c>theme="..."</c> on a <c>Window</c>. Shared so that data, such as a store's
///         <c>ksUiTheme</c>, can name one. The client turns it into a palette and a stylesheet.
/// </summary>
public enum KsTguiWindowTheme : byte
{
    /// <summary>
    ///     tgui's default theme, which tg calls nanotrasen.
    /// </summary>
    Nanotrasen,

    Syndicate,
    Wizard,
}
