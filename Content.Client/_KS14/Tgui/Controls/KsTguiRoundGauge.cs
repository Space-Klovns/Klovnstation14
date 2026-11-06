using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using XColor = Robust.Shared.Maths.Color;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's RoundGauge: a half ring split into coloured <see cref="AddRange">ranges</see>, a red needle from the
///         centre, an alert triangle that lights in the colour of the range the value is in, and the value in text below
///         (an <see cref="KsTguiAnimatedNumber"/>, so it eases as the needle does not). Drawn as triangles in device pixels from
///         the same 100 by 50 view box tgui's SVG uses, with soft edges, so it stays sharp at any UI scale. Not ported: the
///         needle's .2 s ease and its shake and the alert's pulse while in alert.
/// </summary>
public sealed class KsTguiRoundGauge : Control
{
    /// <summary>
    ///     tgui's <c>.RoundGauge</c> is 2.6em by 1.3em, times <see cref="GaugeSize"/>.
    /// </summary>
    private const float BaseWidth = 31.2f;

    private const float BaseHeight = 15.6f;

    /// <summary>
    ///     <c>margin-bottom: 0.2em</c>.
    /// </summary>
    private const float TextGap = 2.4f;

    /// <summary>
    ///     How far, in device pixels, the soft edge reaches past the shape.
    /// </summary>
    private const float Fringe = 0.75f;

    private static readonly XColor Track = new(1f, 1f, 1f, 0.1f);

    private readonly KsTguiAnimatedNumber _text = new() { Align = Robust.Client.UserInterface.Controls.Label.AlignMode.Center };
    private readonly List<(KsTguiColor Color, float Min, float Max)> _ranges = new();
    private readonly List<DrawVertexUV2DColor> _vertices = new(512);

    // The window theme's, from style properties; the default theme's where the sheet doesn't say.
    private Color _ring = KsTguiPalettes.InputBorder;
    private Color _windowBase = KsTguiPalettes.Base;

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        _ring = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyGaugeRing, out var ring) ? ring : KsTguiPalettes.InputBorder;
        _windowBase = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyWindowBase, out var windowBase) ? windowBase : KsTguiPalettes.Base;
        InvalidateMeasure();
    }

    private float _value;
    private float _minValue;
    private float _maxValue = 1f;
    private float _size = 1f;

    public KsTguiRoundGauge()
    {
        AddChild(_text);
    }

    public float Value
    {
        get => _value;
        set
        {
            _value = value;
            _text.Value = value;
        }
    }

    public float MinValue
    {
        get => _minValue;
        set => _minValue = value;
    }

    public float MaxValue
    {
        get => _maxValue;
        set => _maxValue = value;
    }

    /// <summary>
    ///     tgui's <c>size</c>, in ems of the gauge's 2.6 by 1.3.
    /// </summary>
    public float GaugeSize
    {
        get => _size;
        set
        {
            _size = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     The alert shows lit when the value is past this.
    /// </summary>
    public float? AlertAfter { get; set; }

    /// <summary>
    ///     The alert shows lit when the value is under this.
    /// </summary>
    public float? AlertBefore { get; set; }

    /// <summary>
    ///     How the value is written, such as a pressure with its unit. Null shows the number with the decimals it has.
    /// </summary>
    public Func<float, string>? Format
    {
        get => _text.Format;
        set => _text.Format = value;
    }

    /// <summary>
    ///     Colours a stretch of the ring, in the gauge's own values. Without any, the whole ring is one blue.
    /// </summary>
    public void AddRange(KsTguiColor color, float min, float max)
    {
        _ranges.Add((color, min, max));
    }

    public void ClearRanges()
    {
        _ranges.Clear();
    }

    /// <summary>
    ///     The value as a fraction of the dial, 0 to 1, clamped. A dial with no span reads as empty.
    /// </summary>
    public static float FractionOf(float value, float min, float max)
    {
        var span = max - min;
        if (span == 0f || float.IsNaN(value))
            return 0f;

        return Math.Clamp((value - min) / span, 0f, 1f);
    }

    /// <summary>
    ///     The needle's turn from straight up in degrees, clockwise: -90 at the minimum, 0 in the middle, 90 at the maximum
    ///         (tgui's <c>rotate(180 * fraction - 90)</c>), whatever the value.
    /// </summary>
    public static float NeedleDegrees(float value, float min, float max)
    {
        return 180f * FractionOf(value, min, max) - 90f;
    }

    /// <summary>
    ///     Where a range of the gauge's values starts and ends on the dial, as fractions, clamped.
    /// </summary>
    public static (float Start, float End) RangeFractions(float rangeMin, float rangeMax, float min, float max)
    {
        return (FractionOf(rangeMin, min, max), FractionOf(rangeMax, min, max));
    }

    /// <summary>
    ///     The range the value's place on the dial falls in, the first that holds it with both ends included, as tgui's
    ///         <c>keyOfMatchingRange</c>; null when none does.
    /// </summary>
    public KsTguiColor? MatchingRange()
    {
        var fraction = FractionOf(_value, _minValue, _maxValue);
        foreach (var (color, min, max) in _ranges)
        {
            var (start, end) = RangeFractions(min, max, _minValue, _maxValue);
            if (fraction >= start && fraction <= end)
                return color;
        }

        return null;
    }

    /// <summary>
    ///     Whether the alert is lit: past <see cref="AlertAfter"/>, or under <see cref="AlertBefore"/>.
    /// </summary>
    public bool InAlert
    {
        get
        {
            return AlertAfter is { } after && _value > after || AlertBefore is { } before && _value < before;
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _text.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
        var width = MathF.Max(BaseWidth * _size, _text.DesiredSize.X);
        return new Vector2(width, BaseHeight * _size + TextGap + _text.DesiredSize.Y);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        _text.Arrange(UIBox2.FromDimensions(0f, BaseHeight * _size + TextGap, finalSize.X, _text.DesiredSize.Y));
        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        // The dial is centred in the width it was given.
        var scale = UIScale;
        var dialWidth = BaseWidth * _size * scale;
        var unit = dialWidth / 100f;
        var origin = new Vector2((PixelSize.X - dialWidth) / 2f, 0f);
        var fringe = Fringe / unit;

        _vertices.Clear();

        if (AlertAfter != null || AlertBefore != null)
            AppendAlert(origin, unit, fringe, InAlert ? MatchingRange() : null, InAlert);

        AppendRing(origin, unit, fringe, 0f, 1f, Track);
        if (_ranges.Count == 0)
        {
            AppendRing(origin, unit, fringe, 0f, 1f, _ring);
        }
        else
        {
            foreach (var (color, min, max) in _ranges)
            {
                var (start, end) = RangeFractions(min, max, _minValue, _maxValue);
                if (end > start)
                    AppendRing(origin, unit, fringe, start, end, KsTguiColors.Shade(color, 0f));
            }
        }

        AppendNeedle(origin, unit, fringe);

        if (_vertices.Count > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, _vertices.ToArray());
    }

    private static Vector2 Point(Vector2 origin, float unit, Vector2 viewBox)
    {
        return origin + viewBox * unit;
    }

    private void AppendRing(Vector2 origin, float unit, float fringe, float start, float end, XColor color)
    {
        const float innerRadius = 40f;
        const float outerRadius = 50f;

        var centre = new Vector2(50f, 50f);
        var segments = Math.Max(2, (int)MathF.Ceiling((end - start) * 60f));
        var clear = color.WithAlpha(0f);

        for (var i = 0; i < segments; i++)
        {
            var angleA = MathF.PI * (1f + start + (end - start) * i / segments);
            var angleB = MathF.PI * (1f + start + (end - start) * (i + 1) / segments);
            var directionA = new Vector2(MathF.Cos(angleA), MathF.Sin(angleA));
            var directionB = new Vector2(MathF.Cos(angleB), MathF.Sin(angleB));

            var outerFringeA = Point(origin, unit, centre + directionA * (outerRadius + fringe));
            var outerFringeB = Point(origin, unit, centre + directionB * (outerRadius + fringe));
            var outerA = Point(origin, unit, centre + directionA * outerRadius);
            var outerB = Point(origin, unit, centre + directionB * outerRadius);
            var innerA = Point(origin, unit, centre + directionA * innerRadius);
            var innerB = Point(origin, unit, centre + directionB * innerRadius);
            var innerFringeA = Point(origin, unit, centre + directionA * (innerRadius - fringe));
            var innerFringeB = Point(origin, unit, centre + directionB * (innerRadius - fringe));

            AppendQuad(innerA, color, outerA, color, outerB, color, innerB, color);
            AppendQuad(outerA, color, outerFringeA, clear, outerFringeB, clear, outerB, color);
            AppendQuad(innerFringeA, clear, innerA, color, innerB, color, innerFringeB, clear);
        }
    }

    private void AppendNeedle(Vector2 origin, float unit, float fringe)
    {
        var red = KsTguiColors.Shade(KsTguiColor.Red, 0f);
        var centre = new Vector2(50f, 50f);
        var radians = NeedleDegrees(_value, _minValue, _maxValue) * MathF.PI / 180f;
        var rotation = Matrix3x2.CreateRotation(radians, centre);

        // tgui's polygon, base on the centre line and tip at the top.
        var needle = new[]
        {
            Vector2.Transform(new Vector2(46f, 50f), rotation),
            Vector2.Transform(new Vector2(50f, 0f), rotation),
            Vector2.Transform(new Vector2(54f, 50f), rotation),
        };
        AppendConvex(origin, unit, needle, red, fringe);

        var hub = new Vector2[24];
        for (var i = 0; i < hub.Length; i++)
        {
            var angle = MathF.Tau * i / hub.Length;
            hub[i] = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 8f;
        }

        AppendConvex(origin, unit, hub, red, fringe);
    }

    private void AppendAlert(Vector2 origin, float unit, float fringe, KsTguiColor? range, bool lit)
    {
        var fill = lit && range != null ? KsTguiColors.Shade(range.Value, 0f) : Track;

        // tgui scales the glyph to .9 from the top centre.
        static Vector2 Squash(float x, float y)
        {
            return new Vector2((x - 50f) * 0.9f + 50f, y * 0.9f);
        }

        AppendConvex(origin, unit, [Squash(50f, 13.5f), Squash(64f, 40.5f), Squash(36f, 40.5f)], fill, fringe);

        // The exclamation mark is cut out of the triangle, here painted in the window's colour.
        var cut = _windowBase;
        AppendConvex(origin, unit, [Squash(47f, 22f), Squash(53f, 22f), Squash(51.5f, 32.5f), Squash(48.5f, 32.5f)], cut, fringe);

        var dot = new Vector2[12];
        for (var i = 0; i < dot.Length; i++)
        {
            var angle = MathF.Tau * i / dot.Length;
            dot[i] = Squash(50f + MathF.Cos(angle) * 2.6f, 37f + MathF.Sin(angle) * 2.6f);
        }

        AppendConvex(origin, unit, dot, cut, fringe);
    }

    /// <summary>
    ///     A convex polygon in view box units, as a fan with a soft edge pushed out from its middle.
    /// </summary>
    private void AppendConvex(Vector2 origin, float unit, Vector2[] points, XColor color, float fringe)
    {
        var middle = Vector2.Zero;
        foreach (var point in points)
            middle += point;

        middle /= points.Length;
        var clear = color.WithAlpha(0f);

        for (var i = 0; i < points.Length; i++)
        {
            var next = (i + 1) % points.Length;
            AppendTriangle(Point(origin, unit, middle), color, Point(origin, unit, points[i]), color, Point(origin, unit, points[next]), color);

            var outA = points[i] + Vector2.Normalize(points[i] - middle) * fringe;
            var outB = points[next] + Vector2.Normalize(points[next] - middle) * fringe;
            AppendQuad(
                Point(origin, unit, points[i]), color,
                Point(origin, unit, outA), clear,
                Point(origin, unit, outB), clear,
                Point(origin, unit, points[next]), color);
        }
    }

    private void AppendTriangle(Vector2 a, XColor colorA, Vector2 b, XColor colorB, Vector2 c, XColor colorC)
    {
        _vertices.Add(new DrawVertexUV2DColor(a, colorA));
        _vertices.Add(new DrawVertexUV2DColor(b, colorB));
        _vertices.Add(new DrawVertexUV2DColor(c, colorC));
    }

    private void AppendQuad(Vector2 a, XColor colorA, Vector2 b, XColor colorB, Vector2 c, XColor colorC, Vector2 d, XColor colorD)
    {
        AppendTriangle(a, colorA, b, colorB, c, colorC);
        AppendTriangle(a, colorA, c, colorC, d, colorD);
    }
}
