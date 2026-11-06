using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Tgui.Windows.Vending;

/// <summary>
///     One product of the vending window: a tile with the sprite, name and stock (grid), or a full-width row (list), after
///         tgui's ImageButton. It draws its own background from its state.
/// </summary>
public sealed class KsTguiVendingEntry : ContainerButton
{
    private static readonly Color Normal = KsTguiPalettes.Translucent(Color.Black, 0.2f, KsTguiPalettes.Section);
    private static readonly Color Hover = KsTguiPalettes.Translucent(Color.White, 0.1f, KsTguiPalettes.Section);
    private static readonly Color PressedColor = KsTguiPalettes.Translucent(Color.White, 0.2f, KsTguiPalettes.Section);
    private static readonly Color Dim = Color.FromHex("#B4B4B4");

    private readonly KsTguiStyleBox _box = new(Normal, 2f);
    private readonly bool _grid;
    private readonly Label _stockLabel;
    private readonly Control _content;
    private readonly bool _built;

    public KsTguiVendingEntry(bool grid, EntProtoId prototypeId, string name)
    {
        _grid = grid;
        StyleBoxOverride = _box;

        var view = new EntityPrototypeView
        {
            MinSize = new Vector2(32f, 32f),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };
        view.SetPrototype(prototypeId);

        var nameLabel = new Label { Text = name, ClipText = true, HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore };
        _stockLabel = new Label { Modulate = Dim, MouseFilter = MouseFilterMode.Ignore };

        if (grid)
        {
            MinSize = new Vector2(74f, 92f);
            _stockLabel.HorizontalAlignment = HAlignment.Right;
            _stockLabel.Margin = new Thickness(0f, 2f, 4f, 0f);
            view.VerticalExpand = true;
            nameLabel.Margin = new Thickness(4f, 0f, 4f, 3f);

            var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, MouseFilter = MouseFilterMode.Ignore };
            column.AddChild(_stockLabel);
            column.AddChild(view);
            column.AddChild(nameLabel);
            _content = column;
        }
        else
        {
            MinSize = new Vector2(0f, 36f);
            HorizontalExpand = true;
            view.Margin = new Thickness(2f, 0f, 4f, 0f);
            _stockLabel.Margin = new Thickness(0f, 0f, 6f, 0f);
            _stockLabel.MinWidth = 60f;
            _stockLabel.Align = Label.AlignMode.Right;
            nameLabel.VerticalAlignment = VAlignment.Center;
            _stockLabel.VerticalAlignment = VAlignment.Center;

            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, MouseFilter = MouseFilterMode.Ignore };
            row.AddChild(view);
            row.AddChild(nameLabel);
            row.AddChild(_stockLabel);
            _content = row;
        }

        AddChild(_content);
        _built = true;
        RefreshLook();
    }

    /// <summary>
    ///     Shows the amount left and disables the tile when there is none or the machine is busy.
    /// </summary>
    public void SetStock(uint amount, bool enabled)
    {
        _stockLabel.Text = _grid
            ? $"x{amount}"
            : Loc.GetString("ks-tgui-vending-stock-left", ("amount", (int)amount));
        Disabled = !enabled || amount == 0;
        RefreshLook();
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        RefreshLook();
    }

    private void RefreshLook()
    {
        if (!_built)
            return;

        _box.BackgroundColor = DrawMode switch
        {
            DrawModeEnum.Pressed => PressedColor,
            DrawModeEnum.Hover => Hover,
            _ => Normal,
        };
        _content.Modulate = Disabled ? new Color(1f, 1f, 1f, 0.5f) : Color.White;
    }
}
