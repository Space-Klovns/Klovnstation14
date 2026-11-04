namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     tgui's power toggle, used by every atmos device: <c>power-off</c> "On" in green when on, <c>times</c> "Off" when off.
/// </summary>
public static class KsTguiPowerButton
{
    public static void Apply(KsTguiButton button, bool on)
    {
        button.Icon = on ? KsTguiIconKind.PowerOff : KsTguiIconKind.Times;
        button.Text = Loc.GetString(on ? "ks-tgui-on" : "ks-tgui-off");
        button.Selected = on;
    }
}
