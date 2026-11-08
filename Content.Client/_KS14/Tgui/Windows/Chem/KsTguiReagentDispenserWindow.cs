using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared.Chemistry;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Storage;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Tgui.Windows.Chem;

/// <summary>
///     The reagent dispenser laid out after tgui's <c>ChemDispenser</c>: a status section, the dispense section with the
///         amounts in its title row and a grid of reagents under it, and the beaker section. What SS14 has and tg lacks
///         (the jug eject buttons, the plumbing valve, the clear button) sits beside the controls it belongs to.
/// </summary>
public sealed partial class KsTguiReagentDispenserWindow : FancyWindow
{
    private static readonly ReagentDispenserDispenseAmount[] Amounts =
    [
        ReagentDispenserDispenseAmount.U1,
        ReagentDispenserDispenseAmount.U5,
        ReagentDispenserDispenseAmount.U10,
        ReagentDispenserDispenseAmount.U15,
        ReagentDispenserDispenseAmount.U20,
        ReagentDispenserDispenseAmount.U30,
        ReagentDispenserDispenseAmount.U40,
        ReagentDispenserDispenseAmount.U60,
        ReagentDispenserDispenseAmount.U120,
    ];

    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;

    private readonly Dictionary<ReagentDispenserDispenseAmount, KsTguiButton> _amountButtons = new();
    private readonly KsTguiProgressBar _fillBar;

    // tgui's AnimatedNumber on each reagent's volume. The rows are rebuilt on every state, and these outlive them.
    private readonly KsTguiAnimatedNumberPool<ReagentId> _quantityNumbers = new(MakeQuantityNumber);
    private bool _firstStateShown;
    private readonly BoxContainer _beakerContents;
    private readonly Label _beakerName;
    private readonly SpriteView _beakerView;

    public event Action<ReagentDispenserDispenseAmount>? OnAmountPressed;
    public event Action<ItemStorageLocation>? OnDispenseReagentButtonPressed;
    public event Action<ItemStorageLocation>? OnEjectJugButtonPressed;
    public event Action<ItemSlotButtonPressedEvent>? OnEjectPressed;
    public event Action? OnClearPressed;
    public event Action? OnToggleValveButtonPressed;

    public KsTguiReagentDispenserWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("reagent-dispenser-bound-user-interface-title");
        MinSize = new Vector2(565f, 300f);
        SetSize = new Vector2(565f, 620f);

        // Status: the beaker's fill where tg shows the dispenser's energy, and the plumbing valve.
        ValveButton = new KsTguiButton { Text = Loc.GetString("reagent-dispenser-window-valve-closed") };
        ValveButton.OnPressed += _ => OnToggleValveButtonPressed?.Invoke();

        _fillBar = new KsTguiProgressBar { HorizontalExpand = true };
        _fillBar.AddRange(KsTguiColor.Good, 0f, 1f);

        var statusList = new KsTguiLabeledList();
        var fillRow = statusList.AddItem(Loc.GetString("ks-tgui-chem-fill"));
        fillRow.XamlChildren.Add(_fillBar);

        var statusSection = new KsTguiSection
        {
            Title = Loc.GetString("ks-tgui-chem-status"),
            Buttons = ValveButton,
        };
        statusSection.AddChild(statusList);

        // Dispense: the amounts in the title row, as tg.
        var amountRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        foreach (var amount in Amounts)
        {
            var button = new KsTguiButton { Text = ((int)amount).ToString(), Icon = KsTguiIcons.Plus };
            var pressedAmount = amount;
            button.OnPressed += _ => OnAmountPressed?.Invoke(pressedAmount);
            _amountButtons[amount] = button;
            amountRow.AddChild(button);
        }

        ReagentGrid = new GridContainer { Columns = 3, HSeparationOverride = 2, VSeparationOverride = 2, HorizontalExpand = true };

        var dispenseSection = new KsTguiSection
        {
            Title = Loc.GetString("reagent-dispenser-window-amount-to-dispense-label"),
            Buttons = amountRow,
        };
        dispenseSection.AddChild(ReagentGrid);

        // Beaker: name and fill with its eject and clear buttons, then its contents, with the beaker drawn beside them.
        EjectButton = new KsTguiItemSlotButton { SlotId = SharedReagentDispenser.OutputSlotName };
        EjectButton.SlotPressed += message => OnEjectPressed?.Invoke(message);
        ClearButton = new KsTguiButton { Text = Loc.GetString("reagent-dispenser-window-clear-button"), Icon = KsTguiIcons.Trash };
        ClearButton.OnPressed += _ => OnClearPressed?.Invoke();

        var beakerList = new KsTguiLabeledList { HorizontalExpand = true };
        var beakerRow = beakerList.AddItem(Loc.GetString("ks-tgui-chem-beaker"));
        _beakerName = new Label();
        beakerRow.XamlChildren.Add(_beakerName);
        var beakerButtons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        beakerButtons.AddChild(ClearButton);
        beakerButtons.AddChild(EjectButton);
        beakerRow.Buttons = beakerButtons;

        _beakerContents = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        var contentsRow = beakerList.AddItem(Loc.GetString("ks-tgui-chem-contents"));
        contentsRow.XamlChildren.Add(_beakerContents);

        _beakerView = new SpriteView { Scale = new Vector2(3f, 3f), VerticalAlignment = VAlignment.Top };

        var beakerBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        beakerBody.AddChild(beakerList);
        beakerBody.AddChild(_beakerView);

        var beakerSection = new KsTguiSection
        {
            Title = Loc.GetString("ks-tgui-chem-beaker"),
            Fill = true,
        };
        beakerSection.AddChild(beakerBody);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
            Margin = new Thickness(6f),
            HorizontalExpand = true,
        };
        root.AddChild(statusSection);
        root.AddChild(dispenseSection);
        root.AddChild(beakerSection);

        // ChemDispenser.tsx is Window.Content scrollable (overflow-y: scroll), with only the Beaker section filling: the status and
        //      dispense sections are their own height, the beaker takes what is left, and the whole content scrolls when that is
        //      not enough. The bar's column is there whether or not anything overflows.
        var scroll = new KsTguiScrollContainer { HScrollEnabled = false, ReserveVertical = true, HorizontalExpand = true, VerticalExpand = true };
        scroll.AddChild(root);
        ContentsContainer.AddChild(scroll);
    }

    public KsTguiItemSlotButton EjectButton { get; }

    public KsTguiButton ClearButton { get; }

    public KsTguiButton ValveButton { get; }

    public GridContainer ReagentGrid { get; }

    public IReadOnlyDictionary<ReagentDispenserDispenseAmount, KsTguiButton> AmountButtons => _amountButtons;

    public void UpdateState(ReagentDispenserBoundUserInterfaceState state)
    {
        UpdateContainerInfo(state);
        UpdateReagentsList(state.Inventory);

        _entityManager.TryGetEntity(state.OutputContainerEntity, out var outputContainerEntity);
        _beakerView.SetEntity(outputContainerEntity);

        ClearButton.Disabled = state.OutputContainer is null;
        EjectButton.Filled = state.OutputContainer is not null;

        ValveButton.Text = Loc.GetString(state.ValveOpen
            ? "reagent-dispenser-window-valve-open"
            : "reagent-dispenser-window-valve-closed");
        ValveButton.Selected = state.ValveOpen;

        foreach (var (amount, button) in _amountButtons)
        {
            button.Selected = amount == state.SelectedDispenseAmount;
        }
    }

    private void UpdateReagentsList(List<ReagentInventoryItem> inventory)
    {
        ReagentGrid.RemoveAllChildren();

        inventory.Sort((x, y) => string.Compare(x.ReagentLabel, y.ReagentLabel, StringComparison.Ordinal));

        foreach (var item in inventory)
        {
            ReagentGrid.AddChild(MakeReagentCard(item));
        }
    }

    private Control MakeReagentCard(ReagentInventoryItem item)
    {
        var strip = new PanelContainer
        {
            SetWidth = 4f,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat { BackgroundColor = item.ReagentColor },
        };

        var dispense = new KsTguiButton
        {
            Text = item.ReagentLabel,
            Icon = KsTguiIcons.Tint,
            Fluid = true,
            Disabled = item.Quantity == FixedPoint2.Zero,
        };
        dispense.Label.ClipText = true;
        dispense.LeftAligned = true; // a long name is cut at its end, not at both ends
        dispense.OnPressed += _ => OnDispenseReagentButtonPressed?.Invoke(item.StorageLocation);

        var quantity = new Label
        {
            Text = Loc.GetString("reagent-dispenser-window-quantity-label-text", ("quantity", item.Quantity)),
            StyleClasses = { StyleClass.LabelWeak },
            VerticalAlignment = VAlignment.Center,
        };

        var eject = new KsTguiButton { Icon = KsTguiIcons.Eject, Compact = true };
        eject.OnPressed += _ => OnEjectJugButtonPressed?.Invoke(item.StorageLocation);

        var card = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 2,
        };
        card.AddChild(strip);
        card.AddChild(dispense);
        card.AddChild(quantity);
        card.AddChild(eject);
        return card;
    }

    private static string FormatVolume(float volume)
    {
        return volume.ToString("0.##");
    }

    private static KsTguiAnimatedNumber MakeQuantityNumber()
    {
        return new KsTguiAnimatedNumber
        {
            StyleClasses = { StyleClass.LabelWeak },
            Format = displayed => Loc.GetString("reagent-dispenser-window-quantity-label-text", ("quantity", FormatVolume(displayed))),
        };
    }

    /// <summary>
    ///     Where a volume seen for the first time starts. tgui starts them all from 0 (<c>initial={0}</c>), which counts every
    ///         one up each time the window opens, so that is kept for what arrives while it is open.
    /// </summary>
    private float? NewVolumeStart => _firstStateShown ? 0f : null;

    private void UpdateContainerInfo(ReagentDispenserBoundUserInterfaceState state)
    {
        UpdateBeaker(state);
        _quantityNumbers.Sweep();
        _firstStateShown = true;
    }

    private void UpdateBeaker(ReagentDispenserBoundUserInterfaceState state)
    {
        _beakerContents.RemoveAllChildren();

        if (state.OutputContainer is null)
        {
            _beakerName.Text = Loc.GetString("reagent-dispenser-window-no-container-loaded-text");
            _fillBar.Value = 0f;
            _fillBar.Text = "";
            _beakerContents.AddChild(new Label { Text = Loc.GetString("ks-tgui-chem-nothing") });
            return;
        }

        var container = state.OutputContainer;
        _beakerName.Text = container.DisplayName;
        _fillBar.Value = container.MaxVolume > FixedPoint2.Zero
            ? (float)container.CurrentVolume / (float)container.MaxVolume
            : 0f;
        var maxVolume = container.MaxVolume;
        _fillBar.ShowNumber(
            (float)container.CurrentVolume,
            displayed => $"{FormatVolume(displayed)}/{maxVolume}",
            initial: NewVolumeStart);

        if (container.Reagents is not { Count: > 0 })
        {
            _beakerContents.AddChild(new Label { Text = Loc.GetString("ks-tgui-chem-nothing") });
            return;
        }

        foreach (var (reagent, quantity) in container.Reagents)
        {
            var localizedName = _prototypeManager.TryIndex(reagent.Prototype, out ReagentPrototype? prototype)
                ? prototype.LocalizedName
                : Loc.GetString("reagent-dispenser-window-reagent-name-not-found-text");

            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            row.AddChild(new Label { Text = $"{localizedName}: " });
            var quantityNumber = _quantityNumbers.Get(reagent);
            quantityNumber.Initial = NewVolumeStart;
            quantityNumber.Value = (float)quantity;
            row.AddChild(quantityNumber);
            _beakerContents.AddChild(row);
        }
    }
}
