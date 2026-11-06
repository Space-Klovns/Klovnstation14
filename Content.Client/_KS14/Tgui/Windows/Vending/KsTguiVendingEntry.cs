using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Tgui.Windows.Vending;

/// <summary>
///     One product of the vending window: a tile with the sprite, name and stock (grid), or a full-width row (list), after
///         tgui's ImageButton. It draws its own background from its state.
/// </summary>
public sealed class KsTguiVendingEntry : ContainerButton
{
    /// <summary>
    ///     The size of a tile's sprite, and so of the tile.
    /// </summary>
    private const float SpriteSize = 64f;

    private static readonly Color Normal = KsTguiPalettes.Translucent(Color.White, 0.04f, KsTguiPalettes.Section);
    private static readonly Color Hover = KsTguiPalettes.Translucent(Color.White, 0.1f, KsTguiPalettes.Section);
    private static readonly Color PressedColor = KsTguiPalettes.Translucent(Color.White, 0.16f, KsTguiPalettes.Section);
    private static readonly Color NameBar = KsTguiPalettes.Translucent(Color.White, 0.1f, KsTguiPalettes.Section);
    private static readonly Color Dim = Color.FromHex("#B4B4B4");

    private Color _background = Normal;
    private readonly bool _grid;
    private readonly Label _stockLabel;
    private readonly Control _content;
    private readonly bool _built;

    public KsTguiVendingEntry(bool grid, EntProtoId prototypeId, string name)
    {
        _grid = grid;
        StyleBoxOverride = new StyleBoxEmpty(); // drawn by Draw, which no style rule can change

        var view = new EntityPrototypeView
        {
            MinSize = new Vector2(32f, 32f),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };
        if (grid)
        {
            // Fills the 64px box whatever size the sprite is.
            view.MinSize = new Vector2(SpriteSize, SpriteSize);
            view.Stretch = SpriteView.StretchMode.Fill;
            view.WorldRotation = Angle.Zero; // else the view sizes the sprite by its rotated diagonal and fills only 70%
        }
        view.SetPrototype(prototypeId);

        var nameLabel = new Label { Text = name, ClipText = true, HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore };
        _stockLabel = new Label { Modulate = Dim, MouseFilter = MouseFilterMode.Ignore };

        if (grid)
        {
            MinSize = new Vector2(SpriteSize + 8f, 0f);
            _stockLabel.HorizontalAlignment = HAlignment.Right;
            _stockLabel.Margin = new Thickness(0f, 1f, 4f, 0f);
            nameLabel.Margin = new Thickness(4f, 1f, 4f, 1f);
            nameLabel.Align = Label.AlignMode.Left;

            var nameBar = new PanelContainer
            {
                PanelOverride = new StyleBoxFlat { BackgroundColor = NameBar },
                MouseFilter = MouseFilterMode.Ignore,
            };
            nameBar.AddChild(nameLabel);

            var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, MouseFilter = MouseFilterMode.Ignore };
            column.AddChild(_stockLabel);
            column.AddChild(view);
            column.AddChild(new Control { MinHeight = 3f });
            column.AddChild(nameBar);
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

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(PixelSizeBox, _background);
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

        _background = DrawMode switch
        {
            DrawModeEnum.Pressed => PressedColor,
            DrawModeEnum.Hover => Hover,
            _ => Normal,
        };
        _content.Modulate = Disabled ? new Color(1f, 1f, 1f, 0.5f) : Color.White;
    }
}
