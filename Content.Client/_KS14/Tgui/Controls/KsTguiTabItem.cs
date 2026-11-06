using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using XColor = Robust.Shared.Maths.Color;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     One tab of a <see cref="KsTguiTabs"/>: an optional icon at the left, the text, and an optional badge (an unread count)
///         at the right. It draws its own background and indicator line from its state, so it needs no sheet rules and
///         looks the same under either theme.
/// </summary>
public sealed class KsTguiTabItem : ContainerButton
{
    private static readonly XColor Clear = XColor.Black.WithAlpha(0f);
    private static readonly XColor HoverWash = KsTguiPalettes.Translucent(XColor.FromHex("#606060"), 0.25f, KsTguiPalettes.Base);
    private static readonly XColor SelectedWash = KsTguiPalettes.Translucent(XColor.FromHex("#606060"), 0.4f, KsTguiPalettes.Base);

    private readonly KsTguiStyleBox _box = new(Clear);
    private readonly Control _leftSlot;
    private readonly KsTguiIcon _icon;
    private readonly Label _text;
    private readonly Label _count;

    // ContainerButton's constructor calls DrawModeChanged before any of this exists.
    private bool _built;
    private bool _selected;
    private bool _vertical;
    private KsTguiColor _color;

    public KsTguiTabItem()
    {
        StyleBoxOverride = _box;
        MinSize = new System.Numerics.Vector2(48f, 27f);

        _icon = new KsTguiIcon();
        _leftSlot = new Control { Visible = false, MinWidth = 18f, Margin = new Thickness(3f, 0f, 0f, 0f) };
        _leftSlot.AddChild(_icon);
        _icon.HorizontalAlignment = HAlignment.Center;
        _icon.VerticalAlignment = VAlignment.Center;

        _text = new Label { HorizontalExpand = true, Margin = new Thickness(6f, 0f) };

        _count = new Label { Visible = false, MinWidth = 18f, Align = Label.AlignMode.Center, Margin = new Thickness(0f, 0f, 3f, 0f) };

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, MouseFilter = MouseFilterMode.Ignore };
        row.AddChild(_leftSlot);
        row.AddChild(_text);
        row.AddChild(_count);
        AddChild(row);

        _built = true;
        RefreshLook();
    }

    public string? Text
    {
        get => _text.Text;
        set => _text.Text = value;
    }

    /// <summary>
    ///     A <see cref="KsTguiIcons"/> glyph shown before the text.
    /// </summary>
    public string? Icon
    {
        get => _icon.Glyph;
        set
        {
            _icon.Glyph = value;
            _leftSlot.Visible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>
    ///     The badge shown after the text, such as an unread count; null or empty hides it.
    /// </summary>
    public string? Count
    {
        get => _count.Text;
        set
        {
            _count.Text = value;
            _count.Visible = !string.IsNullOrEmpty(value);
        }
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            RefreshLook();
        }
    }

    /// <summary>
    ///     The colour the selected text and the indicator line take; default is the primary blue.
    /// </summary>
    public KsTguiColor SelectedColor
    {
        get => _color;
        set
        {
            _color = value;
            RefreshLook();
        }
    }

    public bool Vertical
    {
        get => _vertical;
        set
        {
            _vertical = value;
            RefreshLook();
        }
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

        var bright = KsTguiColors.Bright(_color);
        var hovered = DrawMode == DrawModeEnum.Hover;
        var pressed = DrawMode == DrawModeEnum.Pressed;

        _box.BackgroundColor = _selected ? SelectedWash : hovered ? HoverWash : Clear;
        _box.BorderColor = _selected ? bright : Clear;
        _box.BorderThickness = _vertical ? new Thickness(0f, 0f, 2f, 0f) : new Thickness(0f, 0f, 0f, 2f);
        _box.RadiusTopLeft = 3f;
        _box.RadiusTopRight = _vertical ? 0f : 3f;
        _box.RadiusBottomLeft = _vertical ? 3f : 0f;
        _box.RadiusBottomRight = 0f;

        // Height 27 horizontally (15 of text and 6 each side), 24 vertically.
        _box.SetContentMarginOverride(StyleBox.Margin.Vertical, _vertical ? 4.5f : 6f);
        MinSize = new System.Numerics.Vector2(48f, _vertical ? 24f : 27f);

        var text = _selected || pressed ? bright : KsTguiPalettes.TextDim;
        _text.FontColorOverride = text;
        _count.FontColorOverride = text;
        _icon.IconColor = text;
    }
}
