using System.Globalization;
using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.UserInterface.Controls;
using Content.Shared.Atmos;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Windows.Atmos;

/// <summary>
///     The canister after tgui's Canister: a section for the canister itself (pressure, regulator, valve, port, and the
///         shielding KS14 adds), one for the holding tank, and the reagents the canister carries. tg's relabel and recolor
///         have no counterpart here. The canister's gauge reads up to the pressure it starts to fail at, tg's
///         <c>pressureLimit</c>, and the tank's to 15% past the pressure it starts to fail at, as tg's.
/// </summary>
public sealed class KsTguiGasCanisterWindow : FancyWindow, IKsGasCanisterWindow
{
    public event Action? TankEjectButtonPressed;
    public event Action? TankInsertButtonPressed;
    public event Action<float>? ReleasePressureSet;
    public event Action? ReleaseValveCloseButtonPressed;
    public event Action? ReleaseValveOpenButtonPressed;
    public event Action? ShieldToggleButtonPressed;

    private readonly KsTguiLabeledListItem _pressureItem;
    private readonly KsTguiRoundGauge _canisterGauge = MakeGauge();
    private readonly KsTguiRoundGauge _tankGauge = MakeGauge();
    private readonly KsTguiLabeledListItem _portItem;
    private readonly KsTguiLabeledListItem _labelItem;
    private readonly KsTguiLabeledListItem _tankPressureItem;
    private readonly KsTguiLabeledListItem _shieldChargeItem;
    private readonly KsTguiLabeledListItem _shieldLoadItem;
    private readonly KsTguiProgressBar _shieldBar;
    private readonly KsTguiLabeledList _shieldList;
    private readonly KsTguiLabeledList _tankList;
    private readonly Label _noTankLabel;

    private bool _valveOpen;
    private bool _hasTank;
    private bool _shieldEnabled;

    /// <summary>
    ///     tg's regulator knob: size 1.25, 5 kPa to the pixel.
    /// </summary>
    public KsTguiKnob ReleaseKnob { get; } = new() { KnobSize = 1.25f, Unit = Loc.GetString("ks-tgui-atmos-unit-kpa"), Step = 5f, StepPixelSize = 1f, ShowValue = true };
    public KsTguiButton ReleaseMaxButton { get; } = new() { Icon = KsTguiIcons.FastForward, Color = KsTguiColor.Transparent };
    public KsTguiButton ReleaseDefaultButton { get; } = new() { Icon = KsTguiIcons.Undo, Color = KsTguiColor.Transparent };
    public KsTguiButton ValveButton { get; } = new();
    public KsTguiButton ShieldButton { get; } = new();
    public KsTguiItemSlotButton EjectButton { get; } = new();

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
        _pressureItem.XamlChildren.Add(_canisterGauge);

        var regulatorItem = canisterList.AddItem(Loc.GetString("ks-tgui-atmos-regulator"));
        regulatorItem.XamlChildren.Add(new RegulatorControl(ReleaseKnob, ReleaseMaxButton, ReleaseDefaultButton));

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

        canisterSection.AddChild(canisterBody);
        root.AddChild(canisterSection);

        // The holding tank.
        var tankSection = new KsTguiSection { Title = Loc.GetString("ks-tgui-atmos-holding-tank"), Buttons = EjectButton, Fill = true };
        var tankBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _tankList = new KsTguiLabeledList();
        _labelItem = _tankList.AddItem(Loc.GetString("ks-tgui-atmos-tank-label"));
        _tankPressureItem = _tankList.AddItem(Loc.GetString("ks-tgui-atmos-pressure"));
        _tankPressureItem.XamlChildren.Add(_tankGauge);
        _noTankLabel = new Label { Text = Loc.GetString("ks-tgui-atmos-no-tank"), FontColorOverride = KsTguiPalettes.Average };
        tankBody.AddChild(_tankList);
        tankBody.AddChild(_noTankLabel);
        tankSection.AddChild(tankBody);
        root.AddChild(tankSection);

        ContentsContainer.AddChild(root);

        ShieldButton.Visible = false;
        ReleaseKnob.OnChanged += value => ReleasePressureSet?.Invoke(value);
        ReleaseMaxButton.OnPressed += _ => SetPressureAndSend(ReleaseKnob.MaxValue);
        ReleaseDefaultButton.OnPressed += _ => SetPressureAndSend(Math.Clamp(Atmospherics.OneAtmosphere, ReleaseKnob.MinValue, ReleaseKnob.MaxValue));
        ValveButton.OnPressed += _ =>
        {
            if (_valveOpen)
                ReleaseValveCloseButtonPressed?.Invoke();
            else
                ReleaseValveOpenButtonPressed?.Invoke();
        };
        ShieldButton.OnPressed += _ => ShieldToggleButtonPressed?.Invoke();
        EjectButton.EjectPressed += () => TankEjectButtonPressed?.Invoke();
        EjectButton.InsertPressed += () => TankInsertButtonPressed?.Invoke();

        SetValveVisual();
        SetShieldVisual();
        SetTankLabel(null);
    }

    public KsTguiRoundGauge CanisterGauge => _canisterGauge;

    public KsTguiRoundGauge TankGauge => _tankGauge;

    /// <summary>
    ///     tg's pressure gauge at size 1.75. Its ranges and alert come with the limits.
    /// </summary>
    private static KsTguiRoundGauge MakeGauge()
    {
        return new KsTguiRoundGauge
        {
            GaugeSize = 1.75f,
            MinValue = 0f,
            HorizontalAlignment = Control.HAlignment.Left,
            Format = FormatPressure,
        };
    }

    /// <summary>
    ///     tg's <c>formatPressure</c>: whole kPa, and megapascals from 10000 kPa, to three figures.
    /// </summary>
    private static string FormatPressure(float pressure)
    {
        if (pressure < 10000f)
            return Loc.GetString("comp-gas-canister-ui-pressure", ("pressure", Math.Round(pressure)));

        var megapascals = pressure / 1000f;
        return Loc.GetString(
            "ks-tgui-atmos-pressure-mpa",
            ("pressure", megapascals.ToString(megapascals < 100f ? "0.0" : "0", CultureInfo.CurrentCulture)));
    }

    private void SetPressureAndSend(float pressure)
    {
        ReleaseKnob.SetValue(pressure);
        ReleasePressureSet?.Invoke(pressure);
    }

    public void SetCanisterLabel(string label)
    {
        Title = label;
    }

    public void SetCanisterPressure(float pressure)
    {
        _canisterGauge.Value = pressure;
    }

    public void SetPortStatus(bool status)
    {
        _portItem.Text = Loc.GetString(status ? "comp-gas-canister-ui-port-connected" : "comp-gas-canister-ui-port-disconnected");
        _portItem.TextColor = status ? KsTguiPalettes.Good : KsTguiPalettes.Bad;
    }

    public void SetTankLabel(string? label)
    {
        _hasTank = label != null;
        EjectButton.Filled = _hasTank;
        _tankList.Visible = _hasTank;
        _noTankLabel.Visible = !_hasTank;
        _labelItem.Text = label;
        SetValveVisual();
    }

    public void SetTankPressure(float pressure)
    {
        _tankGauge.Value = pressure;
    }

    public void SetReleasePressureRange(float min, float max)
    {
        ReleaseKnob.MinValue = min;
        ReleaseKnob.MaxValue = max;
    }

    public void SetCanisterLimit(float pressureLimit)
    {
        var warning = pressureLimit * 0.7f;
        var danger = pressureLimit * 0.85f;
        SetDial(_canisterGauge, pressureLimit, warning, danger, warning);
    }

    public void SetTankLimits(float leakPressure, float fragmentPressure)
    {
        SetDial(_tankGauge, fragmentPressure * 1.15f, leakPressure, fragmentPressure, leakPressure);
    }

    /// <summary>
    ///     Green to <paramref name="warning"/>, yellow to <paramref name="danger"/>, and red from there to the end of the dial.
    /// </summary>
    private static void SetDial(KsTguiRoundGauge gauge, float maximum, float warning, float danger, float alertAfter)
    {
        gauge.MaxValue = maximum;
        gauge.AlertAfter = alertAfter;
        gauge.ClearRanges();
        gauge.AddRange(KsTguiColor.Good, 0f, warning);
        gauge.AddRange(KsTguiColor.Average, warning, danger);
        gauge.AddRange(KsTguiColor.Bad, danger, maximum);
    }

    public void SetReleasePressure(float pressure)
    {
        ReleaseKnob.SetValue(pressure);
        ReleaseMaxButton.Disabled = pressure >= ReleaseKnob.MaxValue;
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

    private void SetValveVisual()
    {
        // tg: the knob's ring is yellow while the valve is open.
        ReleaseKnob.Color = _valveOpen ? KsTguiColor.Yellow : null;
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

    /// <summary>
    ///     tg's regulator cell: the knob, and the fast-forward and undo buttons placed against it as
    ///         <c>position: absolute; right: -20px</c> at <c>top: -2px</c> and <c>16px</c>, so they sit just right of the knob,
    ///         the one above the other.
    /// </summary>
    private sealed class RegulatorControl : Control
    {
        /// <summary>
        ///     Where the buttons start, from the knob's left: its width and the gap tg's offsets leave.
        /// </summary>
        private const float ButtonGap = 7f;

        private const float MaxButtonTop = -2f;
        private const float DefaultButtonTop = 16f;

        private readonly KsTguiKnob _knob;
        private readonly KsTguiButton _maxButton;
        private readonly KsTguiButton _defaultButton;

        public RegulatorControl(KsTguiKnob knob, KsTguiButton maxButton, KsTguiButton defaultButton)
        {
            _knob = knob;
            _maxButton = maxButton;
            _defaultButton = defaultButton;
            HorizontalAlignment = HAlignment.Left;

            AddChild(knob);
            AddChild(maxButton);
            AddChild(defaultButton);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            _knob.Measure(Vector2.PositiveInfinity);
            _maxButton.Measure(Vector2.PositiveInfinity);
            _defaultButton.Measure(Vector2.PositiveInfinity);

            var buttonWidth = MathF.Max(_maxButton.DesiredSize.X, _defaultButton.DesiredSize.X);
            return new Vector2(_knob.DesiredSize.X + ButtonGap + buttonWidth, _knob.DesiredSize.Y);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            _knob.Arrange(UIBox2.FromDimensions(Vector2.Zero, _knob.DesiredSize));

            var left = _knob.DesiredSize.X + ButtonGap;
            _maxButton.Arrange(UIBox2.FromDimensions(new Vector2(left, MaxButtonTop), _maxButton.DesiredSize));
            _defaultButton.Arrange(UIBox2.FromDimensions(new Vector2(left, DefaultButtonTop), _defaultButton.DesiredSize));
            return finalSize;
        }
    }
}
