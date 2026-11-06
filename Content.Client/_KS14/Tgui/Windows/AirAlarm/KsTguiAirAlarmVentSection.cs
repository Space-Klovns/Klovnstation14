using Content.Client._KS14.Tgui.Controls;
using Content.Shared.Atmos.Monitor.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Windows.AirAlarm;

/// <summary>
///     One vent pump, after tgui's Vent: a section with the power button in its title, then the mode, the pressure
///         regulator and the targets it uses.
/// </summary>
public sealed class KsTguiAirAlarmVentSection : BoxContainer
{
    private readonly GasVentPumpData _data;
    private readonly string _address;

    private readonly KsTguiButton _powerButton;
    private readonly KsTguiButton _copyButton;
    private bool _hasAccess = true;
    private readonly KsTguiButton _directionButton;
    private readonly KsTguiButton _internalCheckButton;
    private readonly KsTguiButton _externalCheckButton;
    private readonly KsTguiLabeledListItem _internalTargetItem;
    private readonly KsTguiLabeledListItem _externalTargetItem;
    private readonly KsTguiNumberInput _internalBound;
    private readonly KsTguiNumberInput _externalBound;

    public event Action<string, IAtmosDeviceData>? PumpDataChanged;
    public event Action<IAtmosDeviceData>? PumpDataCopied;

    public KsTguiAirAlarmVentSection(GasVentPumpData data, string address)
    {
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
            PumpDataChanged?.Invoke(_address, _data);
        };
        titleButtons.AddChild(_powerButton);

        _copyButton = new KsTguiButton
        {
            Icon = KsTguiIcons.Copy,
            ToolTip = Loc.GetString("air-alarm-ui-widget-copy-tooltip"),
        };
        _copyButton.OnPressed += _ => PumpDataCopied?.Invoke(_data);
        titleButtons.AddChild(_copyButton);
        section.Buttons = titleButtons;

        var list = new KsTguiLabeledList();

        var directionItem = list.AddItem(Loc.GetString("air-alarm-ui-vent-pump-label"));
        _directionButton = new KsTguiButton { Icon = KsTguiIcons.SignIn };
        _directionButton.OnPressed += _ =>
        {
            _data.PumpDirection = _data.PumpDirection == VentPumpDirection.Releasing
                ? VentPumpDirection.Siphoning
                : VentPumpDirection.Releasing;
            Refresh();
            PumpDataChanged?.Invoke(_address, _data);
        };
        directionItem.XamlChildren.Add(_directionButton);

        var regulatorItem = list.AddItem(Loc.GetString("air-alarm-ui-vent-pressure-label"));
        var regulatorBox = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        _internalCheckButton = new KsTguiButton
        {
            Icon = KsTguiIcons.SignIn,
            Text = Loc.GetString("ks-tgui-airalarm-internal"),
        };
        _internalCheckButton.OnPressed += _ => ToggleBound(VentPressureBound.InternalBound);
        _externalCheckButton = new KsTguiButton
        {
            Icon = KsTguiIcons.SignOut,
            Text = Loc.GetString("ks-tgui-airalarm-external"),
        };
        _externalCheckButton.OnPressed += _ => ToggleBound(VentPressureBound.ExternalBound);
        regulatorBox.AddChild(_internalCheckButton);
        regulatorBox.AddChild(_externalCheckButton);
        regulatorItem.XamlChildren.Add(regulatorBox);

        _internalTargetItem = list.AddItem(Loc.GetString("ks-tgui-airalarm-internal-target"));
        _internalBound = new KsTguiNumberInput { Unit = Loc.GetString("ks-tgui-atmos-unit-kpa"), WidthPixels = 75f, MinValue = 0f, MaxValue = 5066f, Step = 10f };
        _internalBound.OnCommitted += _ =>
        {
            _data.InternalPressureBound = _internalBound.Value;
            PumpDataChanged?.Invoke(_address, _data);
        };
        _internalTargetItem.XamlChildren.Add(_internalBound);

        _externalTargetItem = list.AddItem(Loc.GetString("ks-tgui-airalarm-external-target"));
        _externalBound = new KsTguiNumberInput { Unit = Loc.GetString("ks-tgui-atmos-unit-kpa"), WidthPixels = 75f, MinValue = 0f, MaxValue = 5066f, Step = 10f };
        _externalBound.OnCommitted += _ =>
        {
            _data.ExternalPressureBound = _externalBound.Value;
            PumpDataChanged?.Invoke(_address, _data);
        };
        _externalTargetItem.XamlChildren.Add(_externalBound);

        section.AddChild(list);
        AddChild(section);

        _internalBound.SetValue(_data.InternalPressureBound);
        _externalBound.SetValue(_data.ExternalPressureBound);
        Refresh();
    }

    public void ChangeData(GasVentPumpData data)
    {
        _data.Enabled = data.Enabled;
        _data.PumpDirection = data.PumpDirection;
        _data.PressureChecks = data.PressureChecks;
        _data.ExternalPressureBound = data.ExternalPressureBound;
        _data.InternalPressureBound = data.InternalPressureBound;

        if (!_externalBound.IsEditing)
            _externalBound.SetValue(_data.ExternalPressureBound);

        if (!_internalBound.IsEditing)
            _internalBound.SetValue(_data.InternalPressureBound);

        Refresh();
    }

    /// <summary>
    ///     Without access every control is disabled; the server refuses the messages either way.
    /// </summary>
    public void SetAccessEnabled(bool hasAccess)
    {
        _hasAccess = hasAccess;
        Refresh();
    }

    private void ToggleBound(VentPressureBound bound)
    {
        _data.PressureChecks ^= bound;
        Refresh();
        PumpDataChanged?.Invoke(_address, _data);
    }

    private void Refresh()
    {
        _powerButton.Icon = _data.Enabled ? KsTguiIcons.Power : KsTguiIcons.Times;
        _powerButton.Text = Loc.GetString(_data.Enabled ? "ks-tgui-airalarm-on" : "ks-tgui-airalarm-off");
        _powerButton.Selected = _data.Enabled;

        var releasing = _data.PumpDirection == VentPumpDirection.Releasing;
        _directionButton.Text = Loc.GetString(releasing
            ? "air-alarm-ui-pump-direction-releasing"
            : "air-alarm-ui-pump-direction-siphoning");
        _directionButton.Color = releasing ? KsTguiColor.Default : KsTguiColor.Danger;

        var hasInternal = (_data.PressureChecks & VentPressureBound.InternalBound) != 0;
        var hasExternal = (_data.PressureChecks & VentPressureBound.ExternalBound) != 0;
        _internalCheckButton.Selected = hasInternal;
        _externalCheckButton.Selected = hasExternal;
        _internalTargetItem.Visible = hasInternal;
        _externalTargetItem.Visible = hasExternal;

        var editable = _hasAccess;
        _powerButton.Disabled = !editable;
        _copyButton.Disabled = !editable;
        _directionButton.Disabled = !editable;
        _internalCheckButton.Disabled = !editable;
        _externalCheckButton.Disabled = !editable;
        _internalBound.Disabled = !editable;
        _externalBound.Disabled = !editable;
    }
}
