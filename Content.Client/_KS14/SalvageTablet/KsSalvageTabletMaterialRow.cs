using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.Materials;
using Content.Shared._KS14.SalvageTablet;
using Content.Shared.Materials;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._KS14.SalvageTablet;

/// <summary>
///     One material of the silo on the S.P.A.M. screen, written as a lathe writes it: the icon, then the name and how
///         much there is. Outlined in orange while the lathes have asked for it, and in red with a mark while the silo
///         is low on it. The colours are the theme's, through <see cref="KsSalvageTabletStyleClasses"/>.
/// </summary>
public sealed class KsSalvageTabletMaterialRow : PanelContainer
{
    private readonly Label _textLabel;
    private readonly KsTguiIcon _lowIcon;

    public readonly ProtoId<MaterialPrototype> Material;

    /// <summary>
    ///     Whether the row is outlined as asked for.
    /// </summary>
    public bool Requested { get; private set; }

    /// <summary>
    ///     Whether the row is marked as running low.
    /// </summary>
    public bool Low { get; private set; }

    public KsSalvageTabletMaterialRow(MaterialPrototype materialPrototype, SpriteSystem spriteSystem)
    {
        Material = materialPrototype.ID;
        AddStyleClass(KsSalvageTabletStyleClasses.MaterialRow);
        HorizontalExpand = true;

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
        };

        var icon = new TextureRect
        {
            MinSize = new Vector2(32, 32),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            VerticalAlignment = VAlignment.Center,
        };
        if (materialPrototype.Icon != SpriteSpecifier.Invalid)
            icon.Texture = spriteSystem.Frame0(materialPrototype.Icon);
        box.AddChild(icon);

        _textLabel = new Label { HorizontalExpand = true, VerticalAlignment = VAlignment.Center };
        box.AddChild(_textLabel);

        _lowIcon = new KsTguiIcon
        {
            Glyph = KsTguiIcons.Exclamation,
            VerticalAlignment = VAlignment.Center,
            ToolTip = Loc.GetString("ks-salvage-tablet-silo-low"),
            MouseFilter = MouseFilterMode.Pass,
            Visible = false,
            StyleClasses = { KsSalvageTabletStyleClasses.LowIcon },
        };
        box.AddChild(_lowIcon);

        AddChild(box);
    }

    public void Update(KsSalvageTabletSiloMaterial material, MaterialPrototype materialPrototype, MaterialStorageSystem materialStorageSystem, int lowAmount)
    {
        var sheetVolume = (float)materialStorageSystem.GetSheetVolume(materialPrototype);
        var unit = Loc.GetString(materialPrototype.Unit);
        var name = Loc.GetString(materialPrototype.Name);
        var amount = Loc.GetString("lathe-menu-material-amount", ("amount", material.Amount / sheetVolume), ("unit", unit));

        Requested = material.Requested > 0;
        Low = material.Amount <= lowAmount;

        _textLabel.Text = Requested
            ? Loc.GetString("ks-salvage-tablet-silo-material-requested",
                ("material", name),
                ("amount", amount),
                ("requested", Loc.GetString("lathe-menu-material-amount", ("amount", material.Requested / sheetVolume), ("unit", unit))))
            : Loc.GetString("lathe-menu-material-display", ("material", name), ("amount", amount));

        // Asked for matters more to a salvager than low, so its colour wins the outline. The mark stays either way.
        KsSalvageTabletStyleClasses.Toggle(this, KsSalvageTabletStyleClasses.Requested, Requested);
        KsSalvageTabletStyleClasses.Toggle(this, KsSalvageTabletStyleClasses.Low, Low && !Requested);
        KsSalvageTabletStyleClasses.Toggle(_textLabel, KsSalvageTabletStyleClasses.Requested, Requested);
        _lowIcon.Visible = Low;
    }

}
