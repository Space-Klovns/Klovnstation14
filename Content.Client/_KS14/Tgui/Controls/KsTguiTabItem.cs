using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
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

    private XColor _hoverWash = HoverWash;
    private XColor _selectedWash = SelectedWash;
    private XColor? _selectedText;

    private readonly KsTguiStyleBox _box = new(Clear);
    private readonly Control _leftSlot;
    private readonly KsTguiIcon _icon;
    private readonly Label _text;
    private readonly Label _count;

    /// <summary>
    ///     tgui's <c>--tab-transition</c>, for the indicator, the background and the text alike.
    /// </summary>
    public const float TransitionSeconds = 0.2f;

    private XColor _bgFrom = Clear;
    private XColor _bgTo = Clear;
    private XColor _bgNow = Clear;
    private XColor _textFrom = KsTguiPalettes.TextDim;
    private XColor _textTo = KsTguiPalettes.TextDim;
    private XColor _textNow = KsTguiPalettes.TextDim;
    private float _indicatorFrom;
    private float _indicatorTo;
    private float _indicatorNow;
    private float _elapsed = TransitionSeconds;
    // Changes before the first frame (a theme arriving as the tab enters the tree) are settings, not transitions.
    private bool _framed;

    // ContainerButton's constructor calls DrawModeChanged before any of this exists.
    private bool _built;
    private bool _selected;
    private bool _vertical;
    private KsTguiColor _color;
    private bool _attention;

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

    /// <summary>
    ///     Something on this tab wants a look, such as requests waiting: tgui's <c>textColor='yellow'</c>. The text is yellow
    ///         while the tab is unselected, and the text and the indicator line are yellow while it is selected.
    /// </summary>
    public bool Attention
    {
        get => _attention;
        set
        {
            _attention = value;
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

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        _hoverWash = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyTabHoverWash, out var hover) ? hover : HoverWash;
        _selectedWash = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyTabSelectedWash, out var selected) ? selected : SelectedWash;
        _selectedText = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyTabSelectedText, out var text) ? text : null;
        RefreshLook();
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        RefreshLook();
    }

    /// <summary>
    ///     How far the indicator line has grown, from 0 (not there) to 1: it grows from its middle outwards, as tgui's
    ///         <c>scaleX(0)</c> to <c>scaleX(.99999)</c>.
    /// </summary>
    public float IndicatorProgress => _indicatorNow;

    /// <summary>
    ///     The background drawn now, part way through its transition.
    /// </summary>
    public XColor CurrentBackground => _bgNow;

    /// <summary>
    ///     The text colour drawn now, part way through its transition.
    /// </summary>
    public XColor CurrentTextColor => _textNow;

    /// <summary>
    ///     Finishes the transitions at once.
    /// </summary>
    public void SettleAnimations()
    {
        _elapsed = TransitionSeconds;
        ApplyAnimation();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _framed = true;

        if (_elapsed >= TransitionSeconds)
            return;

        _elapsed += args.DeltaSeconds;
        ApplyAnimation();
    }

    private static float Ease(float progress)
    {
        var remaining = 1f - progress;
        return 1f - remaining * remaining * remaining;
    }

    /// <summary>
    ///     Between two washes the way CSS does: a fade from or to nothing keeps the other's colour and moves only its alpha.
    /// </summary>
    private static XColor Blend(XColor from, XColor to, float amount)
    {
        if (from.A <= 0f)
            return to.WithAlpha(to.A * amount);

        if (to.A <= 0f)
            return from.WithAlpha(from.A * (1f - amount));

        return XColor.InterpolateBetween(from, to, amount);
    }

    private void ApplyAnimation()
    {
        var amount = Ease(MathF.Min(_elapsed / TransitionSeconds, 1f));
        _bgNow = Blend(_bgFrom, _bgTo, amount);
        _textNow = XColor.InterpolateBetween(_textFrom, _textTo, amount);
        _indicatorNow = _indicatorFrom + (_indicatorTo - _indicatorFrom) * amount;

        _box.BackgroundColor = _bgNow;
        _text.FontColorOverride = _textNow;
        _count.FontColorOverride = _textNow;
        _icon.IconColor = _textNow;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_indicatorNow <= 0f)
            return;

        var thickness = MathF.Max(1f, MathF.Round(2f * UIScale));
        var color = _indicatorColor;
        if (_vertical)
        {
            var length = PixelHeight * _indicatorNow * 0.99999f;
            var top = (PixelHeight - length) / 2f;
            handle.DrawRect(new UIBox2(PixelWidth - thickness, top, PixelWidth, top + length), color);
        }
        else
        {
            var length = PixelWidth * _indicatorNow * 0.99999f;
            var left = (PixelWidth - length) / 2f;
            handle.DrawRect(new UIBox2(left, PixelHeight - thickness, left + length, PixelHeight), color);
        }
    }

    private XColor _indicatorColor = Clear;

    private void RefreshLook()
    {
        if (!_built)
            return;

        var bright = _attention ? KsTguiColors.Bright(KsTguiColor.Yellow)
            : _color == KsTguiColor.Default && _selectedText is { } themed ? themed : KsTguiColors.Bright(_color);
        var hovered = DrawMode == DrawModeEnum.Hover;
        var pressed = DrawMode == DrawModeEnum.Pressed;

        // The line is drawn by this control, so it can grow from the middle; the box keeps the background and corners.
        _box.BorderColor = Clear;
        _box.BorderThickness = default;
        _box.RadiusTopLeft = 3f;
        _box.RadiusTopRight = _vertical ? 0f : 3f;
        _box.RadiusBottomLeft = _vertical ? 3f : 0f;
        _box.RadiusBottomRight = 0f;

        // Height 27 horizontally (15 of text and 6 each side), 24 vertically.
        _box.SetContentMarginOverride(StyleBox.Margin.Vertical, _vertical ? 4.5f : 6f);
        MinSize = new System.Numerics.Vector2(48f, _vertical ? 24f : 27f);

        var wanted = _selected ? _selectedWash : hovered && !_selected ? _hoverWash : Clear;
        var text = _selected || pressed || _attention ? bright : KsTguiPalettes.TextDim;
        var indicator = _selected ? 1f : 0f;

        // The line is the text's colour (currentColor), and when it shrinks it keeps the colour it had.
        if (_selected)
            _indicatorColor = bright;

        if (!_framed)
        {
            _bgFrom = _bgTo = wanted;
            _textFrom = _textTo = text;
            _indicatorFrom = _indicatorTo = indicator;
            _elapsed = TransitionSeconds;
        }
        else if (wanted != _bgTo || text != _textTo || indicator != _indicatorTo)
        {
            _bgFrom = _bgNow;
            _textFrom = _textNow;
            _indicatorFrom = _indicatorNow;
            _bgTo = wanted;
            _textTo = text;
            _indicatorTo = indicator;
            _elapsed = 0f;
        }

        ApplyAnimation();
    }
}
