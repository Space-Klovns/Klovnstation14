using Content.Client._KS14.Tgui.Windows.AirAlarm;

namespace Content.Client.Atmos.Monitor.UI;

/// <summary>
///     Lets the BUI hold either the stock window or the tgui one behind <see cref="IKsAirAlarmWindow"/>.
/// </summary>
public sealed partial class AirAlarmWindow : IKsAirAlarmWindow
{
    // The stock window has never shown the lack of access; the server refuses the change and says so in a popup.
    public void SetAccessEnabled(bool hasAccess)
    {
    }
}
