using Content.Client._KS14.Tgui.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Tgui.Windows.Vending;

/// <summary>
///     One product of the vending window: tgui's ImageButton as Vending.tsx builds it. A card (grid) has the sprite, the name
///         in its bar and the stock in its top right corner; a row (list) has a 32px sprite, the name and the stock to the
///         right. SS14 has no prices, so the price tags are left out.
/// </summary>
public sealed class KsTguiVendingEntry : KsTguiImageButton
{
    /// <summary>
    ///     The stock text's colour in a row, <c>rgba(255, 255, 255, 0.5)</c>.
    /// </summary>
    private static readonly Color RowStock = KsTguiPalettes.TextDim;

    private readonly bool _grid;
    private readonly Label? _rowStock;

    public KsTguiVendingEntry(bool grid, EntProtoId prototypeId, string name)
    {
        _grid = grid;
        ImagePrototype = prototypeId;
        ToolTip = name;

        if (grid)
        {
            Text = name;
            return;
        }

        Fluid = true;
        ImageSize = 32f;

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, MouseFilter = MouseFilterMode.Ignore };
        row.AddChild(new Label
        {
            Text = name,
            ClipText = true,
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
        });
        _rowStock = new Label
        {
            FontColorOverride = RowStock,
            Align = Label.AlignMode.Right,
            MinWidth = 42f, // width={3.5}
            MouseFilter = MouseFilterMode.Ignore,
        };
        row.AddChild(_rowStock);
        Content = row;
    }

    /// <summary>
    ///     Shows the amount left and disables the product when there is none or the machine is busy.
    /// </summary>
    public void SetStock(uint amount, bool enabled)
    {
        if (_grid)
            TopRightText = $"x{amount}";
        else
            _rowStock!.Text = Loc.GetString("ks-tgui-vending-stock-left", ("amount", (int)amount));

        Disabled = !enabled || amount == 0;
    }
}
