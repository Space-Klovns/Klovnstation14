using System.Numerics;
using Content.Client.Materials;
using Content.Shared._KS14.SPAM;
using Content.Shared.Materials;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._KS14.SPAM;

/// <summary>
///     One material of the silo on the S.P.A.M. screen, written as a lathe writes it: the icon, then the name and how
///         much there is. Outlined in orange while the lathes have asked for it, and in red with a mark while the silo
///         is low on it.
/// </summary>
public sealed class KsSPAMMaterialRow : PanelContainer
{
    private static readonly Color RequestedColor = Color.FromHex("#E8A33D");
    private static readonly Color LowColor = Color.FromHex("#D9443B");

    // Not Color.Transparent, which is white.
    private static readonly Color Clear = Color.Black.WithAlpha(0f);

    private readonly StyleBoxFlat _outline = new()
    {
        BackgroundColor = Clear,
        BorderColor = Clear,
        BorderThickness = new Thickness(1),
        ContentMarginLeftOverride = 3,
        ContentMarginRightOverride = 5,
        ContentMarginTopOverride = 2,
        ContentMarginBottomOverride = 2,
    };

    private readonly Label _textLabel;
    private readonly Label _lowLabel;

    public readonly ProtoId<MaterialPrototype> Material;

    /// <summary>
    ///     Whether the row is outlined as asked for.
    /// </summary>
    public bool Requested { get; private set; }

    /// <summary>
    ///     Whether the row is marked as running low.
    /// </summary>
    public bool Low { get; private set; }

    public KsSPAMMaterialRow(MaterialPrototype materialPrototype, SpriteSystem spriteSystem)
    {
        Material = materialPrototype.ID;
        PanelOverride = _outline;
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

        _lowLabel = new Label
        {
            Text = "!",
            FontColorOverride = LowColor,
            VerticalAlignment = VAlignment.Center,
            ToolTip = Loc.GetString("ks-spam-silo-low"),
            MouseFilter = MouseFilterMode.Pass,
            Visible = false,
        };
        box.AddChild(_lowLabel);

        AddChild(box);
    }

    public void Update(KsSPAMSiloMaterial material, MaterialPrototype materialPrototype, MaterialStorageSystem materialStorageSystem, int lowAmount)
    {
        var sheetVolume = (float)materialStorageSystem.GetSheetVolume(materialPrototype);
        var unit = Loc.GetString(materialPrototype.Unit);
        var name = Loc.GetString(materialPrototype.Name);
        var amount = Loc.GetString("lathe-menu-material-amount", ("amount", material.Amount / sheetVolume), ("unit", unit));

        Requested = material.Requested > 0;
        Low = material.Amount <= lowAmount;

        _textLabel.Text = Requested
            ? Loc.GetString("ks-spam-silo-material-requested",
                ("material", name),
                ("amount", amount),
                ("requested", Loc.GetString("lathe-menu-material-amount", ("amount", material.Requested / sheetVolume), ("unit", unit))))
            : Loc.GetString("lathe-menu-material-display", ("material", name), ("amount", amount));

        // Asked for matters more to a salvager than low, so its colour wins the outline. The mark stays either way.
        _outline.BorderColor = Requested ? RequestedColor : Low ? LowColor : Clear;
        _textLabel.FontColorOverride = Requested ? RequestedColor : null;
        _lowLabel.Visible = Low;
    }
}
