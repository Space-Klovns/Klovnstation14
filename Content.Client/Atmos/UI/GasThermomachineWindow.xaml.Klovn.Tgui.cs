using Content.Client._KS14.Tgui.Windows.Atmos;

namespace Content.Client.Atmos.UI;

// The members IKsGasThermomachineWindow asks of the stock window that it does not already have; the tgui window has its own.
public sealed partial class GasThermomachineWindow : IKsGasThermomachineWindow
{
    public event Action? ToggleRequested;
    public event Action<float>? TemperatureRequested;

    EntityUid IKsGasThermomachineWindow.Entity
    {
        set => Entity = value;
    }

    /// <summary>
    ///     Raises the interface's events from the stock controls, so the bound user interface can treat the stock and the
    ///         tgui window alike. Called by the constructor.
    /// </summary>
    private void KsHookRequests()
    {
        ToggleStatusButton.OnToggled += _ => ToggleRequested?.Invoke();
        TemperatureSpinbox.OnValueChanged += _ => TemperatureRequested?.Invoke(TemperatureSpinbox.Value);
    }

    public void SetTemperatureRange(float min, float max)
    {
        // The stock window has no presets.
    }
}
