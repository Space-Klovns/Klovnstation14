using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's ColorBox: a 1em (12px) square of <see cref="BoxColor"/>, used inline as a swatch, with <see cref="Content"/>
///         centred on it in the default text colour. Without a colour it is transparent, as tgui's is.
/// </summary>
public sealed class KsTguiColorBox : Control
{
    private const float SwatchSize = 12f;

    private readonly Label _label = new() { Visible = false };
    private Color _boxColor = new(0f, 0f, 0f, 0f);
    private KsTguiColor? _namedColor;

    /// <summary>
    ///     The swatch colour. Transparent unless set.
    /// </summary>
    public Color BoxColor
    {
        get => _boxColor;
        set
        {
            _boxColor = value;
            _namedColor = null;
        }
    }

    /// <summary>
    ///     One of tgui's colour names: the background is its token at lightness -5, as <c>color-bg-X</c> is.
    /// </summary>
    public KsTguiColor? NamedColor
    {
        get => _namedColor;
        set
        {
            _namedColor = value;
            _boxColor = value is { } named ? KsTguiColors.Element(named) : new Color(0f, 0f, 0f, 0f);
        }
    }

    /// <summary>
    ///     tgui's <c>content</c>: text drawn over the swatch, centred, on a line 1em high.
    /// </summary>
    public string? Content
    {
        get => _label.Text;
        set
        {
            _label.Text = value;
            _label.Visible = !string.IsNullOrEmpty(value);
            InvalidateMeasure();
        }
    }

    public KsTguiColorBox()
    {
        VerticalAlignment = VAlignment.Center;
        HorizontalAlignment = HAlignment.Left;
        AddChild(_label);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _label.Measure(Vector2Helpers.Infinity);
        return new Vector2(SwatchSize, SwatchSize);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        // The text is the width of its line and overflows the box if wider, centred on it.
        var size = _label.DesiredSize;
        _label.Arrange(UIBox2.FromDimensions((finalSize.X - size.X) / 2f, (finalSize.Y - size.Y) / 2f, size.X, size.Y));
        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_boxColor.A > 0f)
            handle.DrawRect(PixelSizeBox, _boxColor);
    }
}
