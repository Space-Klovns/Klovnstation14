using System.Globalization;
using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.UserInterface.Controls;
using Content.Client.Power.Components;
using Content.Shared.Atmos;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui.Windows.Atmos;

/// <summary>
///     The heater and freezer after tgui's ThermoMachine: a Status section with the gas it is working on, then power in the
///         header of a Controls section, the target temperature, and presets for the lowest, room and highest temperature
///         the machine can be set to.
/// </summary>
public sealed partial class KsTguiGasThermomachineWindow : FancyWindow, IKsGasThermomachineWindow
{
    [Dependency] private IEntityManager _entityManager = default!;

    public event Action? ToggleRequested;
    public event Action<float>? TemperatureRequested;

    private readonly KsTguiPowerToggle _power = new();
    private float _minTemperature;
    private float _maxTemperature = float.MaxValue;
    private EntityUid _entity;

    public KsTguiButton PowerButton => _power.Button;

    public KsTguiNumberInput TemperatureInput { get; } = new() { Unit = "K", Format = "0.#", Step = 5f, StepPixelSize = 3f, WidthPixels = 62f, TickWhileDragging = true, Animated = true };
    public KsTguiAnimatedNumber GasTemperature { get; } = new() { Format = temperature => temperature.ToString("F2", CultureInfo.CurrentCulture) + " K" };
    public KsTguiAnimatedNumber GasPressure { get; } = new()
    {
        Format = pressure => pressure.ToString("F2", CultureInfo.CurrentCulture) + " " + Loc.GetString("ks-tgui-atmos-unit-kpa"),
    };

    public KsTguiButton MinimumButton { get; } = new() { Icon = KsTguiIcons.FastBackward, ToolTip = Loc.GetString("ks-tgui-atmos-preset-min") };
    public KsTguiButton RoomButton { get; } = new() { Icon = KsTguiIcons.Sync, ToolTip = Loc.GetString("ks-tgui-atmos-preset-room") };
    public KsTguiButton MaximumButton { get; } = new() { Icon = KsTguiIcons.FastForward, ToolTip = Loc.GetString("ks-tgui-atmos-preset-max") };

    public EntityUid Entity
    {
        set => _entity = value;
    }

    public KsTguiGasThermomachineWindow()
    {
        IoCManager.InjectDependencies(this);

        MinWidth = 300f;
        SetWidth = 300f; // The height is the contents'.

        var statusSection = new KsTguiSection { Title = Loc.GetString("ks-tgui-atmos-status") };
        var statusList = new KsTguiLabeledList();
        statusList.AddItem(Loc.GetString("ks-tgui-atmos-temperature")).XamlChildren.Add(GasTemperature);
        statusList.AddItem(Loc.GetString("ks-tgui-atmos-pressure")).XamlChildren.Add(GasPressure);
        statusSection.AddChild(statusList);

        var section = new KsTguiSection
        {
            Title = Loc.GetString("ks-tgui-atmos-controls"),
            Buttons = _power.Button,
            Fill = true,
        };
        var list = new KsTguiLabeledList();
        list.AddItem(Loc.GetString("ks-tgui-atmos-target-temperature")).XamlChildren.Add(TemperatureInput);

        var presets = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 2 };
        presets.AddChild(MinimumButton);
        presets.AddChild(RoomButton);
        presets.AddChild(MaximumButton);
        list.AddItem(Loc.GetString("ks-tgui-atmos-presets")).Buttons = presets;
        section.AddChild(list);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
            Margin = new Thickness(6f),
            VerticalExpand = true,
        };
        root.AddChild(statusSection);
        root.AddChild(section);
        ContentsContainer.AddChild(root);

        _power.Toggled += _ => ToggleRequested?.Invoke();
        TemperatureInput.OnCommitted += value => TemperatureRequested?.Invoke(value);
        MinimumButton.OnPressed += _ => Request(_minTemperature);
        RoomButton.OnPressed += _ => Request(Math.Clamp(Atmospherics.T20C, _minTemperature, _maxTemperature));
        MaximumButton.OnPressed += _ => Request(_maxTemperature);
    }

    private void Request(float temperature)
    {
        TemperatureInput.SetValue(temperature);
        TemperatureRequested?.Invoke(temperature);
    }

    public void SetActive(bool active)
    {
        _power.SetOn(active);
    }

    public void SetTemperature(float temperature)
    {
        TemperatureInput.SetValue(temperature);
        MinimumButton.Disabled = MathHelper.CloseTo(temperature, _minTemperature, 0.09);
        MaximumButton.Disabled = MathHelper.CloseTo(temperature, _maxTemperature, 0.09);
        RoomButton.Disabled = MathHelper.CloseTo(temperature, Math.Clamp(Atmospherics.T20C, _minTemperature, _maxTemperature), 0.09);
    }

    public void SetTemperatureRange(float min, float max)
    {
        _minTemperature = min;
        _maxTemperature = max;
        TemperatureInput.MinValue = min;
        TemperatureInput.MaxValue = max;
    }

    public void SetGasStatus(float temperature, float pressure)
    {
        GasTemperature.Value = temperature;
        GasPressure.Value = pressure;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // As the stock window does: power changes from outside the window reach it here.
        if (_entityManager.TryGetComponent(_entity, out ApcPowerReceiverComponent? receiver))
            SetActive(!receiver.PowerDisabled);
    }
}
