using Content.Client._KS14.UserInterface.Tgui;
using Content.Shared._KS14.CCVar;
using Robust.Shared.Configuration;

namespace Content.Client.UserInterface.Controls;

public partial class FancyWindow
{
    /// <summary>
    ///     Under the global tgui restyle the header is a flat tgui title bar, so the margins that fit the title inside the
    ///         Baron frame's texture would leave it 60px tall. A margin is layout, not style, so it is set here; windows
    ///         already open keep their spacing until reopened when the theme is switched.
    /// </summary>
    private void KsApplyTguiTitleBar()
    {
        if (!IoCManager.Resolve<IConfigurationManager>().GetCVar(KsCCVars.TguiTheme))
            return;

        if (WindowTitle.Parent is not { } titleRow)
            return;

        titleRow.Margin = new Thickness(12f, 0f, 6f, 0f);
        titleRow.MinHeight = KsTguiTheme.TitleBarHeight;
    }
}
