using System.Globalization;
using Content.Client._KS14.Tgui.Controls;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Windows.Atmos;

/// <summary>
///     The gas filter after tgui's AtmosFilter: power in the section's header, then the transfer rate and a button per gas.
///         The "select all" and "deselect all" of the stock window stay, under the gases.
/// </summary>
public sealed class KsTguiGasFilterWindow : KsTguiGasDeviceWindow, IKsGasFilterWindow
{
    public event Action<bool>? ToggleStatusButtonPressed;
    public event Action<string>? FilterTransferRateChanged;
    public event Action<HashSet<Gas>>? FilterGasesChanged;

    private readonly Dictionary<Gas, KsTguiButton> _gasButtons = new();
    private readonly HashSet<Gas> _selectedGases = new();
    private readonly BoxContainer _gasBox;

    public KsTguiNumberInput RateInput { get; } = new() { Unit = Loc.GetString("ks-tgui-atmos-unit-ls"), MaxValue = Atmospherics.MaxTransferRate, WidthPixels = 63f, TickWhileDragging = true };
    public KsTguiButton MaxButtonControl { get; } = MaxButton();
    public KsTguiButton SelectAllButton { get; }
    public KsTguiButton DeselectAllButton { get; }

    public KsTguiGasFilterWindow() : base(width: 440f, sectionTitle: Loc.GetString("ks-tgui-atmos-filter-title"))
    {
        Section.Buttons = Power.Button;

        var rateRow = List.AddItem(Loc.GetString("ks-tgui-atmos-transfer-rate"));
        rateRow.XamlChildren.Add(RateInput);
        rateRow.Buttons = MaxButtonControl;

        _gasBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
        };
        List.AddItem(Loc.GetString("ks-tgui-atmos-filter-types")).XamlChildren.Add(_gasBox);

        SelectAllButton = new KsTguiButton { Text = Loc.GetString("ks-tgui-atmos-select-all"), Compact = true };
        DeselectAllButton = new KsTguiButton { Text = Loc.GetString("ks-tgui-atmos-deselect-all"), Compact = true };
        var bulk = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        bulk.AddChild(SelectAllButton);
        bulk.AddChild(DeselectAllButton);
        List.AddItem(string.Empty).XamlChildren.Add(bulk);

        Power.Toggled += on => ToggleStatusButtonPressed?.Invoke(on);
        RateInput.OnCommitted += value => FilterTransferRateChanged?.Invoke(value.ToString(CultureInfo.CurrentCulture));
        MaxButtonControl.OnPressed += _ =>
        {
            RateInput.SetValue(RateInput.MaxValue);
            FilterTransferRateChanged?.Invoke(RateInput.MaxValue.ToString(CultureInfo.CurrentCulture));
        };
        SelectAllButton.OnPressed += _ => SetAll(selected: true);
        DeselectAllButton.OnPressed += _ => SetAll(selected: false);
    }

    public IReadOnlyDictionary<Gas, KsTguiButton> GasButtons => _gasButtons;

    public void SetTransferRate(float rate)
    {
        RateInput.SetValue(rate);
        MaxButtonControl.Disabled = rate >= RateInput.MaxValue;
    }

    public void SetFilterStatus(bool enabled) => Power.SetOn(enabled);

    public void SetFilteredGases(HashSet<Gas> gases)
    {
        _selectedGases.Clear();
        _selectedGases.UnionWith(gases);
        foreach (var (gas, button) in _gasButtons)
            RefreshButton(gas, button);
    }

    public void PopulateGasList(IEnumerable<GasPrototype> gases)
    {
        // Three buttons to a row, as the stock window lays them out.
        BoxContainer? row = null;
        foreach (var gasPrototype in gases)
        {
            if (!Enum.TryParse<Gas>(gasPrototype.ID, out var gas) || _gasButtons.ContainsKey(gas))
                continue;

            if (row == null || row.ChildCount >= 3)
            {
                row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 2 };
                _gasBox.AddChild(row);
            }

            var button = new KsTguiButton
            {
                Text = Loc.GetString(gasPrototype.Name),
                HorizontalAlignment = Control.HAlignment.Stretch,
                HorizontalExpand = true,
            };
            var capturedGas = gas;
            button.OnPressed += _ => ToggleGas(capturedGas);
            _gasButtons.Add(gas, button);
            row.AddChild(button);
            RefreshButton(gas, button);
        }
    }

    private void ToggleGas(Gas gas)
    {
        if (!_selectedGases.Remove(gas))
            _selectedGases.Add(gas);

        RefreshButton(gas, _gasButtons[gas]);
        FilterGasesChanged?.Invoke(new HashSet<Gas>(_selectedGases));
    }

    private void SetAll(bool selected)
    {
        _selectedGases.Clear();
        if (selected)
            _selectedGases.UnionWith(_gasButtons.Keys);

        foreach (var (gas, button) in _gasButtons)
            RefreshButton(gas, button);

        FilterGasesChanged?.Invoke(new HashSet<Gas>(_selectedGases));
    }

    private void RefreshButton(Gas gas, KsTguiButton button)
    {
        var selected = _selectedGases.Contains(gas);
        button.Selected = selected;
        button.Icon = selected ? KsTguiIcons.CheckSquare : KsTguiIcons.Square;
    }
}
