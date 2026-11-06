using System.Numerics;
using Content.Client._KS14.Fluidmos;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.UserInterface.Controls;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Windows.Atmos;

/// <summary>
///     The canister after tgui's Canister: a section for the canister itself (pressure, regulator, valve, port, and the
///         shielding KS14 adds), one for the holding tank, and the reagents the canister carries. tg's relabel and recolor
///         have no counterpart here, and its pressure gauges are plain readings, since a canister's burst pressure is not
///         something the client is told.
/// </summary>
public sealed class KsTguiGasCanisterWindow : FancyWindow, IKsGasCanisterWindow
{
    public event Action? TankEjectButtonPressed;
    public event Action<float>? ReleasePressureSet;
    public event Action? ReleaseValveCloseButtonPressed;
    public event Action? ReleaseValveOpenButtonPressed;
    public event Action? ShieldToggleButtonPressed;

    private readonly KsTguiLabeledListItem _pressureItem;
    private readonly KsTguiLabeledListItem _portItem;
    private readonly KsTguiLabeledListItem _labelItem;
    private readonly KsTguiLabeledListItem _tankPressureItem;
    private readonly KsTguiLabeledListItem _shieldChargeItem;
    private readonly KsTguiLabeledListItem _shieldLoadItem;
    private readonly KsTguiProgressBar _shieldBar;
    private readonly KsTguiLabeledList _shieldList;
    private readonly KsTguiLabeledList _tankList;
    private readonly Label _noTankLabel;
    private readonly KsFluidListControl _fluidList;

    private bool _valveOpen;
    private bool _hasTank;
    private bool _shieldEnabled;

    public KsTguiNumberInput ReleaseInput { get; } = new() { Unit = Loc.GetString("ks-tgui-atmos-unit-kpa"), Step = 5f, WidthPixels = 75f };
    public KsTguiButton ReleaseMaxButton { get; } = new() { Icon = KsTguiIcons.FastForward, Color = KsTguiColor.Transparent };
    public KsTguiButton ReleaseDefaultButton { get; } = new() { Icon = KsTguiIcons.Undo, Color = KsTguiColor.Transparent };
    public KsTguiButton ValveButton { get; } = new();
    public KsTguiButton ShieldButton { get; } = new();
    public KsTguiButton EjectButton { get; } = new() { Icon = KsTguiIcons.Eject, Text = Loc.GetString("comp-gas-canister-ui-holding-tank-eject") };

    public KsTguiGasCanisterWindow()
    {
        MinWidth = 350f;
        SetWidth = 350f; // The height is the contents'.

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
            Margin = new Thickness(6f),
            VerticalExpand = true,
        };

        // The canister itself.
        var canisterSection = new KsTguiSection { Title = Loc.GetString("ks-tgui-atmos-canister"), Buttons = ShieldButton };
        var canisterBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        var canisterList = new KsTguiLabeledList();
        _pressureItem = canisterList.AddItem(Loc.GetString("ks-tgui-atmos-pressure"));

        var regulatorItem = canisterList.AddItem(Loc.GetString("ks-tgui-atmos-regulator"));
        regulatorItem.XamlChildren.Add(ReleaseInput);
        var regulatorButtons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        regulatorButtons.AddChild(ReleaseDefaultButton);
        regulatorButtons.AddChild(ReleaseMaxButton);
        regulatorItem.Buttons = regulatorButtons;

        canisterList.AddItem(Loc.GetString("ks-tgui-atmos-valve")).Buttons = ValveButton;
        ValveButton.MinWidth = 72f;
        _portItem = canisterList.AddItem(Loc.GetString("ks-tgui-atmos-port"));
        canisterBody.AddChild(canisterList);

        _shieldBar = new KsTguiProgressBar { HorizontalExpand = true };
        _shieldBar.SetRanges(
            (KsTguiColor.Bad, 0f, 0.25f),
            (KsTguiColor.Average, 0.25f, 0.5f),
            (KsTguiColor.Good, 0.5f, 1.01f));
        _shieldList = new KsTguiLabeledList { Visible = false };
        _shieldChargeItem = _shieldList.AddItem(Loc.GetString("ks-tgui-atmos-shield-charge"));
        _shieldChargeItem.XamlChildren.Add(_shieldBar);
        _shieldLoadItem = _shieldList.AddItem(Loc.GetString("gas-canister-shielding-ui-header"));
        canisterBody.AddChild(_shieldList);

        _fluidList = new KsFluidListControl();
        canisterBody.AddChild(_fluidList);

        canisterSection.AddChild(canisterBody);
        root.AddChild(canisterSection);

        // The holding tank.
        var tankSection = new KsTguiSection { Title = Loc.GetString("ks-tgui-atmos-holding-tank"), Buttons = EjectButton, Fill = true };
        var tankBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _tankList = new KsTguiLabeledList();
        _labelItem = _tankList.AddItem(Loc.GetString("ks-tgui-atmos-tank-label"));
        _tankPressureItem = _tankList.AddItem(Loc.GetString("ks-tgui-atmos-pressure"));
        _noTankLabel = new Label { Text = Loc.GetString("ks-tgui-atmos-no-tank"), FontColorOverride = KsTguiPalettes.Average };
        tankBody.AddChild(_tankList);
        tankBody.AddChild(_noTankLabel);
        tankSection.AddChild(tankBody);
        root.AddChild(tankSection);

        ContentsContainer.AddChild(root);

        ShieldButton.Visible = false;
        ReleaseInput.OnCommitted += value => ReleasePressureSet?.Invoke(value);
        ReleaseMaxButton.OnPressed += _ => SetPressureAndSend(ReleaseInput.MaxValue);
        ReleaseDefaultButton.OnPressed += _ => SetPressureAndSend(Math.Clamp(Atmospherics.OneAtmosphere, ReleaseInput.MinValue, ReleaseInput.MaxValue));
        ValveButton.OnPressed += _ =>
        {
            if (_valveOpen)
                ReleaseValveCloseButtonPressed?.Invoke();
            else
                ReleaseValveOpenButtonPressed?.Invoke();
        };
        ShieldButton.OnPressed += _ => ShieldToggleButtonPressed?.Invoke();
        EjectButton.OnPressed += _ => TankEjectButtonPressed?.Invoke();

        SetValveVisual();
        SetShieldVisual();
        SetTankLabel(null);
    }

    public KsFluidListControl FluidList => _fluidList;

    private void SetPressureAndSend(float pressure)
    {
        ReleaseInput.SetValue(pressure);
        ReleasePressureSet?.Invoke(pressure);
    }

    public void SetCanisterLabel(string label)
    {
        Title = label;
    }

    public void SetCanisterPressure(float pressure)
    {
        _pressureItem.Text = Loc.GetString("comp-gas-canister-ui-pressure", ("pressure", Math.Round(pressure)));
    }

    public void SetPortStatus(bool status)
    {
        _portItem.Text = Loc.GetString(status ? "comp-gas-canister-ui-port-connected" : "comp-gas-canister-ui-port-disconnected");
        _portItem.TextColor = status ? KsTguiPalettes.Good : KsTguiPalettes.Bad;
    }

    public void SetTankLabel(string? label)
    {
        _hasTank = label != null;
        EjectButton.Visible = _hasTank;
        _tankList.Visible = _hasTank;
        _noTankLabel.Visible = !_hasTank;
        _labelItem.Text = label;
        SetValveVisual();
    }

    public void SetTankPressure(float pressure)
    {
        _tankPressureItem.Text = Loc.GetString("comp-gas-canister-ui-pressure", ("pressure", Math.Round(pressure)));
    }

    public void SetReleasePressureRange(float min, float max)
    {
        ReleaseInput.MinValue = min;
        ReleaseInput.MaxValue = max;
    }

    public void SetReleasePressure(float pressure)
    {
        ReleaseInput.SetValue(pressure);
        ReleaseMaxButton.Disabled = pressure >= ReleaseInput.MaxValue;
    }

    public void SetReleaseValve(bool valve)
    {
        _valveOpen = valve;
        SetValveVisual();
    }

    public void SetShieldSettingsVisible(bool visible)
    {
        ShieldButton.Visible = visible;
        _shieldList.Visible = visible;
    }

    public void UpdateShieldState(bool enabled, float dischargeRate, float lastCharge, float maxCharge)
    {
        _shieldEnabled = enabled;
        ShieldButton.Disabled = lastCharge < dischargeRate;
        _shieldLoadItem.Text = Loc.GetString("gas-canister-shielding-ui-wattage", ("wattage", dischargeRate));
        _shieldBar.Value = maxCharge > 0f ? lastCharge / maxCharge : 0f;
        SetShieldVisual();
    }

    public void SetKsFluid(KsFluidEntry[]? entries)
    {
        _fluidList.SetEntries(entries);
    }

    private void SetValveVisual()
    {
        ValveButton.Text = Loc.GetString(_valveOpen ? "ks-tgui-atmos-valve-open" : "ks-tgui-atmos-valve-closed");
        ValveButton.Color = !_valveOpen
            ? KsTguiColor.Default
            : _hasTank ? KsTguiColor.Caution : KsTguiColor.Danger;
        EjectButton.Color = _valveOpen ? KsTguiColor.Danger : KsTguiColor.Default;
    }

    private void SetShieldVisual()
    {
        ShieldButton.Selected = _shieldEnabled;
        ShieldButton.Icon = _shieldEnabled ? KsTguiIcons.Power : KsTguiIcons.Times;
        ShieldButton.Text = Loc.GetString(_shieldEnabled ? "ks-tgui-atmos-shielding-on" : "ks-tgui-atmos-shielding-off");
    }
}
