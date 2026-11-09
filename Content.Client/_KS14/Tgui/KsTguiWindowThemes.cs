using Content.Client.Stylesheets;
using Content.Shared._KS14.Tgui;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     Puts a tgui window theme on a window: tgui's <c>&lt;Window theme="syndicate"&gt;</c>. A theme is a whole stylesheet
///         (<see cref="StylesheetManager.KsGetThemedSheet"/>) set as the window's own <see cref="Control.Stylesheet"/>, which
///         reaches everything inside it and nothing outside. The wanted theme is kept on the control, so a rebuilt sheet, the
///         tgui toggle and a window opened again all find it. Under Baron it does nothing.
/// </summary>
public static class KsTguiWindowThemes
{
    private static readonly AttachedProperty<KsTguiWindowTheme?> ThemeProperty =
        AttachedProperty<KsTguiWindowTheme?>.Create("KsTguiWindowTheme", typeof(KsTguiWindowThemes));

    /// <summary>
    ///     The theme this control was given, whether or not it is showing.
    /// </summary>
    public static KsTguiWindowTheme? GetKsTheme(this Control control)
    {
        return control.GetValue(ThemeProperty);
    }

    /// <summary>
    ///     Gives a window a theme, or takes it back with null. Any <see cref="Control"/> will do, but it is a window that
    ///         is meant, since a stylesheet replaces its parent's.
    /// </summary>
    public static void SetKsTheme(this Control control, KsTguiWindowTheme? theme)
    {
        control.SetValue(ThemeProperty, theme);
        Reapply(control);
    }

    /// <summary>
    ///     Sets the control's sheet to the one its theme gives under the current style.
    /// </summary>
    public static void Reapply(Control control)
    {
        if (control.GetValue(ThemeProperty) is not { } theme)
            return;

        control.Stylesheet = ThemedSheet(theme);
    }

    /// <summary>
    ///     The sheet of the theme of the nearest control above this one that has one, if any: what a popup that belongs to
    ///         a themed window should wear, since the popup layers are not inside it.
    /// </summary>
    public static Robust.Client.UserInterface.Stylesheet? FindThemedSheet(Control? control)
    {
        for (var current = control; current != null; current = current.Parent)
        {
            if (current.GetValue(ThemeProperty) is { } theme)
                return ThemedSheet(theme);
        }

        return null;
    }

    /// <summary>
    ///     Null where the control should take the UI's: the default theme is the UI's own, and Baron has no themes.
    /// </summary>
    private static Robust.Client.UserInterface.Stylesheet? ThemedSheet(KsTguiWindowTheme theme)
    {
        if (theme == KsTguiWindowTheme.Nanotrasen || !KsTguiTheme.ReadEnabled())
            return null;

        return IoCManager.Resolve<IStylesheetManager>() is StylesheetManager manager ? manager.KsGetThemedSheet(theme) : null;
    }
}
