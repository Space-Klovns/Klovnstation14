using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;
using XColor = Robust.Shared.Maths.Color;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's RoundGauge: a half ring split into coloured <see cref="AddRange">ranges</see>, a red needle from the
///         centre, an alert triangle that lights in the colour of the range the value is in, and the value in text below
///         (an <see cref="KsTguiAnimatedNumber"/>, so it eases as the needle does not). Drawn as triangles in device pixels from
///         the same 100 by 50 view box tgui's SVG uses, with soft edges, so it stays sharp at any UI scale. As in tgui's CSS
///         the needle takes .2 s to reach a new value, and while in alert it shakes and the alert pulses.
/// </summary>
public sealed class KsTguiRoundGauge : Control
{
    /// <summary>
    ///     tgui's <c>.RoundGauge</c> is 2.6em by 1.3em, times <see cref="GaugeSize"/>.
    /// </summary>
    private const float BaseWidth = 31.2f;

    private const float BaseHeight = 15.6f;

    /// <summary>
    ///     <c>margin-bottom: 0.2em</c>, an em of the gauge's own font-size (<c>size</c> em), so it grows with <see cref="GaugeSize"/>.
    /// </summary>
    private const float TextGap = 2.4f;

    /// <summary>
    ///     The ring's dash arithmetic, <c>stroke-dasharray</c> over the circle's circumference: a range of length <c>len</c>
    ///         is drawn <c>50 * pi * len</c> long on a circle of <c>90 * pi</c>, which is 200 degrees, not 180, per unit of
    ///         length. What passes the right-hand end is below the view box and clipped away.
    /// </summary>
    private const float ArcHalfTurnsPerUnit = 200f / 180f;

    /// <summary>
    ///     The track is <c>stroke-dasharray: 50 * pi</c> with the same offset, so it is the circle's last 160 degrees: it
    ///         starts 20 degrees above the left horizontal, not on it.
    /// </summary>
    private const float TrackStartHalfTurns = 20f / 180f;

    /// <summary>
    ///     How far, in device pixels, the soft edge reaches past the shape.
    /// </summary>
    private const float Fringe = 0.75f;

    /// <summary>
    ///     <c>--round-gauge-transition</c>, which the needle's <c>transform</c> takes.
    /// </summary>
    private const float NeedleSeconds = 0.2f;

    /// <summary>
    ///     <c>--round-gauge-alert-animation</c>.
    /// </summary>
    private const float AlertSeconds = 1f;

    /// <summary>
    ///     <c>--round-gauge-needle-alert-animation</c>.
    /// </summary>
    private const float ShakeSeconds = 0.1f;

    /// <summary>
    ///     <c>--round-gauge-needle-alert-rotation</c>.
    /// </summary>
    private const float ShakeRangeDegrees = 2.5f;

    /// <summary>
    ///     <c>hsla(0, 0%, 100%, 0.1)</c> over a section, which the idle track and the idle alert are. A browser blends in sRGB,
    ///         so this works out the colour CSS arrives at.
    /// </summary>
    private static readonly XColor Idle = KsTguiPalettes.Translucent(XColor.White, 0.1f, KsTguiPalettes.Section);

    /// <summary>
    ///     What a gauge in a section draws over.
    /// </summary>
    private static readonly XColor Backdrop = KsTguiPalettes.Section;

    private readonly KsTguiAnimatedNumber _text = new() { Align = Robust.Client.UserInterface.Controls.Label.AlignMode.Center };
    private readonly List<(KsTguiColor Color, float Min, float Max)> _ranges = new();
    private readonly List<DrawVertexUV2DColor> _vertices = new(512);

    // The window theme's, from style properties; the default theme's where the sheet doesn't say.
    private Color _ring = KsTguiColors.Shade(KsTguiColor.Default, 0f);

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        // The theme's primary, which is what a gauge without ranges is drawn in: tgui gives it the one range, primary.
        _ring = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyGaugeRing, out var ring) ? ring : KsTguiColors.Shade(KsTguiColor.Default, 0f);
        InvalidateMeasure();
    }

    private float _value;
    private float _minValue = 1f; // tgui-core defaults it to 1, odd as that is
    private float _maxValue = 1f;
    private float _size = 1f;

    private bool _hasValue;
    private bool _needlePlaced;
    private bool _needleMoving;
    private float _needleFrom;
    private float _needleTo = -90f;
    private float _needleShown = -90f;
    private float _needleElapsed;
    private bool _wasActive;
    private float _activeElapsed;

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
            _hasValue = true;
            RefreshNeedle();
        }
    }

    public float MinValue
    {
        get => _minValue;
        set
        {
            _minValue = value;
            RefreshNeedle();
        }
    }

    public float MaxValue
    {
        get => _maxValue;
        set
        {
            _maxValue = value;
            RefreshNeedle();
        }
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
    ///     What the ring is drawn in when there are no ranges.
    /// </summary>
    public Color DefaultRingColor => _ring;

    /// <summary>
    ///     The idle track and alert colour.
    /// </summary>
    public static Color IdleColor => Idle;

    /// <summary>
    ///     Whether a point of the 100 by 50 view box, before the glyph's scaling, is on the alert glyph, which has the "!" cut
    ///         out of it.
    /// </summary>
    public static bool AlertGlyphCovers(Vector2 point)
    {
        foreach (var piece in KsTguiAlertGlyph.Pieces)
        {
            if (point.Y < piece.A.Y || point.Y > piece.D.Y)
                continue;

            var t = (point.Y - piece.A.Y) / (piece.D.Y - piece.A.Y);
            var left = piece.A.X + (piece.D.X - piece.A.X) * t;
            var right = piece.B.X + (piece.C.X - piece.B.X) * t;
            if (point.X >= left && point.X <= right)
                return true;
        }

        return false;
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
    ///     The part of the dial a range's ring is drawn on, as fractions of the half turn. tgui scales the ends without
    ///         clamping and draws <see cref="ArcHalfTurnsPerUnit"/> of arc per unit of length from the start, and what lies
    ///         past the dial is clipped.
    /// </summary>
    public static (float Start, float End) RangeArc(float rangeMin, float rangeMax, float min, float max)
    {
        var span = max - min;
        if (span == 0f)
            return (0f, 0f);

        var start = (rangeMin - min) / span;
        var end = (rangeMax - min) / span;
        var arcEnd = start + (end - start) * ArcHalfTurnsPerUnit;
        return (Math.Clamp(start, 0f, 1f), Math.Clamp(arcEnd, 0f, 1f));
    }

    /// <summary>
    ///     The range the value's place on the dial falls in, the first that holds it with both ends included, as tgui's
    ///         <c>keyOfMatchingRange</c>; null when none does.
    /// </summary>
    public KsTguiColor? MatchingRange()
    {
        // tgui's default is one range, primary, over the whole dial.
        if (_ranges.Count == 0)
            return KsTguiColor.Primary;

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
    ///     Whether the value is past <see cref="AlertAfter"/>, or under <see cref="AlertBefore"/>.
    /// </summary>
    public bool InAlert
    {
        get
        {
            return AlertAfter is { } after && _value > after || AlertBefore is { } before && _value < before;
        }
    }

    /// <summary>
    ///     tgui's <c>active</c> class: in alert with the needle on a range, so the alert pulses in that range's colour and the
    ///         needle shakes.
    /// </summary>
    public bool AlertActive => InAlert && MatchingRange() != null;

    /// <summary>
    ///     Where the needle is drawn, in the degrees of <see cref="NeedleDegrees"/>, before any shake.
    /// </summary>
    public float NeedleShown => _needleShown;

    /// <summary>
    ///     The angle the needle is drawn at: <see cref="NeedleShown"/>, and its shake while the alert is active.
    /// </summary>
    public float NeedleDrawn => _needleShown + (AlertActive ? ShakeDegrees(_activeElapsed) : 0f);

    /// <summary>
    ///     The opacity the alert is drawn at while active.
    /// </summary>
    public float AlertOpacityNow => AlertOpacity(_activeElapsed);

    /// <summary>
    ///     Whether the needle is still on its way to the value.
    /// </summary>
    public bool NeedleMoving => _needleMoving;

    /// <summary>
    ///     CSS's <c>cubic-bezier(x1, y1, x2, y2)</c> at a progress of 0 to 1.
    /// </summary>
    public static float CubicBezier(float x1, float y1, float x2, float y2, float progress)
    {
        if (progress <= 0f)
            return 0f;

        if (progress >= 1f)
            return 1f;

        // x grows with t for every curve CSS allows, so halving always finds the t for this progress.
        var low = 0f;
        var high = 1f;
        var t = progress;
        for (var i = 0; i < 16; i++)
        {
            if (BezierAxis(x1, x2, t) < progress)
                low = t;
            else
                high = t;

            t = (low + high) / 2f;
        }

        return BezierAxis(y1, y2, t);
    }

    private static float BezierAxis(float first, float second, float t)
    {
        var inverse = 1f - t;
        return 3f * inverse * inverse * t * first + 3f * inverse * t * t * second + t * t * t;
    }

    /// <summary>
    ///     How far the needle has turned towards a new value after a fraction of its .2 s, as CSS's <c>ease-in-out</c>.
    /// </summary>
    public static float NeedleEase(float progress)
    {
        return CubicBezier(0.42f, 0f, 0.58f, 1f, progress);
    }

    /// <summary>
    ///     The shake on top of the needle's angle, this long into the alert. tgui's keyframes give only 0% and 50%, +2.5 and
    ///         -2.5 degrees, so 100% is the needle's own line, and it jumps back to +2.5 as the next cycle starts. Each leg is
    ///         CSS's default <c>ease</c>.
    /// </summary>
    public static float ShakeDegrees(float seconds)
    {
        var phase = seconds % ShakeSeconds / ShakeSeconds;
        return phase < 0.5f
            ? MathHelper.Lerp(ShakeRangeDegrees, -ShakeRangeDegrees, ShakeEase(phase * 2f))
            : MathHelper.Lerp(-ShakeRangeDegrees, 0f, ShakeEase((phase - 0.5f) * 2f));
    }

    private static float ShakeEase(float progress)
    {
        return CubicBezier(0.25f, 0.1f, 0.25f, 1f, progress);
    }

    /// <summary>
    ///     The alert's opacity, this long into the alert: .1 to 1 and back in a second, with an easing that overshoots, and
    ///         an opacity can't go past 0 or 1.
    /// </summary>
    public static float AlertOpacity(float seconds)
    {
        var phase = seconds % AlertSeconds / AlertSeconds;
        var opacity = phase < 0.5f
            ? MathHelper.Lerp(0.1f, 1f, AlertEase(phase * 2f))
            : MathHelper.Lerp(1f, 0.1f, AlertEase((phase - 0.5f) * 2f));

        return Math.Clamp(opacity, 0f, 1f);
    }

    private static float AlertEase(float progress)
    {
        return CubicBezier(0.34f, 1.56f, 0.64f, 1f, progress);
    }

    private void RefreshNeedle()
    {
        var target = NeedleDegrees(_value, _minValue, _maxValue);

        // tgui draws the first value in place, and a window sets its value and scale in one go.
        if (!_needlePlaced)
        {
            _needleTo = target;
            _needleShown = target;
            return;
        }

        if (target == _needleTo)
            return;

        // A CSS transition starts over from wherever the needle has got to.
        _needleFrom = _needleShown;
        _needleTo = target;
        _needleElapsed = 0f;
        _needleMoving = true;
    }

    /// <summary>
    ///     Moves the animations on by this much time.
    /// </summary>
    public void Advance(float seconds)
    {
        _needlePlaced |= _hasValue;

        if (_needleMoving)
        {
            _needleElapsed += seconds;
            var progress = _needleElapsed / NeedleSeconds;
            _needleMoving = progress < 1f;
            _needleShown = _needleMoving ? MathHelper.Lerp(_needleFrom, _needleTo, NeedleEase(progress)) : _needleTo;
        }

        var active = AlertActive;
        _activeElapsed = active && _wasActive ? (_activeElapsed + seconds) % AlertSeconds : 0f;
        _wasActive = active;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        Advance(args.DeltaSeconds);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _text.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
        var width = MathF.Max(BaseWidth * _size, _text.DesiredSize.X);
        return new Vector2(width, BaseHeight * _size + TextGap * _size + _text.DesiredSize.Y);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        _text.Arrange(UIBox2.FromDimensions(0f, BaseHeight * _size + TextGap * _size, finalSize.X, _text.DesiredSize.Y));
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

        var active = AlertActive;
        if (AlertAfter != null || AlertBefore != null)
            AppendAlert(origin, unit, fringe, active ? MatchingRange() : null);

        AppendRing(origin, unit, fringe, TrackStartHalfTurns, 1f, Idle);
        if (_ranges.Count == 0)
        {
            // The one range tgui gives it is [0, 1]: 200 degrees of arc, all of it that shows.
            AppendRing(origin, unit, fringe, 0f, 1f, _ring);
        }
        else
        {
            foreach (var (color, min, max) in _ranges)
            {
                var (start, end) = RangeArc(min, max, _minValue, _maxValue);
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
        var radians = NeedleDrawn * MathF.PI / 180f;
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

    private void AppendAlert(Vector2 origin, float unit, float fringe, KsTguiColor? activeRange)
    {
        // The glyph is one colour with the "!" a hole in it. Active, it is the range's colour at the pulse's opacity, over
        //      whatever is behind.
        var fill = Idle;
        if (activeRange is { } range)
            fill = KsTguiPalettes.Translucent(KsTguiColors.Shade(range, 0f), AlertOpacityNow, Backdrop);

        var squashed = new Vector2[KsTguiAlertGlyph.Outline.Length];
        for (var i = 0; i < squashed.Length; i++)
            squashed[i] = Squash(KsTguiAlertGlyph.Outline[i]);

        foreach (var piece in KsTguiAlertGlyph.Pieces)
        {
            AppendQuad(
                Point(origin, unit, Squash(piece.A)), fill,
                Point(origin, unit, Squash(piece.B)), fill,
                Point(origin, unit, Squash(piece.C)), fill,
                Point(origin, unit, Squash(piece.D)), fill);
        }

        // A soft edge round the outside only; the "!" is too small to need one.
        var middle = Vector2.Zero;
        foreach (var point in squashed)
            middle += point;

        middle /= squashed.Length;
        var clear = fill.WithAlpha(0f);
        for (var i = 0; i < squashed.Length; i++)
        {
            var next = (i + 1) % squashed.Length;
            var outA = squashed[i] + Vector2.Normalize(squashed[i] - middle) * fringe;
            var outB = squashed[next] + Vector2.Normalize(squashed[next] - middle) * fringe;
            AppendQuad(
                Point(origin, unit, squashed[i]), fill,
                Point(origin, unit, outA), clear,
                Point(origin, unit, outB), clear,
                Point(origin, unit, squashed[next]), fill);
        }
    }

    /// <summary>
    ///     tgui scales the glyph to .9 from the top centre.
    /// </summary>
    private static Vector2 Squash(Vector2 point)
    {
        return new Vector2((point.X - 50f) * 0.9f + 50f, point.Y * 0.9f);
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
