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

namespace Content.Client._KS14.Tgui.Windows.Lathe;

/// <summary>
///     The tgui fabricator's material bar: a counter with the stored sheets for each material and, when the storage allows
///         it, buttons that eject 1, 5 or 10 of them. Follows the owner's storage every frame, as the stock list does.
/// </summary>
public sealed partial class KsTguiLatheMaterials : BoxContainer
{
    private static readonly int[] EjectAmounts = [1, 5, 10];

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private readonly MaterialStorageSystem _materialStorage;
    private readonly SpriteSystem _spriteSystem;
    private readonly MaterialStorageUIController _materialUiController;
    private readonly WrapContainer _counters;
    private readonly Label _noMaterialsLabel;
    private readonly Label _siloLinkedLabel;

    private EntityUid? _owner;
    private Dictionary<ProtoId<MaterialPrototype>, int>? _currentMaterials;
    private bool _currentCanEject;

    public KsTguiLatheMaterials()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;

        _materialStorage = _entityManager.System<MaterialStorageSystem>();
        _spriteSystem = _entityManager.System<SpriteSystem>();
        _materialUiController = UserInterfaceManager.GetUIController<MaterialStorageUIController>();

        _counters = new WrapContainer { HorizontalExpand = true, SeparationOverride = 8, CrossSeparationOverride = 4 };
        _noMaterialsLabel = new Label { Text = Loc.GetString("lathe-menu-no-materials-message"), HorizontalAlignment = HAlignment.Center };
        _siloLinkedLabel = new Label { Text = Loc.GetString("lathe-menu-silo-linked-message"), HorizontalAlignment = HAlignment.Center, Visible = false };
        _siloLinkedLabel.AddStyleClass("LabelSubText");

        AddChild(_noMaterialsLabel);
        AddChild(_counters);
        AddChild(_siloLinkedLabel);
    }

    public void SetOwner(EntityUid owner)
    {
        _owner = owner;
        _currentMaterials = null;
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
        if (_currentMaterials != null && _currentCanEject == canEject && SameMaterials(_currentMaterials, materials))
            return;

        _currentMaterials = new Dictionary<ProtoId<MaterialPrototype>, int>(materials);
        _currentCanEject = canEject;

        _counters.RemoveAllChildren();
        foreach (var (material, volume) in materials)
        {
            if (volume > 0)
                _counters.AddChild(MakeCounter(owner, material, volume, canEject));
        }

        _noMaterialsLabel.Visible = _counters.ChildCount == 0;
    }

    private static bool SameMaterials(Dictionary<ProtoId<MaterialPrototype>, int> known, Dictionary<ProtoId<MaterialPrototype>, int> current)
    {
        if (known.Count != current.Count)
            return false;

        foreach (var (material, volume) in current)
        {
            if (!known.TryGetValue(material, out var knownVolume) || knownVolume != volume)
                return false;
        }

        return true;
    }

    private Control MakeCounter(EntityUid owner, ProtoId<MaterialPrototype> material, int volume, bool canEject)
    {
        var materialPrototype = _prototypeManager.Index(material);
        var sheetVolume = _materialStorage.GetSheetVolume(materialPrototype);
        var sheets = (float)volume / sheetVolume;
        var ejectableSheets = (int)MathF.Floor(sheets);

        var unit = Loc.GetString(materialPrototype.Unit);
        var amountText = Loc.GetString("lathe-menu-material-amount", ("amount", sheets), ("unit", unit));
        var displayText = Loc.GetString("lathe-menu-material-display", ("material", Loc.GetString(materialPrototype.Name)), ("amount", amountText));

        var counter = new BoxContainer { Orientation = LayoutOrientation.Vertical, MinWidth = 70, ToolTip = displayText };

        counter.AddChild(new TextureRect
        {
            Texture = _spriteSystem.Frame0(materialPrototype.Icon),
            MinSize = new System.Numerics.Vector2(32f, 32f),
            HorizontalAlignment = HAlignment.Center,
        });
        counter.AddChild(new Label { Text = amountText, HorizontalAlignment = HAlignment.Center });

        if (!canEject)
            return counter;

        var buttons = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalAlignment = HAlignment.Center, SeparationOverride = 1 };
        foreach (var amount in EjectAmounts)
        {
            var sheetsToEject = amount;
            var button = new KsTguiButton
            {
                Text = Loc.GetString("ks-tgui-lathe-recipe-times", ("amount", sheetsToEject)),
                Color = KsTguiColor.Transparent,
                Compact = true,
                Disabled = ejectableSheets < sheetsToEject,
                ToolTip = Loc.GetString("lathe-menu-tooltip-display", ("amount", sheetsToEject), ("material", Loc.GetString(materialPrototype.Name))),
            };
            button.OnPressed += _ => _materialUiController.SendLatheEjectMessage(owner, material, sheetsToEject);
            buttons.AddChild(button);
        }

        counter.AddChild(buttons);
        return counter;
    }
}
