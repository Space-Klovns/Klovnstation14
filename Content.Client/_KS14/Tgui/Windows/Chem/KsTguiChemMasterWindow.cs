using System.Linq;
using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared.Chemistry;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Tgui.Windows.Chem;

/// <summary>
///     The ChemMaster laid out after tgui's <c>ChemMaster</c>: one scrolling page of sections (the input container, the
///         buffer, the output container and packaging) in place of the stock window's two tabs. The reagent rows carry
///         the stock transfer amounts, and the plumbing valve, the sort cycle and the draw source keep their buttons.
/// </summary>
public sealed partial class KsTguiChemMasterWindow : FancyWindow
{
    private const string PillsRsiPath = "/Textures/Objects/Specific/Chemistry/pills.rsi";

    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;

    private readonly KsTguiSection _inputSection;
    private readonly KsTguiSection _bufferSection;
    private readonly KsTguiSection _outputSection;
    private readonly BoxContainer _inputInfo;
    private readonly BoxContainer _bufferInfo;
    private readonly BoxContainer _outputInfo;
    private readonly Label _drawSourceLabel;
    private readonly Label _bufferVolumeLabel;
    private readonly KsTguiButton _sortButton;

    public event Action<ItemSlotButtonPressedEvent>? OnInputEjectPressed;
    public event Action<ItemSlotButtonPressedEvent>? OnOutputEjectPressed;
    public event Action? OnToggleValveButtonPressed;
    public event Action<ChemMasterMode>? OnModePressed;
    public event Action? OnSortPressed;
    public event Action<ChemMasterDrawSource>? OnDrawSourcePressed;
    public event Action<uint>? OnPillTypePressed;
    public event Action<uint, uint, string>? OnCreatePillsPressed;
    public event Action<uint, string>? OnCreateBottlePressed;
    public event Action<ReagentId, ChemMasterReagentAmount, bool>? OnReagentButtonPressed;

    public KsTguiChemMasterWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("chem-master-bound-user-interface-title");
        MinSize = new Vector2(640f, 360f);
        SetSize = new Vector2(640f, 760f);

        // Input container.
        ValveButton = new KsTguiButton { Text = Loc.GetString("chem-master-window-valve-closed") };
        ValveButton.OnPressed += _ => OnToggleValveButtonPressed?.Invoke();
        InputEjectButton = new KsTguiItemSlotButton { SlotId = SharedChemMaster.InputSlotName };
        InputEjectButton.SlotPressed += message => OnInputEjectPressed?.Invoke(message);
        _inputInfo = MakeInfoBox();
        _inputSection = new KsTguiSection
        {
            Title = Loc.GetString("ks-tgui-chem-input-container"),
            Buttons = MakeRow(ValveButton, InputEjectButton),
        };
        _inputSection.AddChild(_inputInfo);

        // Buffer.
        _sortButton = new KsTguiButton { Text = Loc.GetString("chem-master-window-sort-type-none"), Icon = KsTguiIcons.Exchange };
        _sortButton.OnPressed += _ => OnSortPressed?.Invoke();
        BufferTransferButton = new KsTguiButton { Text = Loc.GetString("chem-master-window-transfer-button"), Icon = KsTguiIcons.Exchange };
        BufferTransferButton.OnPressed += _ => OnModePressed?.Invoke(ChemMasterMode.Transfer);
        BufferDiscardButton = new KsTguiButton { Text = Loc.GetString("chem-master-window-discard-button"), Icon = KsTguiIcons.Trash };
        BufferDiscardButton.OnPressed += _ => OnModePressed?.Invoke(ChemMasterMode.Discard);
        _bufferInfo = MakeInfoBox();
        _bufferSection = new KsTguiSection
        {
            Title = Loc.GetString("chem-master-window-buffer-text"),
            Buttons = MakeRow(_sortButton, BufferTransferButton, BufferDiscardButton),
        };
        _bufferSection.AddChild(_bufferInfo);

        // Output container.
        OutputEjectButton = new KsTguiItemSlotButton { SlotId = SharedChemMaster.OutputSlotName };
        OutputEjectButton.SlotPressed += message => OnOutputEjectPressed?.Invoke(message);
        _outputInfo = MakeInfoBox();
        _outputSection = new KsTguiSection
        {
            Title = Loc.GetString("ks-tgui-chem-output-container"),
            Buttons = OutputEjectButton,
        };
        _outputSection.AddChild(_outputInfo);

        // Packaging.
        OutputBufferDraw = new KsTguiButton { Text = Loc.GetString("chem-master-output-buffer-draw") };
        OutputBufferDraw.OnPressed += _ => OnDrawSourcePressed?.Invoke(ChemMasterDrawSource.Internal);
        OutputBeakerDraw = new KsTguiButton { Text = Loc.GetString("chem-master-output-beaker-draw") };
        OutputBeakerDraw.OnPressed += _ => OnDrawSourcePressed?.Invoke(ChemMasterDrawSource.External);

        _drawSourceLabel = new Label();
        _bufferVolumeLabel = new Label { StyleClasses = { StyleClass.LabelWeak } };

        var sourceTitle = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, VerticalAlignment = Control.VAlignment.Center };
        sourceTitle.AddChild(new Label
        {
            Text = Loc.GetString("chem-master-output-source"),
            StyleClasses = { StyleClass.LabelWeak },
            Margin = new Thickness(0f, 0f, 5f, 0f),
        });
        sourceTitle.AddChild(OutputBufferDraw);
        sourceTitle.AddChild(OutputBeakerDraw);

        LabelLineEdit = new LineEdit { HorizontalExpand = true };
        LabelLineEdit.IsValid = text => text.Length <= SharedChemMaster.LabelMaxLength;

        PillTypeButtons = MakePillTypeButtons(out var pillGrid);

        PillNumber = new KsTguiNumberInput { Format = "0", WidthPixels = 100f };
        PillDosage = new KsTguiNumberInput { Format = "0", WidthPixels = 100f, MinValue = 1f };
        PillDosage.SetValue(1f);
        BottleDosage = new KsTguiNumberInput { Format = "0", WidthPixels = 100f };

        CreatePillButton = new KsTguiButton { Text = Loc.GetString("chem-master-window-create-button"), Icon = KsTguiIcons.Flask };
        CreatePillButton.OnPressed += _ => OnCreatePillsPressed?.Invoke(
            (uint)PillDosage.Value, (uint)PillNumber.Value, LabelLine);
        CreateBottleButton = new KsTguiButton { Text = Loc.GetString("chem-master-window-create-button"), Icon = KsTguiIcons.Flask };
        CreateBottleButton.OnPressed += _ => OnCreateBottlePressed?.Invoke((uint)BottleDosage.Value, LabelLine);

        var packagingList = new KsTguiLabeledList();
        // The source is the toggle in the title row, so it has no row of its own.
        var labelRow = packagingList.AddItem(Loc.GetString("chem-master-current-text-label").TrimEnd(':'));
        labelRow.XamlChildren.Add(LabelLineEdit);
        var typeRow = packagingList.AddItem(Loc.GetString("chem-master-window-pill-type-label").TrimEnd(':'));
        typeRow.XamlChildren.Add(pillGrid);
        var pillsRow = packagingList.AddItem(Loc.GetString("chem-master-window-pills-label").TrimEnd(':'));
        pillsRow.XamlChildren.Add(MakeRow(
            LabelOf("chem-master-window-pills-number-label"), PillNumber,
            LabelOf("chem-master-window-dose-label"), PillDosage,
            CreatePillButton));
        var bottlesRow = packagingList.AddItem(Loc.GetString("chem-master-window-bottles-label").TrimEnd(':'));
        bottlesRow.XamlChildren.Add(MakeRow(
            LabelOf("chem-master-window-dose-label"), BottleDosage,
            CreateBottleButton));

        var packagingSection = new KsTguiSection
        {
            Title = Loc.GetString("chem-master-window-packaging-text"),
            Buttons = sourceTitle,
        };
        packagingSection.AddChild(packagingList);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
            Margin = new Thickness(6f),
            HorizontalExpand = true,
        };
        root.AddChild(_inputSection);
        root.AddChild(_bufferSection);
        root.AddChild(_outputSection);
        root.AddChild(packagingSection);

        var scroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        scroll.AddChild(root);
        ContentsContainer.AddChild(scroll);
    }

    public KsTguiButton ValveButton { get; }

    public KsTguiItemSlotButton InputEjectButton { get; }

    public KsTguiItemSlotButton OutputEjectButton { get; }

    public KsTguiButton BufferTransferButton { get; }

    public KsTguiButton BufferDiscardButton { get; }

    public KsTguiButton OutputBufferDraw { get; }

    public KsTguiButton OutputBeakerDraw { get; }

    public KsTguiButton CreatePillButton { get; }

    public KsTguiButton CreateBottleButton { get; }

    public KsTguiButton SortButton => _sortButton;

    public KsTguiButton[] PillTypeButtons { get; }

    public LineEdit LabelLineEdit { get; }

    public KsTguiNumberInput PillNumber { get; }

    public KsTguiNumberInput PillDosage { get; }

    public KsTguiNumberInput BottleDosage { get; }

    public string LabelLine
    {
        get => LabelLineEdit.Text;
        set => LabelLineEdit.Text = value;
    }

    private static Label LabelOf(string key)
    {
        return new Label { Text = Loc.GetString(key), StyleClasses = { StyleClass.LabelWeak }, Margin = new Thickness(5f, 0f, 3f, 0f) };
    }

    private static BoxContainer MakeInfoBox()
    {
        return new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
    }

    private static BoxContainer MakeRow(params Control[] children)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        foreach (var child in children)
        {
            row.AddChild(child);
        }

        return row;
    }

    private KsTguiButton[] MakePillTypeButtons(out GridContainer grid)
    {
        var sprite = _entityManager.System<SpriteSystem>();
        var resourcePath = new ResPath(PillsRsiPath);

        grid = new GridContainer { Columns = 10, HSeparationOverride = 1, VSeparationOverride = 1 };
        var buttons = new KsTguiButton[SharedChemMaster.PillTypes];
        for (uint i = 0; i < buttons.Length; i++)
        {
            var pillType = i;
            var button = new KsTguiButton { MaxSize = new Vector2(42f, 28f) };
            button.AddChild(new TextureRect
            {
                Texture = sprite.Frame0(new SpriteSpecifier.Rsi(resourcePath, "pill" + (i + 1))),
                TextureScale = new Vector2(1.75f, 1.75f),
                Stretch = TextureRect.StretchMode.KeepCentered,
                MouseFilter = Control.MouseFilterMode.Ignore,
            });
            button.OnPressed += _ => OnPillTypePressed?.Invoke(pillType);
            buttons[i] = button;
            grid.AddChild(button);
        }

        return buttons;
    }

    public void UpdateState(ChemMasterBoundUserInterfaceState state)
    {
        if (state.UpdateLabel)
            LabelLine = GenerateLabel(state);

        UpdatePanelInfo(state);

        switch (state.DrawSource)
        {
            case ChemMasterDrawSource.Internal:
                SetBufferText(state.BufferCurrentVolume, "chem-master-output-buffer-draw");
                break;
            case ChemMasterDrawSource.External:
                SetBufferText(state.InputContainerInfo?.CurrentVolume, "chem-master-output-beaker-draw");
                break;
            default:
                throw new($"Chemmaster {state.OutputContainerInfo} draw source is not set");
        }

        InputEjectButton.Filled = state.InputContainerInfo is not null;
        OutputEjectButton.Filled = state.OutputContainerInfo is not null;
        CreateBottleButton.Disabled = state.OutputContainerInfo?.Reagents == null;

        ValveButton.Text = Loc.GetString(state.ValveOpen
            ? "chem-master-window-valve-open"
            : "chem-master-window-valve-closed");
        ValveButton.Selected = state.ValveOpen;

        UpdateDosageFields(state);
    }

    private void SetBufferText(FixedPoint2? volume, string text)
    {
        _bufferVolumeLabel.Text = $" {volume ?? FixedPoint2.Zero}u";
        _drawSourceLabel.Text = Loc.GetString(text);
    }

    private void UpdateDosageFields(ChemMasterBoundUserInterfaceState state)
    {
        var output = state.OutputContainerInfo;
        var remainingCapacity = output is null ? 0 : (output.MaxVolume - output.CurrentVolume).Int();
        var holdsReagents = output?.Reagents != null;
        var pillNumberMax = holdsReagents ? 0 : remainingCapacity;
        var bottleAmountMax = holdsReagents ? remainingCapacity : 0;
        var outputVolume = state.DrawSource switch
        {
            ChemMasterDrawSource.Internal => state.BufferCurrentVolume?.Int() ?? 0,
            ChemMasterDrawSource.External => state.InputContainerInfo?.CurrentVolume.Int() ?? 0,
            _ => 0,
        };

        PillDosage.MaxValue = state.PillDosageLimit;
        PillDosage.SetValue((int)Math.Min(outputVolume, state.PillDosageLimit));

        for (var i = 0; i < PillTypeButtons.Length; i++)
        {
            PillTypeButtons[i].Selected = i == state.SelectedPillType;
        }

        PillNumber.MaxValue = pillNumberMax;
        BottleDosage.MaxValue = bottleAmountMax;

        // Avoid division by zero
        PillNumber.SetValue(PillDosage.Value > 0
            ? Math.Min(outputVolume / PillDosage.Value, pillNumberMax)
            : 0);

        BottleDosage.SetValue(Math.Min(bottleAmountMax, outputVolume));
    }

    /// <summary>
    ///     A product label from the reagent there is least of in the buffer or beaker.
    /// </summary>
    private string GenerateLabel(ChemMasterBoundUserInterfaceState state)
    {
        if (
            state.BufferCurrentVolume == 0 && state.DrawSource == ChemMasterDrawSource.Internal ||
            state.InputContainerInfo?.CurrentVolume == 0 && state.DrawSource == ChemMasterDrawSource.External ||
            state.InputContainerInfo?.Reagents == null
        )
            return "";

        var reagent = (state.DrawSource switch
        {
            ChemMasterDrawSource.Internal => state.BufferReagents,
            ChemMasterDrawSource.External => state.InputContainerInfo.Reagents ?? [],
            _ => throw new($"Chemmaster {state.OutputContainerInfo} draw source is not set"),
        }).MinBy(r => r.Quantity)
            .Reagent;
        _prototypeManager.TryIndex(reagent.Prototype, out ReagentPrototype? prototype);
        return prototype?.LocalizedName ?? "";
    }

    private void UpdatePanelInfo(ChemMasterBoundUserInterfaceState state)
    {
        // tg's one toggle is green while moving reagents and red while destroying them (ChemMaster.tsx:199), so the chosen
        //      mode takes its colour here and not Selected's green.
        BufferTransferButton.Selected = state.Mode == ChemMasterMode.Transfer;
        BufferDiscardButton.Color = state.Mode == ChemMasterMode.Discard ? KsTguiColor.Red : KsTguiColor.Default;

        BuildContainerUi(_inputInfo, state.InputContainerInfo, addReagentButtons: true);
        BuildContainerUi(_outputInfo, state.OutputContainerInfo, addReagentButtons: false);

        _bufferInfo.RemoveAllChildren();

        _sortButton.Text = state.SortingType switch
        {
            ChemMasterSortingType.Alphabetical => Loc.GetString("chem-master-window-sort-type-alphabetical"),
            ChemMasterSortingType.Quantity => Loc.GetString("chem-master-window-sort-type-quantity"),
            ChemMasterSortingType.Latest => Loc.GetString("chem-master-window-sort-type-latest"),
            _ => Loc.GetString("chem-master-window-sort-type-none"),
        };

        OutputBufferDraw.Selected = state.DrawSource == ChemMasterDrawSource.Internal;
        OutputBeakerDraw.Selected = state.DrawSource == ChemMasterDrawSource.External;

        if (!state.BufferReagents.Any())
        {
            _bufferInfo.AddChild(new Label { Text = Loc.GetString("chem-master-window-buffer-empty-text") });
            return;
        }

        var volumeRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        volumeRow.AddChild(new Label { Text = $"{Loc.GetString("chem-master-window-buffer-label")} " });
        volumeRow.AddChild(new Label { Text = $"{state.BufferCurrentVolume}u", StyleClasses = { StyleClass.LabelWeak } });
        _bufferInfo.AddChild(volumeRow);

        var reagentList = new List<(ReagentId ReagentId, string Name, Color Color, FixedPoint2 Quantity)>();
        foreach (var (reagent, quantity) in state.BufferReagents)
        {
            _prototypeManager.TryIndex(reagent.Prototype, out ReagentPrototype? prototype);
            var name = prototype?.LocalizedName ?? Loc.GetString("chem-master-window-unknown-reagent-text");
            reagentList.Add((reagent, name, prototype?.SubstanceColor ?? default(Color), quantity));
        }

        switch (state.SortingType)
        {
            case ChemMasterSortingType.Alphabetical:
                reagentList = reagentList.OrderBy(x => x.Name).ToList();
                break;
            case ChemMasterSortingType.Quantity:
                reagentList = reagentList.OrderByDescending(x => x.Quantity).ToList();
                break;
            case ChemMasterSortingType.Latest:
                reagentList = Enumerable.Reverse(reagentList).ToList();
                break;
        }

        foreach (var reagent in reagentList)
        {
            _bufferInfo.AddChild(BuildReagentRow(reagent.Color, reagent.Name, reagent.ReagentId, reagent.Quantity, true, true));
        }
    }

    private void BuildContainerUi(BoxContainer control, ContainerInfo? info, bool addReagentButtons)
    {
        control.RemoveAllChildren();

        if (info is null)
        {
            control.AddChild(new Label { Text = Loc.GetString("chem-master-window-no-container-loaded-text") });
            return;
        }

        var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        header.AddChild(new Label { Text = $"{info.DisplayName}: " });
        header.AddChild(new Label { Text = $"{info.CurrentVolume}/{info.MaxVolume}", StyleClasses = { StyleClass.LabelWeak } });
        control.AddChild(header);

        if (info.Entities != null)
        {
            foreach (var (id, quantity) in info.Entities.Select(x => (x.Id, x.Quantity)))
            {
                control.AddChild(BuildReagentRow(default, id, default, quantity, false, addReagentButtons));
            }
        }

        if (info.Reagents != null)
        {
            foreach (var reagent in info.Reagents)
            {
                _prototypeManager.TryIndex(reagent.Reagent.Prototype, out ReagentPrototype? prototype);
                var name = prototype?.LocalizedName ?? Loc.GetString("chem-master-window-unknown-reagent-text");
                control.AddChild(BuildReagentRow(
                    prototype?.SubstanceColor ?? default(Color), name, reagent.Reagent, reagent.Quantity, false, addReagentButtons));
            }
        }
    }

    /// <summary>
    ///     One reagent: name and amount at the left, its colour, then the transfer amounts when it can be moved.
    /// </summary>
    private Control BuildReagentRow(
        Color reagentColor,
        string name,
        ReagentId reagent,
        FixedPoint2 quantity,
        bool isBuffer,
        bool addReagentButtons)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 1 };
        row.AddChild(new Label { Text = $"{name}: " });
        row.AddChild(new Label { Text = $"{quantity}u", StyleClasses = { StyleClass.LabelWeak } });
        row.AddChild(new Control { HorizontalExpand = true });

        if (!addReagentButtons)
            return row;

        if (reagentColor != default(Color))
        {
            row.AddChild(new PanelContainer
            {
                VerticalExpand = true,
                MinWidth = 4f,
                Margin = new Thickness(0f, 1f, 4f, 1f),
                PanelOverride = new StyleBoxFlat { BackgroundColor = reagentColor },
            });
        }

        var amounts = new (string Text, ChemMasterReagentAmount Amount)[]
        {
            ("1", ChemMasterReagentAmount.U1),
            ("5", ChemMasterReagentAmount.U5),
            ("10", ChemMasterReagentAmount.U10),
            ("15", ChemMasterReagentAmount.U15),
            ("20", ChemMasterReagentAmount.U20),
            ("30", ChemMasterReagentAmount.U30),
            ("40", ChemMasterReagentAmount.U40),
            ("60", ChemMasterReagentAmount.U60),
            ("120", ChemMasterReagentAmount.U120),
            (Loc.GetString("chem-master-window-buffer-all-amount"), ChemMasterReagentAmount.All),
        };

        foreach (var (text, amount) in amounts)
        {
            var pressedAmount = amount;
            var button = new KsTguiButton { Text = text, Compact = true };
            button.OnPressed += _ => OnReagentButtonPressed?.Invoke(reagent, pressedAmount, isBuffer);
            row.AddChild(button);
        }

        return row;
    }
}
