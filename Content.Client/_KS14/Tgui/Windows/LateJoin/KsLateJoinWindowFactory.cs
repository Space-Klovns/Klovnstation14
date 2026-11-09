using Content.Client.LateJoin;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._KS14.Tgui.Windows.LateJoin;

/// <summary>
///     Picks the late join window for the active theme: tgui's job selection, or the stock list.
/// </summary>
public static class KsLateJoinWindowFactory
{
    public static BaseWindow Create()
    {
        return KsTguiTheme.ReadEnabled() ? new KsTguiLateJoinWindow() : new LateJoinGui();
    }
}
