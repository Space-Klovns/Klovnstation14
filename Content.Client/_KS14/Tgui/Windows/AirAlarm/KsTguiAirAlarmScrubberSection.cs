using Content.Client._KS14.Tgui.Controls;
using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Atmos.Monitor.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Atmos.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Tgui.Windows.AirAlarm;

/// <summary>
///     One scrubber, after tgui's Scrubber: power in the title, the mode and range, and a button for each gas it filters.
/// </summary>
public sealed partial class KsTguiAirAlarmScrubberSection : BoxContainer
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;

    private readonly GasVentScrubberData _data;
    private readonly string _address;

    private readonly KsTguiButton _powerButton;
    private readonly KsTguiButton _directionButton;
    private readonly KsTguiButton _wideNetButton;
    private readonly KsTguiNumberInput _volumeRate;
    private readonly Dictionary<Gas, KsTguiButton> _gasButtons = new();

    public event Action<string, IAtmosDeviceData>? ScrubberDataChanged;
    public event Action<IAtmosDeviceData>? ScrubberDataCopied;

    public KsTguiAirAlarmScrubberSection(GasVentScrubberData data, string address)
    {
        IoCManager.InjectDependencies(this);
        var atmosphereSystem = _entityManager.System<SharedAtmosphereSystem>();

        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        Name = address;

        _data = data;
        _address = address;

        var section = new KsTguiSection
        {
            Title = Loc.GetString("air-alarm-ui-atmos-net-device-label", ("address", address)),
            HorizontalExpand = true,
        };

        var titleButtons = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        _powerButton = new KsTguiButton();
        _powerButton.OnPressed += _ =>
        {
            _data.Enabled = !_data.Enabled;
            Refresh();
            ScrubberDataChanged?.Invoke(_address, _data);
        };
        titleButtons.AddChild(_powerButton);

        var copyButton = new KsTguiButton
        {
            Icon = KsTguiIcons.Copy,
            ToolTip = Loc.GetString("air-alarm-ui-widget-copy-tooltip"),
        };
        copyButton.OnPressed += _ => ScrubberDataCopied?.Invoke(_data);
        titleButtons.AddChild(copyButton);
        section.Buttons = titleButtons;

        var list = new KsTguiLabeledList();

        var modeItem = list.AddItem(Loc.GetString("air-alarm-ui-scrubber-pump-direction-label"));
        var modeBox = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        _directionButton = new KsTguiButton();
        _directionButton.OnPressed += _ =>
        {
            _data.PumpDirection = _data.PumpDirection == ScrubberPumpDirection.Scrubbing
                ? ScrubberPumpDirection.Siphoning
                : ScrubberPumpDirection.Scrubbing;
            Refresh();
            ScrubberDataChanged?.Invoke(_address, _data);
        };
        _wideNetButton = new KsTguiButton();
        _wideNetButton.OnPressed += _ =>
        {
            _data.WideNet = !_data.WideNet;
            Refresh();
            ScrubberDataChanged?.Invoke(_address, _data);
        };
        modeBox.AddChild(_directionButton);
        modeBox.AddChild(_wideNetButton);
        modeItem.XamlChildren.Add(modeBox);

        var rateItem = list.AddItem(Loc.GetString("air-alarm-ui-scrubber-volume-rate-label"));
        _volumeRate = new KsTguiNumberInput { Unit = Loc.GetString("ks-tgui-atmos-unit-ls"), WidthPixels = 75f, MinValue = 0f, MaxValue = Atmospherics.MaxTransferRate, Step = 10f };
        _volumeRate.OnCommitted += _ =>
        {
            _data.VolumeRate = _volumeRate.Value;
            ScrubberDataChanged?.Invoke(_address, _data);
        };
        rateItem.XamlChildren.Add(_volumeRate);

        var filtersItem = list.AddItem(Loc.GetString("air-alarm-ui-widget-gas-filters"));
        var filterBox = new BoxContainer { Orientation = LayoutOrientation.Vertical };

        var allGases = Enum.GetValues<Gas>();
        var allButtons = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        var selectAll = new KsTguiButton
        {
            Icon = KsTguiIcons.CheckSquare,
            Text = Loc.GetString("air-alarm-ui-scrubber-select-all-gases-label"),
        };
        selectAll.OnPressed += _ =>
        {
            _data.FilterGases = new HashSet<Gas>(allGases);
            Refresh();
            ScrubberDataChanged?.Invoke(_address, _data);
        };
        var deselectAll = new KsTguiButton
        {
            Icon = KsTguiIcons.Square,
            Text = Loc.GetString("air-alarm-ui-scrubber-deselect-all-gases-label"),
        };
        deselectAll.OnPressed += _ =>
        {
            _data.FilterGases = new HashSet<Gas>();
            Refresh();
            ScrubberDataChanged?.Invoke(_address, _data);
        };
        allButtons.AddChild(selectAll);
        allButtons.AddChild(deselectAll);
        filterBox.AddChild(allButtons);

        var gasGrid = new GridContainer { Columns = 3, HSeparationOverride = 2, VSeparationOverride = 2 };
        foreach (var gas in allGases)
        {
            ProtoId<GasPrototype> gasProtoId = atmosphereSystem.GetGas(gas);
            var gasName = _prototypeManager.Index(gasProtoId).Name;

            var gasButton = new KsTguiButton
            {
                Text = Loc.GetString(gasName),
                ToolTip = Loc.GetString(gasName),
                Fluid = true,
                Compact = true,
            };
            var capturedGas = gas;
            gasButton.OnPressed += _ =>
            {
                if (!_data.FilterGases.Remove(capturedGas))
                    _data.FilterGases.Add(capturedGas);

                Refresh();
                ScrubberDataChanged?.Invoke(_address, _data);
            };
            _gasButtons.Add(gas, gasButton);
            gasGrid.AddChild(gasButton);
        }

        filterBox.AddChild(gasGrid);
        filtersItem.XamlChildren.Add(filterBox);

        section.AddChild(list);
        AddChild(section);

        _volumeRate.SetValue(_data.VolumeRate);
        Refresh();
    }

    public void ChangeData(GasVentScrubberData data)
    {
        _data.Enabled = data.Enabled;
        _data.PumpDirection = data.PumpDirection;
        _data.WideNet = data.WideNet;
        _data.FilterGases = data.FilterGases;
        _data.VolumeRate = data.VolumeRate;
        _data.AirAlarmPanicWireCut = data.AirAlarmPanicWireCut;

        if (!_volumeRate.IsEditing)
            _volumeRate.SetValue(_data.VolumeRate);

        Refresh();
    }

    private void Refresh()
    {
        _powerButton.Icon = _data.Enabled ? KsTguiIcons.Power : KsTguiIcons.Times;
        _powerButton.Text = Loc.GetString(_data.Enabled ? "ks-tgui-airalarm-on" : "ks-tgui-airalarm-off");
        _powerButton.Selected = _data.Enabled;

        var scrubbing = _data.PumpDirection == ScrubberPumpDirection.Scrubbing;
        _directionButton.Icon = scrubbing ? KsTguiIcons.Filter : KsTguiIcons.SignIn;
        _directionButton.Text = Loc.GetString(scrubbing
            ? "air-alarm-ui-pump-direction-scrubbing"
            : "air-alarm-ui-pump-direction-siphoning");
        _directionButton.Color = scrubbing ? KsTguiColor.Default : KsTguiColor.Danger;
        _directionButton.Disabled = _data.AirAlarmPanicWireCut;

        _wideNetButton.Icon = _data.WideNet ? KsTguiIcons.Expand : KsTguiIcons.Compress;
        _wideNetButton.Text = Loc.GetString(_data.WideNet ? "ks-tgui-airalarm-expanded" : "ks-tgui-airalarm-normal");
        _wideNetButton.Selected = _data.WideNet;

        foreach (var (gas, button) in _gasButtons)
        {
            var enabled = _data.FilterGases.Contains(gas);
            button.Selected = enabled;
            button.Icon = enabled ? KsTguiIcons.CheckSquare : KsTguiIcons.Square;
        }
    }
}
