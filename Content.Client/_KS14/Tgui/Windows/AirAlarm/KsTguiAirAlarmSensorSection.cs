using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Temperature;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Tgui.Windows.AirAlarm;

/// <summary>
///     One sensor: its readings, then tgui's Alarm Thresholds table (danger below, warning below, warning above, danger above)
///         for pressure, temperature and every gas. Picking a value opens the editor under the table, as tgui opens its
///         Threshold Value Editor.
/// </summary>
public sealed partial class KsTguiAirAlarmSensorSection : BoxContainer
{
    private static readonly AtmosMonitorLimitType[] LimitColumns =
    [
        AtmosMonitorLimitType.LowerDanger,
        AtmosMonitorLimitType.LowerWarning,
        AtmosMonitorLimitType.UpperWarning,
        AtmosMonitorLimitType.UpperDanger,
    ];

    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;

    private readonly string _address;
    private readonly KsTguiSection _section;
    private readonly KsTguiLabeledList _readings;
    private readonly KsTguiLabeledListItem _statusItem;
    private readonly KsTguiLabeledListItem _pressureItem;
    private readonly KsTguiLabeledListItem _temperatureItem;
    private readonly Dictionary<Gas, KsTguiLabeledListItem> _gasItems = new();
    private readonly List<ThresholdRow> _rows = new();

    private readonly KsTguiSection _editor;
    private readonly Label _editorText;
    private readonly FloatSpinBox _editorSpin;
    private readonly KsTguiButton _editorToggle;

    private ThresholdRow? _editingRow;
    private AtmosMonitorLimitType _editingLimit;

    private AtmosSensorData _data;

    public event Action<string, AtmosMonitorThresholdType, AtmosAlarmThreshold, Gas?>? OnThresholdUpdate;
    public event Action<AtmosSensorData>? SensorDataCopied;

    public KsTguiAirAlarmSensorSection(AtmosSensorData data, string address)
    {
        IoCManager.InjectDependencies(this);
        var atmosphereSystem = _entityManager.System<SharedAtmosphereSystem>();

        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        Name = address;

        _data = data;
        _address = address;

        _section = new KsTguiSection { HorizontalExpand = true };
        var copyButton = new KsTguiButton
        {
            Icon = KsTguiIcons.Copy,
            ToolTip = Loc.GetString("air-alarm-ui-thresholds-copy-tooltip"),
        };
        copyButton.OnPressed += _ => SensorDataCopied?.Invoke(_data);
        _section.Buttons = copyButton;

        _readings = new KsTguiLabeledList();
        _statusItem = _readings.AddItem(Loc.GetString("air-alarm-ui-window-alarm-state-label"));
        _pressureItem = _readings.AddItem(Loc.GetString("air-alarm-ui-window-pressure-label"));
        _temperatureItem = _readings.AddItem(Loc.GetString("air-alarm-ui-window-temperature-label"));
        foreach (var gas in data.Gases.Keys)
        {
            ProtoId<GasPrototype> gasProtoId = atmosphereSystem.GetGas(gas);
            var gasName = _prototypeManager.Index(gasProtoId).Name;
            _gasItems.Add(gas, _readings.AddItem(Loc.GetString(gasName)));
        }

        _section.AddChild(_readings);

        // The table. Both separations are the same: GridContainer measures with one and places with the other.
        var grid = new GridContainer { Columns = 6, HSeparationOverride = 3, VSeparationOverride = 3, Margin = new Thickness(0f, 8f, 0f, 0f) };
        grid.AddChild(HeaderLabel(Loc.GetString("ks-tgui-airalarm-threshold"), null));
        grid.AddChild(HeaderLabel(Loc.GetString("ks-tgui-airalarm-danger-below"), KsTguiPalettes.Bad));
        grid.AddChild(HeaderLabel(Loc.GetString("ks-tgui-airalarm-warning-below"), KsTguiPalettes.Average));
        grid.AddChild(HeaderLabel(Loc.GetString("ks-tgui-airalarm-warning-above"), KsTguiPalettes.Average));
        grid.AddChild(HeaderLabel(Loc.GetString("ks-tgui-airalarm-danger-above"), KsTguiPalettes.Bad));
        grid.AddChild(HeaderLabel(string.Empty, null));

        AddRow(grid, Loc.GetString("ks-tgui-airalarm-row-pressure"), AtmosMonitorThresholdType.Pressure, null, 1f, "kPa");
        AddRow(grid, Loc.GetString("ks-tgui-airalarm-row-temperature"), AtmosMonitorThresholdType.Temperature, null, 1f, "K");
        foreach (var gas in data.Gases.Keys)
        {
            ProtoId<GasPrototype> gasProtoId = atmosphereSystem.GetGas(gas);
            var gasName = _prototypeManager.Index(gasProtoId).Name;
            AddRow(grid, Loc.GetString(gasName), AtmosMonitorThresholdType.Gas, gas, 100f, "%");
        }

        _section.AddChild(grid);

        _editorText = new Label { Margin = new Thickness(0f, 0f, 0f, 6f) };
        _editorSpin = new FloatSpinBox(1f, 2) { HorizontalAlignment = HAlignment.Left, MinWidth = 160f, IsValid = value => value > 0f };
        _editorSpin.OnValueChanged += _ => EditorValueChanged();
        _editorToggle = new KsTguiButton { Margin = new Thickness(0f, 6f, 0f, 0f) };
        _editorToggle.OnPressed += _ => EditorToggled();

        var closeButton = new KsTguiButton { Icon = KsTguiIcons.Times, Color = KsTguiColor.Red };
        closeButton.OnPressed += _ => CloseEditor();

        _editor = new KsTguiSection
        {
            Title = Loc.GetString("ks-tgui-airalarm-editor-title"),
            Buttons = closeButton,
            Visible = false,
            Margin = new Thickness(0f, 8f, 0f, 0f),
        };
        _editor.AddChild(_editorText);
        _editor.AddChild(_editorSpin);
        _editor.AddChild(_editorToggle);
        _section.AddChild(_editor);

        AddChild(_section);

        ChangeData(data);
    }

    public void ChangeData(AtmosSensorData data)
    {
        _data = data;

        _section.Title = Loc.GetString("air-alarm-ui-window-listing-title", ("address", _address), ("state", data.AlarmState));

        _statusItem.Text = Loc.GetString(KsTguiAirAlarmColors.StateKey(data.AlarmState));
        _statusItem.TextColor = KsTguiAirAlarmColors.ForAlarm(data.AlarmState);

        _pressureItem.Text = Loc.GetString("air-alarm-ui-window-pressure", ("pressure", $"{data.Pressure:0.##}"));
        _pressureItem.TextColor = ColorForThreshold(data.Pressure, data.PressureThreshold);

        _temperatureItem.Text = Loc.GetString("air-alarm-ui-window-temperature",
            ("tempC", $"{TemperatureHelpers.KelvinToCelsius(data.Temperature):0.#}"),
            ("temperature", $"{data.Temperature:0.##}"));
        _temperatureItem.TextColor = ColorForThreshold(data.Temperature, data.TemperatureThreshold);

        foreach (var (gas, item) in _gasItems)
        {
            if (!data.Gases.TryGetValue(gas, out var amount))
                continue;

            var fraction = GasFraction(data, gas);
            item.Text = Loc.GetString("ks-tgui-airalarm-sensor-gas", ("amount", $"{amount:0.####}"), ("percentage", $"{(100f * fraction):0.##}"));
            if (data.GasThresholds.TryGetValue(gas, out var gasThreshold))
                item.TextColor = ColorForThreshold(fraction, gasThreshold);
        }

        foreach (var row in _rows)
        {
            var threshold = row.Type switch
            {
                AtmosMonitorThresholdType.Pressure => data.PressureThreshold,
                AtmosMonitorThresholdType.Temperature => data.TemperatureThreshold,
                _ => data.GasThresholds.GetValueOrDefault(row.Gas!.Value),
            };

            if (threshold == null)
                continue;

            var current = row.Type switch
            {
                AtmosMonitorThresholdType.Pressure => data.Pressure,
                AtmosMonitorThresholdType.Temperature => data.Temperature,
                _ => GasFraction(data, row.Gas!.Value),
            };

            row.Threshold = threshold;
            RefreshRow(row, current);
        }

        RefreshEditor();
    }

    private static float GasFraction(AtmosSensorData data, Gas gas)
    {
        return data.TotalMoles > 0f && data.Gases.TryGetValue(gas, out var amount) ? amount / data.TotalMoles : 0f;
    }

    private static Color ColorForThreshold(float amount, AtmosAlarmThreshold threshold)
    {
        threshold.CheckThreshold(amount, out var alarm);
        return KsTguiAirAlarmColors.ForAlarm(alarm);
    }

    private static Label HeaderLabel(string text, Color? color)
    {
        var label = new Label { Text = text, HorizontalExpand = true };
        if (color != null)
            label.FontColorOverride = color;

        return label;
    }

    private void AddRow(GridContainer grid, string name, AtmosMonitorThresholdType type, Gas? gas, float scale, string unit)
    {
        var threshold = type switch
        {
            AtmosMonitorThresholdType.Pressure => _data.PressureThreshold,
            AtmosMonitorThresholdType.Temperature => _data.TemperatureThreshold,
            _ => _data.GasThresholds[gas!.Value],
        };

        var row = new ThresholdRow(name, type, gas, scale, unit, threshold);
        grid.AddChild(new Label { Text = name, HorizontalExpand = true });
        for (var i = 0; i < LimitColumns.Length; i++)
        {
            var limit = LimitColumns[i];
            var button = new KsTguiButton { Fluid = true, Compact = true };
            button.OnPressed += _ => OpenEditor(row, limit);
            row.Buttons[i] = button;
            grid.AddChild(button);
        }

        row.IgnoreButton = new KsTguiButton
        {
            Icon = KsTguiIcons.Power,
            Compact = true,
            ToolTip = Loc.GetString("ks-tgui-airalarm-toggle-alarms"),
        };
        row.IgnoreButton.OnPressed += _ =>
        {
            row.Threshold.Ignore = !row.Threshold.Ignore;
            row.IgnoreButton.Selected = !row.Threshold.Ignore;
            OnThresholdUpdate?.Invoke(_address, row.Type, row.Threshold, row.Gas);
        };
        grid.AddChild(row.IgnoreButton);

        _rows.Add(row);
    }

    private void RefreshRow(ThresholdRow row, float current)
    {
        row.Current = current;
        row.Threshold.CheckThreshold(current, out var alarm, out var bound);

        AtmosMonitorLimitType? violatedLimit = alarm switch
        {
            AtmosAlarmType.Danger => bound == AtmosMonitorThresholdBound.Upper ? AtmosMonitorLimitType.UpperDanger : AtmosMonitorLimitType.LowerDanger,
            AtmosAlarmType.Warning => bound == AtmosMonitorThresholdBound.Upper ? AtmosMonitorLimitType.UpperWarning : AtmosMonitorLimitType.LowerWarning,
            _ => null,
        };

        for (var i = 0; i < LimitColumns.Length; i++)
        {
            var limit = LimitColumns[i];
            var setting = SettingOf(row.Threshold, limit);
            var button = row.Buttons[i]!;
            button.Text = setting.Enabled
                ? $"{setting.Value * row.Scale:0.##} {row.Unit}"
                : Loc.GetString("ks-tgui-airalarm-disabled");

            button.Color = limit == violatedLimit
                ? alarm == AtmosAlarmType.Danger ? KsTguiColor.Bad : KsTguiColor.Average
                : KsTguiColor.Default;
        }

        row.IgnoreButton!.Selected = !row.Threshold.Ignore;
    }

    private static AlarmThresholdSetting SettingOf(AtmosAlarmThreshold threshold, AtmosMonitorLimitType limit)
    {
        return limit switch
        {
            AtmosMonitorLimitType.LowerDanger => threshold.LowerBound,
            AtmosMonitorLimitType.LowerWarning => threshold.LowerWarningBound,
            AtmosMonitorLimitType.UpperWarning => threshold.UpperWarningBound,
            _ => threshold.UpperBound,
        };
    }

    private void OpenEditor(ThresholdRow row, AtmosMonitorLimitType limit)
    {
        _editingRow = row;
        _editingLimit = limit;
        _editor.Visible = true;
        _editorSpin.Value = SettingOf(row.Threshold, limit).Value * row.Scale;
        RefreshEditor();
    }

    private void CloseEditor()
    {
        _editingRow = null;
        _editor.Visible = false;
    }

    private void RefreshEditor()
    {
        if (_editingRow == null)
            return;

        var setting = SettingOf(_editingRow.Threshold, _editingLimit);
        _editorText.Text = Loc.GetString("ks-tgui-airalarm-editing",
            ("limit", Loc.GetString(LimitKey(_editingLimit))),
            ("name", _editingRow.Name));
        _editorSpin.Visible = setting.Enabled;
        _editorToggle.Text = Loc.GetString(setting.Enabled ? "ks-tgui-airalarm-disable" : "ks-tgui-airalarm-enable");

        if (setting.Enabled && !_editorSpin.HasKeyboardFocus())
            _editorSpin.Value = setting.Value * _editingRow.Scale;
    }

    private static string LimitKey(AtmosMonitorLimitType limit)
    {
        return limit switch
        {
            AtmosMonitorLimitType.LowerDanger => "ks-tgui-airalarm-danger-below",
            AtmosMonitorLimitType.LowerWarning => "ks-tgui-airalarm-warning-below",
            AtmosMonitorLimitType.UpperWarning => "ks-tgui-airalarm-warning-above",
            _ => "ks-tgui-airalarm-danger-above",
        };
    }

    private void EditorValueChanged()
    {
        if (_editingRow == null)
            return;

        _editingRow.Threshold.SetLimit(_editingLimit, _editorSpin.Value / _editingRow.Scale);
        OnThresholdUpdate?.Invoke(_address, _editingRow.Type, _editingRow.Threshold, _editingRow.Gas);
    }

    private void EditorToggled()
    {
        if (_editingRow == null)
            return;

        var enabled = SettingOf(_editingRow.Threshold, _editingLimit).Enabled;
        _editingRow.Threshold.SetEnabled(_editingLimit, !enabled);
        OnThresholdUpdate?.Invoke(_address, _editingRow.Type, _editingRow.Threshold, _editingRow.Gas);

        RefreshRow(_editingRow, _editingRow.Current);
        RefreshEditor();
    }

    private sealed class ThresholdRow
    {
        public readonly string Name;
        public readonly AtmosMonitorThresholdType Type;
        public readonly Gas? Gas;
        public readonly float Scale;
        public readonly string Unit;
        public readonly KsTguiButton?[] Buttons = new KsTguiButton?[LimitColumns.Length];
        public AtmosAlarmThreshold Threshold;
        public float Current;
        public KsTguiButton? IgnoreButton;

        public ThresholdRow(string name, AtmosMonitorThresholdType type, Gas? gas, float scale, string unit, AtmosAlarmThreshold threshold)
        {
            Name = name;
            Type = type;
            Gas = gas;
            Scale = scale;
            Unit = unit;
            Threshold = threshold;
        }
    }
}
