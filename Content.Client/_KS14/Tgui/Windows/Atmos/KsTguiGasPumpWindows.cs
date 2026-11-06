using System.Globalization;
using System.Numerics;
using Content.Client._KS14.Atmos.UI;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.UserInterface.Controls;
using Content.Shared.Atmos;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Windows.Atmos;

/// <summary>
///     Shared body of the gas pump, filter and mixer windows after tgui's AtmosPump: one section holding a labeled list of
///         the device's rows, and the heat footnote.
/// </summary>
public abstract class KsTguiGasDeviceWindow : FancyWindow
{
    protected readonly KsTguiPowerToggle Power = new();
    protected readonly KsTguiLabeledList List = new();
    protected readonly KsTguiSection Section;
    protected readonly BoxContainer RootBox;
    protected readonly KsPumpHeatFootnote HeatFootnote = new();

    protected KsTguiGasDeviceWindow(float width, string? sectionTitle = null)
    {
        MinSize = new Vector2(width, 0f);
        SetSize = new Vector2(width, 0f);

        RootBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
            Margin = new Thickness(6f),
            VerticalExpand = true,
        };

        Section = new KsTguiSection { Title = sectionTitle, Fill = true };
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        body.AddChild(List);
        body.AddChild(HeatFootnote);
        Section.AddChild(body);
        RootBox.AddChild(Section);
        ContentsContainer.AddChild(RootBox);
    }

    public EntityUid HeatDevice
    {
        set => HeatFootnote.Device = value;
    }

    public KsPumpHeatFootnote Footnote => HeatFootnote;

    public KsTguiButton PowerButton => Power.Button;

    protected static string Kpa => Loc.GetString("ks-tgui-atmos-unit-kpa");

    protected static KsTguiButton MaxButton()
    {
        return new KsTguiButton { Icon = KsTguiIcons.Plus, Text = Loc.GetString("comp-gas-pump-ui-pump-set-max") };
    }
}

/// <summary>
///     The gas pressure pump after tgui's AtmosPump (its pressure form).
/// </summary>
public sealed class KsTguiGasPressurePumpWindow : KsTguiGasDeviceWindow, IKsGasPressurePumpWindow
{
    public event Action<bool>? ToggleStatusButtonPressed;
    public event Action<float>? PumpOutputPressureChanged;

    public KsTguiNumberInput PressureInput { get; } = new() { Unit = Kpa, MaxValue = Atmospherics.MaxOutputPressure };
    public KsTguiButton MaxButtonControl { get; } = MaxButton();

    public KsTguiGasPressurePumpWindow() : base(width: 340f)
    {
        List.AddItem(Loc.GetString("ks-tgui-atmos-power")).XamlChildren.Add(Power.Button);

        var row = List.AddItem(Loc.GetString("ks-tgui-atmos-output-pressure"));
        row.XamlChildren.Add(PressureInput);
        row.Buttons = MaxButtonControl;

        Power.Toggled += on => ToggleStatusButtonPressed?.Invoke(on);
        PressureInput.OnCommitted += value => PumpOutputPressureChanged?.Invoke(value);
        MaxButtonControl.OnPressed += _ =>
        {
            PressureInput.SetValue(PressureInput.MaxValue);
            PumpOutputPressureChanged?.Invoke(PressureInput.MaxValue);
        };
    }

    public float MaxPressure
    {
        get => PressureInput.MaxValue;
        set
        {
            PressureInput.MaxValue = value;
            RefreshMax();
        }
    }

    public void SetOutputPressure(float pressure)
    {
        PressureInput.SetValue(pressure);
        RefreshMax();
    }

    public void SetPumpStatus(bool enabled) => Power.SetOn(enabled);

    private void RefreshMax()
    {
        MaxButtonControl.Disabled = PressureInput.Value >= PressureInput.MaxValue;
    }
}

/// <summary>
///     The volume pump after tgui's AtmosPump (its transfer rate form).
/// </summary>
public sealed class KsTguiGasVolumePumpWindow : KsTguiGasDeviceWindow, IKsGasVolumePumpWindow
{
    public event Action<bool>? ToggleStatusButtonPressed;
    public event Action<string>? PumpTransferRateChanged;

    public KsTguiNumberInput RateInput { get; } = new() { Unit = Loc.GetString("ks-tgui-atmos-unit-ls"), MaxValue = Atmospherics.MaxTransferRate };
    public KsTguiButton MaxButtonControl { get; } = MaxButton();

    public KsTguiGasVolumePumpWindow() : base(width: 340f)
    {
        List.AddItem(Loc.GetString("ks-tgui-atmos-power")).XamlChildren.Add(Power.Button);

        var row = List.AddItem(Loc.GetString("ks-tgui-atmos-transfer-rate"));
        row.XamlChildren.Add(RateInput);
        row.Buttons = MaxButtonControl;

        Power.Toggled += on => ToggleStatusButtonPressed?.Invoke(on);
        RateInput.OnCommitted += value => PumpTransferRateChanged?.Invoke(value.ToString(CultureInfo.CurrentCulture));
        MaxButtonControl.OnPressed += _ =>
        {
            RateInput.SetValue(RateInput.MaxValue);
            PumpTransferRateChanged?.Invoke(RateInput.MaxValue.ToString(CultureInfo.CurrentCulture));
        };
    }

    public void SetTransferRate(float rate)
    {
        RateInput.SetValue(rate);
        MaxButtonControl.Disabled = rate >= RateInput.MaxValue;
    }

    public void SetPumpStatus(bool enabled) => Power.SetOn(enabled);
}
