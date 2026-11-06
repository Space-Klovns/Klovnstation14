using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using XColor = Robust.Shared.Maths.Color;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's ProgressBar: a 1px border and a fill in the same colour, with the text, the percentage unless given, over it.
///         Drawn by the control itself, so it looks the same under either theme.
/// </summary>
public sealed class KsTguiProgressBar : Control
{
    /// <summary>
    ///     <c>--button-height</c>, which a progress bar's <c>min-height</c> takes.
    /// </summary>
    private const float MinHeightPixels = 20f;

    /// <summary>
    ///     <c>padding: 0 var(--space-m)</c>.
    /// </summary>
    private const float TextPadding = 6f;

    private readonly Label _label;
    private readonly KsTguiStyleBox _border = new(XColor.Black.WithAlpha(0f), 2f)
    {
        BorderThickness = new Thickness(1f),
    };

    private readonly List<(KsTguiColor Color, float Min, float Max)> _ranges = new();

    private XColor _defaultElement = KsTguiColors.Element(KsTguiColor.Default);
    private XColor _backdrop = XColor.Black.WithAlpha(0f);

    private float _value;
    private float _minValue;
    private float _maxValue = 1f;
    private KsTguiColor _color;
    private string? _text;
    private bool _hideText;
    private Label.AlignMode _textAlign = Label.AlignMode.Right;

    public KsTguiProgressBar()
    {
        _label = new Label { Align = _textAlign };
        AddChild(_label);
        RefreshText();
    }

    public float Value
    {
        get => _value;
        set
        {
            _value = value;
            RefreshText();
        }
    }

    public float MinValue
    {
        get => _minValue;
        set
        {
            _minValue = value;
            RefreshText();
        }
    }

    public float MaxValue
    {
        get => _maxValue;
        set
        {
            _maxValue = value;
            RefreshText();
        }
    }

    /// <summary>
    ///     The colour; ignored where a range in <see cref="Ranges"/> matches the value.
    /// </summary>
    public KsTguiColor Color
    {
        get => _color;
        set => _color = value;
    }

    /// <summary>
    ///     Replaces the percentage. Null shows the percentage, empty shows nothing.
    /// </summary>
    public string? Text
    {
        get => _text;
        set
        {
            _text = value;
            RefreshText();
        }
    }

    /// <summary>
    ///     tgui's <c>empty</c>: no text at all.
    /// </summary>
    public bool HideText
    {
        get => _hideText;
        set
        {
            _hideText = value;
            RefreshText();
        }
    }

    /// <summary>
    ///     tgui right-aligns it.
    /// </summary>
    public Label.AlignMode TextAlign
    {
        get => _textAlign;
        set
        {
            _textAlign = value;
            _label.Align = value;
        }
    }

    /// <summary>
    ///     Colours by value, as tgui's <c>ranges</c>; the first range holding the value (inclusive both ends, in the unscaled
    ///         value) wins.
    /// </summary>
    public void SetRanges(params (KsTguiColor Color, float Min, float Max)[] ranges)
    {
        _ranges.Clear();
        _ranges.AddRange(ranges);
    }

    public void AddRange(KsTguiColor color, float min, float max)
    {
        _ranges.Add((color, min, max));
    }

    /// <summary>
    ///     The fraction filled, 0 to 1.
    /// </summary>
    public float Fraction
    {
        get
        {
            var span = _maxValue - _minValue;
            return span == 0f ? 0f : Math.Clamp((_value - _minValue) / span, 0f, 1f);
        }
    }

    /// <summary>
    ///     The colour that applies to the current value.
    /// </summary>
    public KsTguiColor ResolvedColor
    {
        get
        {
            foreach (var (color, min, max) in _ranges)
            {
                if (_value >= min && _value <= max)
                    return color;
            }

            return _color;
        }
    }

    private void RefreshText()
    {
        _label.Text = _hideText ? string.Empty : _text ?? (Fraction * 100f).ToString("0") + "%";
        InvalidateMeasure();
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        // The default colour is the theme's primary, and a backdrop is a theme's own: both come from the sheet.
        _defaultElement = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyProgressColor, out var element)
            ? element
            : KsTguiColors.Element(KsTguiColor.Default);
        _backdrop = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyProgressBackdrop, out var backdrop)
            ? backdrop
            : XColor.Black.WithAlpha(0f);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _label.Measure(new Vector2(MathF.Max(0f, availableSize.X - 2f * TextPadding), availableSize.Y));
        return new Vector2(_label.DesiredSize.X + 2f * TextPadding, MathF.Max(MinHeightPixels, _label.DesiredSize.Y));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var labelHeight = MathF.Min(finalSize.Y, _label.DesiredSize.Y);
        _label.Arrange(UIBox2.FromDimensions(TextPadding, (finalSize.Y - labelHeight) / 2f,
            MathF.Max(0f, finalSize.X - 2f * TextPadding), labelHeight));
        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var box = PixelSizeBox;
        var resolved = ResolvedColor;
        var element = resolved == KsTguiColor.Default ? _defaultElement : KsTguiColors.Element(resolved);
        var border = MathF.Max(1f, MathF.Round(UIScale));

        if (_backdrop.A > 0f)
            handle.DrawRect(box, _backdrop);

        var inner = new UIBox2(box.Left + border, box.Top + border, box.Right - border, box.Bottom - border);
        var fillRight = inner.Left + inner.Width * Fraction;
        if (fillRight > inner.Left)
            handle.DrawRect(new UIBox2(inner.Left, inner.Top, fillRight, inner.Bottom), element);

        _border.BorderColor = element;
        _border.Draw(handle, box, UIScale);
    }
}
