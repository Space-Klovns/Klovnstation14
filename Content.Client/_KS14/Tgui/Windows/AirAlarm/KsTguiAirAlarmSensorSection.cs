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
///         for pressure, temperature and every gas. Each value is edited in place, with a toggle that enables it, and each
///         row has the toggle for ignoring the reading altogether.
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

    // One value cell: the enable toggle, then the number. The header labels are as wide, so the columns line up.
    private const float ToggleWidth = 22f;
    private const float InputWidth = 54f;
    private const float CellWidth = ToggleWidth + 2f + InputWidth;

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

    private readonly KsTguiButton _copyButton;
    private bool _hasAccess = true;

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
        _copyButton = new KsTguiButton
        {
            Icon = KsTguiIcons.Copy,
            ToolTip = Loc.GetString("air-alarm-ui-thresholds-copy-tooltip"),
        };
        _copyButton.OnPressed += _ => SensorDataCopied?.Invoke(_data);
        _section.Buttons = _copyButton;

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

        // The table, as tgui's: a header row, then a candystriped row per reading. The header counts for odd and even.
        var table = new KsTguiTable { Margin = new Thickness(0f, 8f, 0f, 0f) };
        var header = new KsTguiTableRow { Header = true };
        header.AddChild(new Label { Text = Loc.GetString("ks-tgui-airalarm-threshold"), HorizontalExpand = true, StyleClasses = { "LabelHeading" } });
        header.AddChild(HeaderLabel(Loc.GetString("ks-tgui-airalarm-danger-below"), KsTguiPalettes.Bad));
        header.AddChild(HeaderLabel(Loc.GetString("ks-tgui-airalarm-warning-below"), KsTguiPalettes.Average));
        header.AddChild(HeaderLabel(Loc.GetString("ks-tgui-airalarm-warning-above"), KsTguiPalettes.Average));
        header.AddChild(HeaderLabel(Loc.GetString("ks-tgui-airalarm-danger-above"), KsTguiPalettes.Bad));
        table.AddChild(header);

        AddRow(table, Loc.GetString("ks-tgui-airalarm-row-pressure"), AtmosMonitorThresholdType.Pressure, null, 1f, 100000f, 10f, "kPa");
        AddRow(table, Loc.GetString("ks-tgui-airalarm-row-temperature"), AtmosMonitorThresholdType.Temperature, null, 1f, 100000f, 10f, "K");
        foreach (var gas in data.Gases.Keys)
        {
            ProtoId<GasPrototype> gasProtoId = atmosphereSystem.GetGas(gas);
            var gasName = _prototypeManager.Index(gasProtoId).Name;
            AddRow(table, Loc.GetString(gasName), AtmosMonitorThresholdType.Gas, gas, 100f, 100f, 1f, "%");
        }

        _section.AddChild(table);

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
        var label = new Label { Text = text, MinWidth = CellWidth };
        if (color != null)
            label.FontColorOverride = color;

        return label;
    }

    /// <summary>
    ///     Without access every control is disabled; the server refuses the messages either way.
    /// </summary>
    public void SetAccessEnabled(bool hasAccess)
    {
        _hasAccess = hasAccess;
        _copyButton.Disabled = !hasAccess;
        foreach (var row in _rows)
            RefreshRow(row, row.Current);
    }

    private void AddRow(KsTguiTable table, string name, AtmosMonitorThresholdType type, Gas? gas, float scale, float maxValue, float step, string unit)
    {
        var threshold = type switch
        {
            AtmosMonitorThresholdType.Pressure => _data.PressureThreshold,
            AtmosMonitorThresholdType.Temperature => _data.TemperatureThreshold,
            _ => _data.GasThresholds[gas!.Value],
        };

        var row = new ThresholdRow(name, type, gas, scale, unit, threshold);

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

        var tableRow = new KsTguiTableRow { Candystripe = true };
        var title = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4, HorizontalExpand = true };
        title.AddChild(row.IgnoreButton);
        title.AddChild(new Label { Text = name, VerticalAlignment = VAlignment.Center });
        tableRow.AddChild(title);

        for (var i = 0; i < LimitColumns.Length; i++)
        {
            var limit = LimitColumns[i];

            var toggle = new KsTguiButton
            {
                Icon = KsTguiIcons.Square,
                Compact = true,
                MinWidth = ToggleWidth,
                ToolTip = Loc.GetString("ks-tgui-airalarm-toggle-limit"),
            };
            var input = new KsTguiNumberInput
            {
                Unit = unit,
                WidthPixels = InputWidth,
                MinValue = 0.01f,
                MaxValue = maxValue,
                Step = step,
            };

            toggle.OnPressed += _ =>
            {
                row.Threshold.SetEnabled(limit, !SettingOf(row.Threshold, limit).Enabled);
                OnThresholdUpdate?.Invoke(_address, row.Type, row.Threshold, row.Gas);
                RefreshRow(row, row.Current);
            };
            input.OnCommitted += _ =>
            {
                row.Threshold.SetLimit(limit, input.Value / row.Scale);
                OnThresholdUpdate?.Invoke(_address, row.Type, row.Threshold, row.Gas);
                RefreshRow(row, row.Current);
            };

            row.Toggles[i] = toggle;
            row.Inputs[i] = input;

            var cell = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 2 };
            cell.AddChild(toggle);
            cell.AddChild(input);
            tableRow.AddChild(cell);
        }

        table.AddChild(tableRow);

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
            var toggle = row.Toggles[i]!;
            var input = row.Inputs[i]!;

            toggle.Selected = setting.Enabled;
            toggle.Icon = setting.Enabled ? KsTguiIcons.CheckSquare : KsTguiIcons.Square;
            toggle.Disabled = !_hasAccess;
            toggle.Color = limit == violatedLimit
                ? alarm == AtmosAlarmType.Danger ? KsTguiColor.Bad : KsTguiColor.Average
                : KsTguiColor.Default;

            input.Disabled = !_hasAccess || !setting.Enabled;
            if (!input.IsEditing)
                input.SetValue(setting.Value * row.Scale);
        }

        row.IgnoreButton!.Selected = !row.Threshold.Ignore;
        row.IgnoreButton.Disabled = !_hasAccess;
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

    private sealed class ThresholdRow
    {
        public readonly string Name;
        public readonly AtmosMonitorThresholdType Type;
        public readonly Gas? Gas;
        public readonly float Scale;
        public readonly string Unit;
        public readonly KsTguiButton?[] Toggles = new KsTguiButton?[LimitColumns.Length];
        public readonly KsTguiNumberInput?[] Inputs = new KsTguiNumberInput?[LimitColumns.Length];
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
