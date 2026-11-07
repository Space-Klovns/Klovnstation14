using Content.Client._KS14.Tgui.Controls;
using Content.Client.Materials;
using Content.Client.Materials.UI;
using Content.Shared.Materials;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using XColor = Robust.Shared.Maths.Color;

namespace Content.Client._KS14.Tgui.Windows.Lathe;

/// <summary>
///     tg's Autolathe header (<c>Autolathe.tsx</c>): a <c>Section</c> "Total Materials" holding a <c>LabeledList</c> with a
///         bar of how full the storage is and, under a <c>Collapsible</c>, one row per stored material (a bar in the
///         material's colour, its sheets, and "Eject:" x1, x5 and x all). Follows the owner's storage every frame, as
///         <see cref="Windows.Materials.KsTguiMaterialList"/> does.
/// </summary>
/// <remarks>
///     Sheets. The bar's value and maximum are volumes, as tg's are: the volumes stored, and
///         <see cref="MaterialStorageComponent.StorageLimit"/>. Its text turns both into sheets with
///         <see cref="StandardSheetVolume"/>, so the text and the fill agree whatever the materials are. A material's own row
///         uses that material's sheet volume (<c>GetSheetVolume</c>), which is what an eject takes out. A storage with no limit
///         has no maximum to be a fraction of: the bar is then full, in the default colour with no ranges, and says how many
///         sheets there are.
/// </remarks>
public sealed partial class KsTguiLatheMaterialSummary : BoxContainer
{
    /// <summary>
    ///     tg's <c>SHEET_MATERIAL_AMOUNT</c>. The storage system's default sheet volume, which it keeps private.
    /// </summary>
    public const int StandardSheetVolume = 100;

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private readonly MaterialStorageSystem _materialStorage;
    private readonly MaterialStorageUIController _materialUiController;
    private readonly KsTguiProgressBar _totalBar;
    private readonly KsTguiCollapsible _collapsible;
    private readonly KsTguiLabeledListItem _collapsibleItem;
    private readonly KsTguiLabeledList _materialsList;
    private readonly Dictionary<ProtoId<MaterialPrototype>, MaterialRow> _rows = new();

    private EntityUid? _owner;

    public KsTguiLatheMaterialSummary()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;

        _materialStorage = _entityManager.System<MaterialStorageSystem>();
        _materialUiController = UserInterfaceManager.GetUIController<MaterialStorageUIController>();

        _totalBar = new KsTguiProgressBar { HorizontalExpand = true };

        _materialsList = new KsTguiLabeledList();
        _collapsible = new KsTguiCollapsible { Title = Loc.GetString("lathe-menu-materials-title"), Visible = false };
        _collapsible.AddChild(_materialsList);

        var list = new KsTguiLabeledList();
        list.AddItem(Loc.GetString("ks-tgui-lathe-total-materials")).AddChild(_totalBar);
        _collapsibleItem = new KsTguiLabeledListItem();
        _collapsibleItem.AddChild(_collapsible);
        list.AddChild(_collapsibleItem);

        var section = new KsTguiSection { Title = Loc.GetString("ks-tgui-lathe-total-materials") };
        section.AddChild(list);
        AddChild(section);
    }

    public KsTguiProgressBar TotalBar => _totalBar;

    public KsTguiCollapsible MaterialsCollapsible => _collapsible;

    public IReadOnlyDictionary<ProtoId<MaterialPrototype>, MaterialRow> Rows => _rows;

    public void SetOwner(EntityUid owner)
    {
        _owner = owner;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_owner is not { } owner)
            return;

        if (_entityManager.Deleted(owner) || !_entityManager.TryGetComponent<MaterialStorageComponent>(owner, out var materialStorage))
        {
            _owner = null;
            return;
        }

        var stored = _materialStorage.GetStoredMaterials((owner, materialStorage));
        var canEject = materialStorage.CanEjectStoredMaterials;

        var membershipChanged = false;
        List<ProtoId<MaterialPrototype>>? gone = null;
        foreach (var material in _rows.Keys)
        {
            if (!stored.TryGetValue(material, out var volume) || volume <= 0)
                (gone ??= new()).Add(material);
        }

        if (gone != null)
        {
            foreach (var material in gone)
                _rows.Remove(material);

            membershipChanged = true;
        }

        var totalVolume = 0;
        foreach (var (material, volume) in stored)
        {
            if (volume <= 0)
                continue;

            totalVolume += volume;
            if (!_rows.ContainsKey(material))
            {
                _rows[material] = MakeRow(owner, material);
                membershipChanged = true;
            }
        }

        // The maximum is what every bar is a fraction of.
        var maxVolume = (float)Math.Max(materialStorage.StorageLimit ?? totalVolume, 1);
        UpdateTotal(totalVolume, materialStorage.StorageLimit, maxVolume);

        if (membershipChanged)
            Reorder();

        foreach (var (material, row) in _rows)
            row.Update(stored[material], maxVolume, canEject);

        _collapsible.Visible = _rows.Count > 0;
    }

    private void UpdateTotal(int totalVolume, int? limit, float maxVolume)
    {
        _totalBar.MaxValue = maxVolume;
        _totalBar.Value = (float)totalVolume;

        if (limit is { } max)
        {
            // tg's ranges, as fractions of the maximum.
            var maxValue = (float)max;
            _totalBar.SetRanges(
                (KsTguiColor.Good, maxValue * 0.85f, maxValue),
                (KsTguiColor.Average, maxValue * 0.25f, maxValue * 0.85f),
                (KsTguiColor.Bad, 0f, maxValue * 0.25f));
            _totalBar.Text = Loc.GetString(
                "ks-tgui-lathe-total-sheets",
                ("total", Sheets(totalVolume)),
                ("max", Sheets(max)));
        }
        else
        {
            _totalBar.SetRanges();
            _totalBar.Text = Loc.GetString("ks-tgui-lathe-total-sheets-unlimited", ("total", Sheets(totalVolume)));
        }
    }

    private static float Sheets(float volume)
    {
        return MathF.Round(volume / (float)StandardSheetVolume, 2);
    }

    /// <summary>
    ///     Puts the rows in the list in order of name; the rows themselves are kept.
    /// </summary>
    private void Reorder()
    {
        _materialsList.RemoveAllChildren();

        var ordered = new List<MaterialRow>(_rows.Values);
        ordered.Sort((left, right) =>
        {
            var byName = string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            return byName != 0 ? byName : string.CompareOrdinal(left.Material.Id, right.Material.Id);
        });

        foreach (var row in ordered)
            _materialsList.AddChild(row.Item);
    }

    private MaterialRow MakeRow(EntityUid owner, ProtoId<MaterialPrototype> material)
    {
        var prototype = _prototypeManager.Index(material);
        return new MaterialRow(
            material,
            Capitalize(Loc.GetString(prototype.Name)),
            Loc.GetString(prototype.Unit),
            prototype.Color,
            _materialStorage.GetSheetVolume(prototype),
            sheets => _materialUiController.SendLatheEjectMessage(owner, material, sheets));
    }

    private static string Capitalize(string text)
    {
        // TODO SANDBOX: a char concatenated to a string compiles to a ReadOnlySpan<char> concat, which the whitelist refuses
        return text.Length == 0 ? text : text.Substring(0, 1).ToUpperInvariant() + text.Substring(1);
    }

    /// <summary>
    ///     One stored material: tg's <c>LabeledList.Item</c> with a mirrored bar, "Eject:" and the buttons.
    /// </summary>
    public sealed class MaterialRow
    {
        private readonly string _unit;
        private readonly Action<int> _eject;
        private readonly Label _ejectLabel;
        private readonly BoxContainer _buttonRow;
        private int _lastAll = -1;
        private int _volume;

        public MaterialRow(ProtoId<MaterialPrototype> material, string name, string unit, XColor color, int sheetVolume, Action<int> eject)
        {
            Material = material;
            Name = name;
            SheetVolume = Math.Max(1, sheetVolume);
            _unit = unit;
            _eject = eject;

            // tg: scaleX(-1), a value of max - amount, the material's colour behind and black for the fill.
            Bar = new KsTguiProgressBar
            {
                HorizontalExpand = true,
                Mirrored = true,
                BackgroundColor = color,
                Color = KsTguiColor.Black,
            };
            _ejectLabel = new Label { Text = Loc.GetString("ks-tgui-lathe-eject"), VerticalAlignment = VAlignment.Center };
            _buttonRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = KsTguiSection.Gap };

            // A Stack fill: the bar takes what the buttons leave, with a Stack's gap between.
            var stack = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = KsTguiSection.Gap, HorizontalExpand = true };
            stack.AddChild(Bar);
            stack.AddChild(_ejectLabel);
            stack.AddChild(_buttonRow);

            Item = new KsTguiLabeledListItem { Label = name };
            Item.AddChild(stack);
        }

        public ProtoId<MaterialPrototype> Material { get; }

        public string Name { get; }

        public int SheetVolume { get; }

        public KsTguiLabeledListItem Item { get; }

        public KsTguiProgressBar Bar { get; }

        /// <summary>
        ///     The eject buttons, in the order shown, with how many sheets each ejects.
        /// </summary>
        public List<(KsTguiButton Button, int Sheets)> EjectButtons { get; } = new();

        public void Update(int volume, float maxVolume, bool canEject)
        {
            _volume = volume;

            Bar.MaxValue = maxVolume;
            Bar.Value = maxVolume - (float)volume;
            Bar.Text = Loc.GetString(
                "lathe-menu-material-amount",
                ("amount", MathF.Round((float)volume / (float)SheetVolume, 2)),
                ("unit", _unit));

            _ejectLabel.Visible = canEject;
            _buttonRow.Visible = canEject;

            // The last button is "all", so it follows the amount.
            var all = volume / SheetVolume;
            if (_lastAll != all)
            {
                _lastAll = all;
                RebuildButtons(all);
            }

            foreach (var (button, sheets) in EjectButtons)
                button.Disabled = volume < SheetVolume * sheets;
        }

        /// <summary>
        ///     tg's <c>[1, 5, floor(amount / SHEET)].sort()</c>. Sorted by number (JS's default <c>sort</c> compares text, so
        ///         it would put x12 before x5), and each amount once: tg shows x5 twice with five sheets, and x0 under one.
        /// </summary>
        private void RebuildButtons(int all)
        {
            _buttonRow.RemoveAllChildren();
            EjectButtons.Clear();

            var amounts = new SortedSet<int> { 1, 5 };
            if (all > 0)
                amounts.Add(all);

            foreach (var amount in amounts)
            {
                var sheets = amount;
                var button = new KsTguiButton { Text = Loc.GetString("ks-tgui-lathe-eject-times", ("amount", sheets)) };
                button.OnPressed += _ => _eject(sheets);
                button.Disabled = _volume < SheetVolume * sheets;

                // Each is a Stack.Item of its own, so none is a row's neighbour and none keeps a margin.
                var cell = new Control();
                cell.AddChild(button);
                _buttonRow.AddChild(cell);
                EjectButtons.Add((button, sheets));
            }
        }
    }
}
