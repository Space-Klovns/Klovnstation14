using System.Globalization;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using XColor = Robust.Shared.Maths.Color;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's ProgressBar: a 1px border and a fill in the same colour, with the text, the percentage unless given, over it.
///         Drawn by the control itself, so it looks the same under either theme. As tgui's, the fill's width and colour ease
///         over a second when they change, and the border's colour eases out over the same second.
/// </summary>
/// <remarks>
///     <see cref="FrameUpdate"/> returns at its first check once every transition has arrived, so a settled bar costs nothing
///         per frame, as <see cref="KsTguiAnimatedNumber"/> does.
/// </remarks>
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

    /// <summary>
    ///     The 1px border the padding sits inside: tgui's text starts at <c>border + padding</c>, 7px from the outer edge.
    /// </summary>
    private const float BorderLayoutPixels = 1f;

    /// <summary>
    ///     <c>--progress-bar-transition</c>, for the fill's width and colour and the border's colour.
    /// </summary>
    public const float TransitionSeconds = 1f;

    private readonly KsTguiAnimatedNumber _label;
    private readonly KsTguiStyleBox _border = new(XColor.Black.WithAlpha(0f), 2f)
    {
        BorderThickness = new Thickness(1f),
    };

    private readonly List<(KsTguiColor Color, float Min, float Max)> _ranges = new();

    // Each transition runs from what was shown when its target changed, as a CSS transition does. The fill's width and
    //      colour use CSS's default timing function (ease); the border's is ease-out (ProgressBar.scss:13).
    private float _fillFrom;
    private float _fillTo;
    private float _fillElapsed = TransitionSeconds;
    private XColor _fillColorFrom;
    private XColor _fillColorTo;
    private float _fillColorElapsed = TransitionSeconds;
    private XColor _borderColorFrom;
    private XColor _borderColorTo;
    private float _borderColorElapsed = TransitionSeconds;
    private bool _targetsKnown;
    private bool _framed;
    private int _fractionDigits;

    private XColor _defaultElement = KsTguiColors.Element(KsTguiColor.Default);
    private XColor _backdrop = XColor.Black.WithAlpha(0f);

    private float _value;
    private float _minValue;
    private float _maxValue = 1f;
    private KsTguiColor _color;
    private string? _text;
    private bool _hideText;
    private bool _showingNumber;
    private Label.AlignMode _textAlign = Label.AlignMode.Right;

    public KsTguiProgressBar()
    {
        _label = new KsTguiAnimatedNumber { Align = _textAlign };
        AddChild(_label);
        RefreshText();
        Retarget();
    }

    public float Value
    {
        get => _value;
        set
        {
            _value = value;
            RefreshText();
            Retarget();
        }
    }

    public float MinValue
    {
        get => _minValue;
        set
        {
            _minValue = value;
            RefreshText();
            Retarget();
        }
    }

    public float MaxValue
    {
        get => _maxValue;
        set
        {
            _maxValue = value;
            RefreshText();
            Retarget();
        }
    }

    /// <summary>
    ///     The colour; ignored where a range in <see cref="Ranges"/> matches the value.
    /// </summary>
    public KsTguiColor Color
    {
        get => _color;
        set
        {
            _color = value;
            Retarget();
        }
    }

    /// <summary>
    ///     tgui's <c>fractionDigits</c>: the decimals of the percentage, <c>toFixed(fractionDigits)</c>.
    /// </summary>
    public int FractionDigits
    {
        get => _fractionDigits;
        set
        {
            _fractionDigits = Math.Clamp(value, 0, 20);
            RefreshText();
        }
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
            _showingNumber = false;
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
        Retarget();
    }

    public void AddRange(KsTguiColor color, float min, float max)
    {
        _ranges.Add((color, min, max));
        Retarget();
    }

    /// <summary>
    ///     The fraction the value is of its range, not clamped: tgui's percentage text is this, and only the width is clamped.
    /// </summary>
    public float Scale
    {
        get
        {
            var span = _maxValue - _minValue;
            return span == 0f ? 0f : (_value - _minValue) / span;
        }
    }

    /// <summary>
    ///     The fraction filled, 0 to 1.
    /// </summary>
    public float Fraction => Math.Clamp(Scale, 0f, 1f);

    /// <summary>
    ///     The fraction the fill is drawn at right now, which is <see cref="Fraction"/> once <see cref="Transitioning"/> is
    ///         false.
    /// </summary>
    public float DisplayedFraction => Transition(_fillFrom, _fillTo, _fillElapsed, EaseDefault);

    /// <summary>
    ///     The fill colour drawn right now.
    /// </summary>
    public XColor DisplayedFillColor => TransitionColor(_fillColorFrom, _fillColorTo, _fillColorElapsed, EaseDefault);

    /// <summary>
    ///     The border colour drawn right now, which leads the fill's as it eases out.
    /// </summary>
    public XColor DisplayedBorderColor => TransitionColor(_borderColorFrom, _borderColorTo, _borderColorElapsed, EaseOut);

    /// <summary>
    ///     Whether the fill or a colour is still on its way, and so doing work each frame.
    /// </summary>
    public bool Transitioning =>
        _fillElapsed < TransitionSeconds || _fillColorElapsed < TransitionSeconds || _borderColorElapsed < TransitionSeconds;

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

    /// <summary>
    ///     What the bar's text says right now.
    /// </summary>
    public string? ShownText => _label.Text;

    /// <summary>
    ///     CSS's <c>ease</c>, the default timing function.
    /// </summary>
    public static float EaseDefault(float progress)
    {
        return KsTguiRoundGauge.CubicBezier(0.25f, 0.1f, 0.25f, 1f, progress);
    }

    /// <summary>
    ///     CSS's <c>ease-out</c>.
    /// </summary>
    public static float EaseOut(float progress)
    {
        return KsTguiRoundGauge.CubicBezier(0f, 0f, 0.58f, 1f, progress);
    }

    private static float Transition(float from, float to, float elapsed, Func<float, float> ease)
    {
        if (elapsed >= TransitionSeconds)
            return to;

        return from + (to - from) * ease(elapsed / TransitionSeconds);
    }

    private static XColor TransitionColor(XColor from, XColor to, float elapsed, Func<float, float> ease)
    {
        if (elapsed >= TransitionSeconds)
            return to;

        // CSS interpolates colours component by component, in sRGB.
        var t = ease(elapsed / TransitionSeconds);
        return new XColor(from.R + (to.R - from.R) * t, from.G + (to.G - from.G) * t,
            from.B + (to.B - from.B) * t, from.A + (to.A - from.A) * t);
    }

    /// <summary>
    ///     Replaces the percentage with a number that eases to its value, as an <c>AnimatedNumber</c> inside a tgui ProgressBar.
    ///         Setting <see cref="Text"/> ends it.
    /// </summary>
    public void ShowNumber(float value, Func<float, string> format, float? initial = null)
    {
        _showingNumber = true;
        _label.Initial = initial;
        _label.Format = format;
        _label.Value = value;
        InvalidateMeasure();
    }

    private void RefreshText()
    {
        if (_showingNumber)
            return;

        _label.ShowText(_hideText
            ? string.Empty
            : _text ?? (Scale * 100f).ToString("F" + _fractionDigits.ToString(CultureInfo.InvariantCulture),
                CultureInfo.CurrentCulture) + "%");
        InvalidateMeasure();
    }

    /// <summary>
    ///     Points the fill and the colours at what the value now asks for. Before the first frame they jump there, as an
    ///         element's first render has nothing to transition from.
    /// </summary>
    private void Retarget(bool snap = false)
    {
        var fraction = Fraction;
        var resolved = ResolvedColor;
        var element = resolved == KsTguiColor.Default ? _defaultElement : KsTguiColors.Element(resolved);

        if (snap || !_framed || !_targetsKnown)
        {
            _targetsKnown = true;
            _fillFrom = _fillTo = fraction;
            _fillElapsed = TransitionSeconds;
            _fillColorFrom = _fillColorTo = _borderColorFrom = _borderColorTo = element;
            _fillColorElapsed = _borderColorElapsed = TransitionSeconds;
            return;
        }

        if (fraction != _fillTo)
        {
            _fillFrom = DisplayedFraction;
            _fillTo = fraction;
            _fillElapsed = 0f;
        }

        if (element != _fillColorTo)
        {
            _fillColorFrom = DisplayedFillColor;
            _fillColorTo = element;
            _fillColorElapsed = 0f;
            _borderColorFrom = DisplayedBorderColor;
            _borderColorTo = element;
            _borderColorElapsed = 0f;
        }
    }

    /// <summary>
    ///     Moves every transition on by a frame. Does nothing once they have all arrived.
    /// </summary>
    public void Advance(float frameSeconds)
    {
        _framed = true;

        if (!Transitioning)
            return;

        _fillElapsed = MathF.Min(TransitionSeconds, _fillElapsed + frameSeconds);
        _fillColorElapsed = MathF.Min(TransitionSeconds, _fillColorElapsed + frameSeconds);
        _borderColorElapsed = MathF.Min(TransitionSeconds, _borderColorElapsed + frameSeconds);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        Advance(args.DeltaSeconds);
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        // The default colour is the theme's primary, and a backdrop is a theme's own: both come from the sheet.
        var defaultElement = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyProgressColor, out var element)
            ? element
            : KsTguiColors.Element(KsTguiColor.Default);
        _backdrop = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyProgressBackdrop, out var backdrop)
            ? backdrop
            : XColor.Black.WithAlpha(0f);

        // A theme change is not a value changing: the colour lands at once.
        if (defaultElement == _defaultElement)
            return;

        _defaultElement = defaultElement;
        Retarget(snap: true);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var inset = BorderLayoutPixels + TextPadding;
        _label.Measure(new Vector2(MathF.Max(0f, availableSize.X - 2f * inset), availableSize.Y));
        return new Vector2(_label.DesiredSize.X + 2f * inset, MathF.Max(MinHeightPixels, _label.DesiredSize.Y));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var inset = BorderLayoutPixels + TextPadding;
        var labelHeight = MathF.Min(finalSize.Y, _label.DesiredSize.Y);
        _label.Arrange(UIBox2.FromDimensions(inset, (finalSize.Y - labelHeight) / 2f,
            MathF.Max(0f, finalSize.X - 2f * inset), labelHeight));
        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var box = PixelSizeBox;
        var border = MathF.Max(1f, MathF.Round(UIScale));

        if (_backdrop.A > 0f)
            handle.DrawRect(box, _backdrop);

        var inner = new UIBox2(box.Left + border, box.Top + border, box.Right - border, box.Bottom - border);
        var fillRight = inner.Left + inner.Width * DisplayedFraction;
        if (fillRight > inner.Left)
            handle.DrawRect(new UIBox2(inner.Left, inner.Top, fillRight, inner.Bottom), DisplayedFillColor);

        _border.BorderColor = DisplayedBorderColor;
        _border.Draw(handle, box, UIScale);
    }
}
