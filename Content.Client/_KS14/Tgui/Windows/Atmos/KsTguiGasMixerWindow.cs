using System.Globalization;
using Content.Client._KS14.Tgui.Controls;
using Content.Shared.Atmos;

namespace Content.Client._KS14.Tgui.Windows.Atmos;

/// <summary>
///     The gas mixer after tgui's AtmosMixer: power, output pressure, and the two node shares, each its own row.
/// </summary>
public sealed class KsTguiGasMixerWindow : KsTguiGasDeviceWindow, IKsGasMixerWindow
{
    public event Action<bool>? ToggleStatusButtonPressed;
    public event Action<string>? MixerOutputPressureChanged;
    public event Action<string>? MixerNodePercentageChanged;

    public bool NodeOneLastEdited { get; private set; } = true;

    public KsTguiNumberInput PressureInput { get; } = new() { Unit = Kpa, MaxValue = Atmospherics.MaxOutputPressure };
    public KsTguiNumberInput NodeOneInput { get; } = new() { Unit = "%", MaxValue = 100f };
    public KsTguiNumberInput NodeTwoInput { get; } = new() { Unit = "%", MaxValue = 100f };
    public KsTguiButton MaxButtonControl { get; } = MaxButton();

    public KsTguiGasMixerWindow() : base(width: 370f)
    {
        List.AddItem(Loc.GetString("ks-tgui-atmos-power")).XamlChildren.Add(Power.Button);

        var pressureRow = List.AddItem(Loc.GetString("ks-tgui-atmos-output-pressure"));
        pressureRow.XamlChildren.Add(PressureInput);
        pressureRow.Buttons = MaxButtonControl;

        List.AddItem(Loc.GetString("ks-tgui-atmos-main-node")).XamlChildren.Add(NodeOneInput);
        List.AddItem(Loc.GetString("ks-tgui-atmos-side-node")).XamlChildren.Add(NodeTwoInput);

        Power.Toggled += on => ToggleStatusButtonPressed?.Invoke(on);
        PressureInput.OnCommitted += value => MixerOutputPressureChanged?.Invoke(value.ToString(CultureInfo.CurrentCulture));
        MaxButtonControl.OnPressed += _ =>
        {
            PressureInput.SetValue(PressureInput.MaxValue);
            MixerOutputPressureChanged?.Invoke(PressureInput.MaxValue.ToString(CultureInfo.CurrentCulture));
        };

        NodeOneInput.OnCommitted += value =>
        {
            NodeOneLastEdited = true;
            MixerNodePercentageChanged?.Invoke(value.ToString(CultureInfo.CurrentCulture));
        };
        NodeTwoInput.OnCommitted += value =>
        {
            NodeOneLastEdited = false;
            MixerNodePercentageChanged?.Invoke(value.ToString(CultureInfo.CurrentCulture));
        };
    }

    public void SetOutputPressure(float pressure)
    {
        PressureInput.SetValue(pressure);
        MaxButtonControl.Disabled = pressure >= PressureInput.MaxValue;
    }

    public void SetNodePercentages(float nodeOne)
    {
        nodeOne *= 100f;
        NodeOneInput.SetValue(nodeOne);
        NodeTwoInput.SetValue(100f - nodeOne);
    }

    public void SetMixerStatus(bool enabled) => Power.SetOn(enabled);
}
