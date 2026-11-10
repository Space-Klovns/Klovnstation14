using Content.Client._KS14.Tgui.Controls;
using Content.Shared.Cargo.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using XColor = Robust.Shared.Maths.Color;

namespace Content.Client._KS14.Tgui.Windows.Cargo;

/// <summary>
///     One catalog entry: tgui's fluid ImageButton from CargoCatalog.tsx, with the pack's 32px picture, its name and its
///         price. Pressing it asks for the order.
/// </summary>
public sealed class KsTguiCargoProductRow : KsTguiImageButton
{
    private static readonly XColor Gold = XColor.FromHex("#FFD700");

    public CargoProductPrototype Product { get; }

    public KsTguiCargoProductRow(CargoProductPrototype product, Texture? icon, string productName, string cost)
    {
        Product = product;
        Fluid = true;
        ImageSize = 32f;
        ImageTexture = icon;

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, MouseFilter = MouseFilterMode.Ignore };
        row.AddChild(new Label
        {
            Text = productName,
            ClipText = true,
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
        });
        row.AddChild(new Label { Text = cost, FontColorOverride = Gold, MouseFilter = MouseFilterMode.Ignore });
        Content = row;
    }
}
