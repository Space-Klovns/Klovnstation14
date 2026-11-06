using Content.Shared.Atmos;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Atmos.Monitor.Components;
using Robust.Client.UserInterface;

namespace Content.Client.Atmos.Monitor.UI;

public sealed partial class AirAlarmBoundUserInterface : BoundUserInterface
{
    private Content.Client._KS14.Tgui.Windows.AirAlarm.IKsAirAlarmWindow? _window; /* KS14: AirAlarmWindow -> either window */

    public AirAlarmBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = Content.Client._KS14.Tgui.KsTguiTheme.ReadEnabled() /* KS14: tgui window */
            ? this.CreateWindow<Content.Client._KS14.Tgui.Windows.AirAlarm.KsTguiAirAlarmWindow>()
            : this.CreateWindow<AirAlarmWindow>();
        _window.SetEntity(Owner);
        RefreshAccess(); // KS14: tgui window disables its controls without access

        _window.AtmosDeviceDataChanged += OnDeviceDataChanged;
        _window.AtmosDeviceDataCopied += OnDeviceDataCopied;
        _window.AtmosAlarmThresholdChanged += OnThresholdChanged;
        _window.AirAlarmModeChanged += OnAirAlarmModeChanged;
        _window.AutoModeChanged += OnAutoModeChanged;
        _window.ResyncAllRequested += ResyncAllDevices;
    }

    private void ResyncAllDevices()
    {
        SendMessage(new AirAlarmResyncAllDevicesMessage());
    }

    private void OnDeviceDataChanged(string address, IAtmosDeviceData data)
    {
        SendMessage(new AirAlarmUpdateDeviceDataMessage(address, data));
    }

    private void OnDeviceDataCopied(IAtmosDeviceData data)
    {
        SendMessage(new AirAlarmCopyDeviceDataMessage(data));
    }

    private void OnAirAlarmModeChanged(AirAlarmMode mode)
    {
        SendMessage(new AirAlarmUpdateAlarmModeMessage(mode));
    }

    private void OnAutoModeChanged(bool enabled)
    {
        SendMessage(new AirAlarmUpdateAutoModeMessage(enabled));
    }

    private void OnThresholdChanged(string address, AtmosMonitorThresholdType type, AtmosAlarmThreshold threshold, Gas? gas = null)
    {
        SendMessage(new AirAlarmUpdateAlarmThresholdMessage(address, type, threshold, gas));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not AirAlarmUIState cast || _window == null)
        {
            return;
        }

        RefreshAccess(); // KS14: the player's ID may have changed
        _window.UpdateState(cast);
    }

    // KS14 start: same check as the APC's, since the server only answers a denied change with a popup
    private void RefreshAccess()
    {
        var hasAccess = false;
        if (PlayerManager.LocalEntity is { } localEntity)
            hasAccess = EntMan.System<Content.Shared.Access.Systems.AccessReaderSystem>().IsAllowed(localEntity, Owner);

        _window?.SetAccessEnabled(hasAccess);
    }
    // KS14 end

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _window?.Dispose();
    }
}
