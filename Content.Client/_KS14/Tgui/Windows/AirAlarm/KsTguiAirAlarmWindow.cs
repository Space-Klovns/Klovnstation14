using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.UserInterface.Controls;
using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Atmos.Monitor.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Temperature;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Tgui.Windows.AirAlarm;

/// <summary>
///     The air alarm window laid out after tgui's AirAlarm: an Air Status section on top, and below it a section that
///         swaps between the control screens (Air Controls, Vent Controls, Scrubber Controls, Operating Mode, Alarm
///         Thresholds) behind a Back button. tg's area alarm trigger, sensor disconnect and per-threshold reset have no
///         SS14 counterpart and are left out.
/// </summary>
public sealed partial class KsTguiAirAlarmWindow : FancyWindow, IKsAirAlarmWindow
{
    private enum Screen : byte
    {
        Home,
        Vents,
        Scrubbers,
        Modes,
        Thresholds,
    }

    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;

    private readonly KsTguiNoticeBox _panicWireNotice;
    private readonly KsTguiNoticeBox _lockedNotice;
    private readonly KsTguiButton _resyncButton;
    private readonly KsTguiSection _statusSection;
    private readonly KsTguiLabeledList _statusList;

    private readonly KsTguiSection _screenSection;
    private readonly KsTguiButton _backButton;
    private readonly Dictionary<Screen, BoxContainer> _screens = new();
    private readonly Dictionary<Screen, string> _screenTitles = new()
    {
        [Screen.Home] = "ks-tgui-airalarm-screen-home",
        [Screen.Vents] = "ks-tgui-airalarm-screen-vents",
        [Screen.Scrubbers] = "ks-tgui-airalarm-screen-scrubbers",
        [Screen.Modes] = "ks-tgui-airalarm-screen-modes",
        [Screen.Thresholds] = "ks-tgui-airalarm-screen-thresholds",
    };

    private readonly KsTguiButton _panicButton;
    private readonly Dictionary<AirAlarmMode, KsTguiButton> _modeButtons = new();
    private readonly KsTguiButton _autoModeButton;
    private readonly KsTguiNoticeBox _modesNotice;
    private readonly KsTguiNoticeBox _ventsEmpty;
    private readonly KsTguiNoticeBox _scrubbersEmpty;
    private readonly KsTguiNoticeBox _sensorsEmpty;
    private readonly BoxContainer _ventContainer;
    private readonly BoxContainer _scrubberContainer;
    private readonly BoxContainer _sensorContainer;

    private readonly Dictionary<string, KsTguiAirAlarmVentSection> _vents = new();
    private readonly Dictionary<string, KsTguiAirAlarmScrubberSection> _scrubbers = new();
    private readonly Dictionary<string, KsTguiAirAlarmSensorSection> _sensors = new();

    private AirAlarmMode _mode = AirAlarmMode.None;
    private bool _autoMode;
    private bool _panicWireCut;
    private bool _hasAccess;

    public event Action<string, IAtmosDeviceData>? AtmosDeviceDataChanged;
    public event Action<IAtmosDeviceData>? AtmosDeviceDataCopied;
    public event Action<string, AtmosMonitorThresholdType, AtmosAlarmThreshold, Gas?>? AtmosAlarmThresholdChanged;
    public event Action<AirAlarmMode>? AirAlarmModeChanged;
    public event Action<bool>? AutoModeChanged;
    public event Action? ResyncAllRequested;

    public KsTguiAirAlarmWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("air-alarm-ui-title");
        // Wider than tg's 475: the threshold table edits four values per row in place.
        SetSize = new Vector2(600f, 650f);
        MinSize = new Vector2(600f, 300f);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
            Margin = new Thickness(6f),
            VerticalExpand = true,
        };

        _panicWireNotice = new KsTguiNoticeBox
        {
            HorizontalExpand = true,
            NoticeColor = KsTguiNoticeColor.Danger,
            Text = Loc.GetString("ks-tgui-airalarm-panic-wire-cut"),
            Visible = false,
        };
        root.AddChild(_panicWireNotice);

        // Starts locked: the BUI says whether the player has access once it knows.
        _lockedNotice = new KsTguiNoticeBox
        {
            HorizontalExpand = true,
            Text = Loc.GetString("ks-tgui-airalarm-locked"),
        };
        root.AddChild(_lockedNotice);

        _statusSection = new KsTguiSection { Title = Loc.GetString("ks-tgui-airalarm-air-status") };
        _statusList = new KsTguiLabeledList();
        _statusSection.AddChild(_statusList);
        root.AddChild(_statusSection);

        _backButton = new KsTguiButton { Icon = KsTguiIcons.ArrowLeft, Text = Loc.GetString("ks-tgui-airalarm-back") };
        _backButton.OnPressed += _ => ShowScreen(Screen.Home);

        _screenSection = new KsTguiSection
        {
            Buttons = _backButton,
            Fill = true,
            Scrollable = true,
            VerticalExpand = true,
        };

        // Home
        var home = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        _panicButton = new KsTguiButton { Text = Loc.GetString("ks-tgui-airalarm-panic-siphon") };
        _panicButton.OnPressed += _ => SetMode(_mode == AirAlarmMode.Panic ? AirAlarmMode.Filtering : AirAlarmMode.Panic);
        home.AddChild(_panicButton);
        home.AddChild(NavigationButton(KsTguiIcons.SignOut, "ks-tgui-airalarm-screen-vents", Screen.Vents));
        home.AddChild(NavigationButton(KsTguiIcons.Filter, "ks-tgui-airalarm-screen-scrubbers", Screen.Scrubbers));
        home.AddChild(NavigationButton(KsTguiIcons.Cog, "ks-tgui-airalarm-screen-modes", Screen.Modes));
        home.AddChild(NavigationButton(KsTguiIcons.ChartBar, "ks-tgui-airalarm-screen-thresholds", Screen.Thresholds));
        var resyncButton = _resyncButton = new KsTguiButton { Icon = KsTguiIcons.Sync, Text = Loc.GetString("air-alarm-ui-window-resync-devices-label") };
        resyncButton.OnPressed += _ => Resync();
        home.AddChild(resyncButton);
        AddScreen(Screen.Home, home);

        // Vents
        var vents = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = KsTguiSection.Gap };
        _ventsEmpty = EmptyNotice();
        _ventContainer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = KsTguiSection.Gap };
        vents.AddChild(_ventsEmpty);
        vents.AddChild(_ventContainer);
        AddScreen(Screen.Vents, vents);

        // Scrubbers
        var scrubbers = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = KsTguiSection.Gap };
        _scrubbersEmpty = EmptyNotice();
        _scrubberContainer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = KsTguiSection.Gap };
        scrubbers.AddChild(_scrubbersEmpty);
        scrubbers.AddChild(_scrubberContainer);
        AddScreen(Screen.Scrubbers, scrubbers);

        // Modes
        var modes = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        _modesNotice = new KsTguiNoticeBox
        {
            HorizontalExpand = true,
            NoticeColor = KsTguiNoticeColor.Danger,
            Text = Loc.GetString("ks-tgui-airalarm-panic-wire-cut"),
            Visible = false,
        };
        modes.AddChild(_modesNotice);
        foreach (var mode in Enum.GetValues<AirAlarmMode>())
        {
            var capturedMode = mode;
            var button = new KsTguiButton { Text = Loc.GetString(ModeKey(mode)) };
            button.OnPressed += _ => SetMode(capturedMode);
            _modeButtons.Add(mode, button);
            modes.AddChild(button);
        }

        _autoModeButton = new KsTguiButton
        {
            Text = Loc.GetString("air-alarm-ui-window-auto-mode-label"),
            Margin = new Thickness(0f, 6f, 0f, 0f),
        };
        _autoModeButton.OnPressed += _ =>
        {
            if (!_hasAccess)
                return;

            _autoMode = !_autoMode;
            RefreshModes();
            AutoModeChanged?.Invoke(_autoMode);
        };
        modes.AddChild(_autoModeButton);
        AddScreen(Screen.Modes, modes);

        // Thresholds
        var thresholds = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = KsTguiSection.Gap };
        _sensorsEmpty = EmptyNotice();
        _sensorContainer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = KsTguiSection.Gap };
        thresholds.AddChild(_sensorsEmpty);
        thresholds.AddChild(_sensorContainer);
        AddScreen(Screen.Thresholds, thresholds);

        root.AddChild(_screenSection);
        ContentsContainer.AddChild(root);

        ShowScreen(Screen.Home);
        RefreshModes();
        RefreshEmptyNotices();
    }

    public void SetEntity(EntityUid uid)
    {
        // tg's window has no picture of the machine.
    }

    public void SetAccessEnabled(bool hasAccess)
    {
        _hasAccess = hasAccess;
        _lockedNotice.Visible = !hasAccess;
        _resyncButton.Disabled = !hasAccess;
        RefreshModes();

        foreach (var vent in _vents.Values)
            vent.SetAccessEnabled(hasAccess);

        foreach (var scrubber in _scrubbers.Values)
            scrubber.SetAccessEnabled(hasAccess);

        foreach (var sensor in _sensors.Values)
            sensor.SetAccessEnabled(hasAccess);
    }

    public void UpdateState(AirAlarmUIState state)
    {
        _mode = state.Mode;
        _autoMode = state.AutoMode;
        _panicWireCut = state.PanicWireCut;
        _panicWireNotice.Visible = state.PanicWireCut;
        RefreshModes();

        foreach (var (address, device) in state.DeviceData)
            UpdateDeviceData(address, device);

        RefreshStatus(state);
        RefreshEmptyNotices();
    }

    private void UpdateDeviceData(string address, IAtmosDeviceData device)
    {
        switch (device)
        {
            case GasVentPumpData pump:
                if (_vents.TryGetValue(address, out var ventSection))
                {
                    ventSection.ChangeData(pump);
                    break;
                }

                var newVent = new KsTguiAirAlarmVentSection(pump, address);
                newVent.PumpDataChanged += (changedAddress, data) => AtmosDeviceDataChanged?.Invoke(changedAddress, data);
                newVent.PumpDataCopied += data => AtmosDeviceDataCopied?.Invoke(data);
                newVent.SetAccessEnabled(_hasAccess);
                _vents.Add(address, newVent);
                _ventContainer.AddChild(newVent);
                break;
            case GasVentScrubberData scrubber:
                if (_scrubbers.TryGetValue(address, out var scrubberSection))
                {
                    scrubberSection.ChangeData(scrubber);
                    break;
                }

                var newScrubber = new KsTguiAirAlarmScrubberSection(scrubber, address);
                newScrubber.ScrubberDataChanged += (changedAddress, data) => AtmosDeviceDataChanged?.Invoke(changedAddress, data);
                newScrubber.ScrubberDataCopied += data => AtmosDeviceDataCopied?.Invoke(data);
                newScrubber.SetAccessEnabled(_hasAccess);
                _scrubbers.Add(address, newScrubber);
                _scrubberContainer.AddChild(newScrubber);
                break;
            case AtmosSensorData sensor:
                if (_sensors.TryGetValue(address, out var sensorSection))
                {
                    sensorSection.ChangeData(sensor);
                    break;
                }

                var newSensor = new KsTguiAirAlarmSensorSection(sensor, address);
                newSensor.OnThresholdUpdate += (changedAddress, type, threshold, gas) =>
                    AtmosAlarmThresholdChanged?.Invoke(changedAddress, type, threshold, gas);
                newSensor.SensorDataCopied += data => AtmosDeviceDataCopied?.Invoke(data);
                newSensor.SetAccessEnabled(_hasAccess);
                _sensors.Add(address, newSensor);
                _sensorContainer.AddChild(newSensor);
                break;
        }
    }

    private void RefreshStatus(AirAlarmUIState state)
    {
        _statusList.RemoveAllChildren();

        // The average pressure and temperature are the alarm's own; the gases are summed over the sensors it can see.
        var totalMoles = 0f;
        var sensorCount = 0;
        var gasMoles = new Dictionary<Gas, float>();
        var gasAlarms = new Dictionary<Gas, AtmosAlarmType>();
        foreach (var (_, device) in state.DeviceData)
        {
            if (device is not AtmosSensorData sensor)
                continue;

            sensorCount++;
            totalMoles += sensor.TotalMoles;
            foreach (var (gas, amount) in sensor.Gases)
                gasMoles[gas] = gasMoles.GetValueOrDefault(gas) + amount;
        }

        foreach (var (_, device) in state.DeviceData)
        {
            if (device is not AtmosSensorData sensor || sensor.TotalMoles <= 0f)
                continue;

            foreach (var (gas, amount) in sensor.Gases)
            {
                if (!sensor.GasThresholds.TryGetValue(gas, out var threshold))
                    continue;

                threshold.CheckThreshold(amount / sensor.TotalMoles, out var alarm);
                gasAlarms[gas] = KsTguiAirAlarmColors.Worse(gasAlarms.GetValueOrDefault(gas, AtmosAlarmType.Normal), alarm);
            }
        }

        var alarmType = state.AlarmType;
        var alarmColor = KsTguiAirAlarmColors.ForAlarm(alarmType);

        if (sensorCount == 0)
        {
            _statusList.AddItem(Loc.GetString("ks-tgui-airalarm-warning"), Loc.GetString("ks-tgui-airalarm-no-sample"), KsTguiPalettes.Bad);
        }
        else
        {
            _statusList.AddItem(Loc.GetString("air-alarm-ui-window-pressure-label"),
                Loc.GetString("air-alarm-ui-window-pressure", ("pressure", $"{state.PressureAverage:0.##}")),
                alarmColor);
            _statusList.AddItem(Loc.GetString("air-alarm-ui-window-temperature-label"),
                Loc.GetString("air-alarm-ui-window-temperature",
                    ("tempC", $"{TemperatureHelpers.KelvinToCelsius(state.TemperatureAverage):0.#}"),
                    ("temperature", $"{state.TemperatureAverage:0.##}")),
                alarmColor);

            var atmosphereSystem = _entityManager.System<SharedAtmosphereSystem>();
            foreach (var (gas, moles) in gasMoles)
            {
                if (moles <= 0f || totalMoles <= 0f)
                    continue;

                ProtoId<GasPrototype> gasProtoId = atmosphereSystem.GetGas(gas);
                var gasName = _prototypeManager.Index(gasProtoId).Name;
                var fraction = moles / totalMoles;
                _statusList.AddItem(Loc.GetString(gasName),
                    Loc.GetString("ks-tgui-airalarm-gas-line",
                        ("moles", $"{(moles / (float)sensorCount):0.##}"),
                        ("percent", $"{(100f * fraction):0.##}"),
                        ("pressure", $"{(fraction * state.PressureAverage):0.##}")),
                    KsTguiAirAlarmColors.ForAlarm(gasAlarms.GetValueOrDefault(gas, AtmosAlarmType.Normal)));
            }

            _statusList.AddItem(Loc.GetString("ks-tgui-airalarm-local-status"), Loc.GetString(KsTguiAirAlarmColors.StateKey(alarmType)), alarmColor);
        }

        _statusList.AddItem(Loc.GetString("air-alarm-ui-window-address-label"), state.Address);
        _statusList.AddItem(Loc.GetString("air-alarm-ui-window-device-count-label"), $"{state.DeviceCount}");
    }

    private void RefreshModes()
    {
        _panicButton.Icon = _mode == AirAlarmMode.Panic ? KsTguiIcons.Warning : KsTguiIcons.Exclamation;
        _panicButton.Color = _mode == AirAlarmMode.Panic ? KsTguiColor.Danger : KsTguiColor.Default;
        _panicButton.Disabled = _panicWireCut || !_hasAccess;

        foreach (var (mode, button) in _modeButtons)
        {
            var selected = mode == _mode;
            button.Icon = selected ? KsTguiIcons.CheckSquare : KsTguiIcons.Square;
            button.Color = selected ? (mode == AirAlarmMode.Panic ? KsTguiColor.Red : KsTguiColor.Green) : KsTguiColor.Default;
            button.Disabled = _panicWireCut || !_hasAccess;
        }

        _modesNotice.Visible = _panicWireCut;

        _autoModeButton.Icon = _autoMode ? KsTguiIcons.CheckSquare : KsTguiIcons.Square;
        _autoModeButton.Color = _autoMode ? KsTguiColor.Green : KsTguiColor.Default;
        _autoModeButton.Disabled = !_hasAccess;
    }

    private void RefreshEmptyNotices()
    {
        _ventsEmpty.Visible = _vents.Count == 0;
        _scrubbersEmpty.Visible = _scrubbers.Count == 0;
        _sensorsEmpty.Visible = _sensors.Count == 0;
    }

    private void SetMode(AirAlarmMode mode)
    {
        if (_panicWireCut || !_hasAccess)
            return;

        _mode = mode;
        RefreshModes();
        AirAlarmModeChanged?.Invoke(mode);
    }

    private void Resync()
    {
        if (!_hasAccess)
            return;

        _ventContainer.RemoveAllChildren();
        _vents.Clear();
        _scrubberContainer.RemoveAllChildren();
        _scrubbers.Clear();
        _sensorContainer.RemoveAllChildren();
        _sensors.Clear();
        RefreshEmptyNotices();
        ResyncAllRequested?.Invoke();
    }

    private KsTguiButton NavigationButton(string icon, string textKey, Screen screen)
    {
        var button = new KsTguiButton { Icon = icon, Text = Loc.GetString(textKey) };
        button.OnPressed += _ => ShowScreen(screen);
        return button;
    }

    private static KsTguiNoticeBox EmptyNotice()
    {
        return new KsTguiNoticeBox
        {
            HorizontalExpand = true,
            NoticeColor = KsTguiNoticeColor.Info,
            Text = Loc.GetString("ks-tgui-airalarm-nothing"),
        };
    }

    private void AddScreen(Screen screen, BoxContainer content)
    {
        content.Visible = false;
        _screens.Add(screen, content);
        _screenSection.AddChild(content);
    }

    private void ShowScreen(Screen screen)
    {
        foreach (var (key, content) in _screens)
            content.Visible = key == screen;

        _screenSection.Title = Loc.GetString(_screenTitles[screen]);
        _backButton.Disabled = screen == Screen.Home;
    }

    private static string ModeKey(AirAlarmMode mode)
    {
        return mode switch
        {
            AirAlarmMode.Filtering => "air-alarm-ui-mode-filtering",
            AirAlarmMode.WideFiltering => "air-alarm-ui-mode-wide-filtering",
            AirAlarmMode.Fill => "air-alarm-ui-mode-fill",
            AirAlarmMode.Panic => "air-alarm-ui-mode-panic",
            _ => "air-alarm-ui-mode-none",
        };
    }
}
