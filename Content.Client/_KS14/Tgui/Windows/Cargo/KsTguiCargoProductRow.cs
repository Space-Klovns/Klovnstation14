using Content.Client._KS14.Tgui.Controls;
using Content.Shared.Cargo.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using XColor = Robust.Shared.Maths.Color;

namespace Content.Client._KS14.Tgui.Windows.Cargo;

/// <summary>
///     One catalog entry: tgui's ImageButton with the pack's picture, name and price. Pressing it asks for the order.
/// </summary>
public sealed class KsTguiCargoProductRow : ContainerButton
{
    private static readonly XColor Clear = XColor.Black.WithAlpha(0f);
    private static readonly XColor Rest = KsTguiPalettes.Translucent(XColor.FromHex("#606060"), 0.1f, KsTguiPalettes.Base);
    private static readonly XColor HoverWash = KsTguiPalettes.Translucent(XColor.FromHex("#808080"), 0.25f, KsTguiPalettes.Base);
    private static readonly XColor Gold = XColor.FromHex("#FFD700");

    private readonly KsTguiStyleBox _box = new(Clear);

    // ContainerButton's constructor calls DrawModeChanged before any of this exists.
    private bool _built;

    public CargoProductPrototype Product { get; }

    public KsTguiCargoProductRow(CargoProductPrototype product, Texture? icon, string productName, string cost)
    {
        Product = product;
        StyleBoxOverride = _box;
        HorizontalExpand = true;
        _box.Radius = 2f;

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            MouseFilter = MouseFilterMode.Ignore,
            SeparationOverride = 6,
            Margin = new Thickness(4f),
        };
        row.AddChild(new TextureRect
        {
            Texture = icon,
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            MinSize = new System.Numerics.Vector2(32f, 32f),
            MaxSize = new System.Numerics.Vector2(32f, 32f),
            MouseFilter = MouseFilterMode.Ignore,
        });
        row.AddChild(new Label
        {
            Text = productName,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            ClipText = true,
        });
        row.AddChild(new Label
        {
            Text = cost,
            VerticalAlignment = VAlignment.Center,
            FontColorOverride = Gold,
        });
        AddChild(row);

        _built = true;
        Refresh();
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        Refresh();
    }

    private void Refresh()
    {
        if (!_built)
            return;

        _box.BackgroundColor = DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed ? HoverWash : Rest;
    }
}
