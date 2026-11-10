using Content.Client._KS14.Tgui.Controls;
using Content.Client.Materials;
using Content.Client.Materials.UI;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui.Windows.Materials;

/// <summary>
///     The material bar of a machine with <see cref="MaterialStorageComponent"/>, a lathe's or the silo's: one
///         <see cref="KsTguiMaterialEntry"/> per stored material in equal cells. Follows the owner's storage every frame, as the
///         stock list does. Entries are kept and updated in place; one is added when a material appears and removed when it
///         runs out, and they stay in order of their localised names.
/// </summary>
public sealed partial class KsTguiMaterialList : BoxContainer
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private readonly MaterialStorageSystem _materialStorage;
    private readonly SpriteSystem _spriteSystem;
    private readonly MaterialStorageUIController _materialUiController;
    private readonly KsTguiCardGrid _grid;
    private readonly Label _noMaterialsLabel;
    private readonly Label _siloLinkedLabel;
    private readonly Dictionary<ProtoId<MaterialPrototype>, KsTguiMaterialEntry> _entries = new();

    private EntityUid? _owner;

    /// <summary>
    ///     Whether the owner's storage has been read since <see cref="SetOwner"/>. What is there then is placed; a material
    ///         that arrives afterwards counts up from nothing, as nothing counts up while a window opens.
    /// </summary>
    private bool _populated;

    public KsTguiMaterialList()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;

        _materialStorage = _entityManager.System<MaterialStorageSystem>();
        _spriteSystem = _entityManager.System<SpriteSystem>();
        _materialUiController = UserInterfaceManager.GetUIController<MaterialStorageUIController>();

        _grid = new KsTguiCardGrid { HorizontalExpand = true, Gap = 8f, VerticalGap = 4f };
        _noMaterialsLabel = new Label { Text = Loc.GetString("lathe-menu-no-materials-message"), HorizontalAlignment = HAlignment.Center };
        _siloLinkedLabel = new Label { Text = Loc.GetString("lathe-menu-silo-linked-message"), HorizontalAlignment = HAlignment.Center, Visible = false };
        _siloLinkedLabel.AddStyleClass("LabelSubText");

        AddChild(_noMaterialsLabel);
        AddChild(_grid);
        AddChild(_siloLinkedLabel);
    }

    public IReadOnlyDictionary<ProtoId<MaterialPrototype>, KsTguiMaterialEntry> Entries => _entries;

    public void SetOwner(EntityUid owner)
    {
        _owner = owner;
        _populated = false;
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

        _siloLinkedLabel.Visible = _entityManager.TryGetComponent<OreSiloClientComponent>(owner, out var client) && client.Silo != null;

        var canEject = materialStorage.CanEjectStoredMaterials;
        var materials = _materialStorage.GetStoredMaterials((owner, materialStorage));

        var membershipChanged = false;

        // Entries whose material has run out.
        List<ProtoId<MaterialPrototype>>? gone = null;
        foreach (var material in _entries.Keys)
        {
            if (!materials.TryGetValue(material, out var volume) || volume <= 0)
                (gone ??= new()).Add(material);
        }

        if (gone != null)
        {
            foreach (var material in gone)
            {
                _entries.Remove(material, out var entry);
                _grid.RemoveChild(entry!);
            }

            membershipChanged = true;
        }

        foreach (var (material, volume) in materials)
        {
            if (volume <= 0)
                continue;

            if (!_entries.TryGetValue(material, out var entry))
            {
                entry = MakeEntry(owner, material);
                if (_populated)
                    entry.AmountNumber.Initial = 0f;

                _entries[material] = entry;
                membershipChanged = true;
            }

            entry.CanEject = canEject;
            entry.SetAmount((float)volume / _materialStorage.GetSheetVolume(_prototypeManager.Index(material)));
        }

        _populated = true;

        if (membershipChanged)
            Reorder();

        _noMaterialsLabel.Visible = _entries.Count == 0;
    }

    /// <summary>
    ///     Puts the entries in the grid in order of name. The controls themselves are kept, so their numbers keep easing.
    /// </summary>
    private void Reorder()
    {
        _grid.RemoveAllChildren();

        var ordered = new List<KsTguiMaterialEntry>(_entries.Values);
        ordered.Sort((left, right) =>
        {
            var byName = string.Compare(left.MaterialName, right.MaterialName, StringComparison.CurrentCultureIgnoreCase);
            return byName != 0 ? byName : string.CompareOrdinal(left.Material.Id, right.Material.Id);
        });

        foreach (var entry in ordered)
            _grid.AddChild(entry);
    }

    private KsTguiMaterialEntry MakeEntry(EntityUid owner, ProtoId<MaterialPrototype> material)
    {
        var materialPrototype = _prototypeManager.Index(material);
        return new KsTguiMaterialEntry(
            material,
            _spriteSystem.Frame0(materialPrototype.Icon),
            Loc.GetString(materialPrototype.Unit),
            Loc.GetString(materialPrototype.Name),
            sheets => _materialUiController.SendLatheEjectMessage(owner, material, sheets));
    }
}
