namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     The numbers behind tgui-core's <c>Knob</c> and <c>DraggableControl</c>, apart from the control, so they can be asserted
///         on without a screen. Angles are degrees, clockwise from straight up unless said otherwise, and the ring's are
///         clockwise from 3 o'clock like an svg circle's.
/// </summary>
public static class KsTguiKnobMath
{
    /// <summary>
    ///     <c>.Knob</c>'s <c>width</c> and <c>height</c>, 2.6em, in pixels at a size of 1 (<c>font-size</c> 12px).
    /// </summary>
    public const float BaseSize = 31.2f;

    /// <summary>
    ///     The pixels of one em at a size of 1.
    /// </summary>
    public const float Em = 12f;

    /// <summary>
    ///     The circle's radius, (2.6 - 2 * (0.17 + 0.3)) / 2 em.
    /// </summary>
    public const float CircleRadiusEm = 0.83f;

    /// <summary>
    ///     The ring's radius: its svg's content box is 2.6em less <c>--knob-inner-padding</c> each side, 2.26em, and the circle
    ///         is r=50 of 100 in it.
    /// </summary>
    public const float RingRadiusEm = 1.13f;

    /// <summary>
    ///     The ring's stroke width: 8 of those 100 units.
    /// </summary>
    public const float RingWidthEm = 0.1808f;

    /// <summary>
    ///     Where both rings start: the svgs are <c>rotateZ(135deg)</c>.
    /// </summary>
    public const float RingStartDegrees = 135f;

    /// <summary>
    ///     The bipolar fill starts at 12 o'clock: <c>rotateZ(270deg)</c> from 3 o'clock.
    /// </summary>
    public const float BipolarStartDegrees = 270f;

    /// <summary>
    ///     The track's sweep: <c>stroke-dasharray: 75 * pi</c> of a circle that is 100 * pi round.
    /// </summary>
    public const float TrackSweepDegrees = 270f;

    /// <summary>
    ///     The track's alpha, which replaces the ring colour's own.
    /// </summary>
    public const float TrackAlpha = 0.15f;

    /// <summary>
    ///     tgui's <c>scale</c>: where the value is in the range, unclamped. A range with no span reads as 0.
    /// </summary>
    public static float Scale(float value, float min, float max)
    {
        var span = max - min;
        var fraction = (value - min) / span;
        return float.IsFinite(fraction) ? fraction : 0f;
    }

    /// <summary>
    ///     The cursor's turn from straight up, <c>min((f - .5) * 270, 225)</c>: -135 at the minimum, 0 in the middle, 135 at the
    ///         maximum.
    /// </summary>
    public static float CursorDegrees(float value, float min, float max)
    {
        return MathF.Min((Scale(value, min, max) - 0.5f) * 270f, 225f);
    }

    /// <summary>
    ///     Where the fill starts and how far it sweeps, clockwise positive, from <c>stroke-dashoffset</c>: a dash of 100 * pi
    ///         pushed back by <c>max((2 - 1.5f) * pi * 50, 0)</c>, or <c>(2.75 - 1.5f)</c> for a bipolar knob. The sweep is
    ///         held to one turn either way. A bipolar fill below the middle is the dash's wrapped end, counter-clockwise.
    /// </summary>
    public static (float Start, float Sweep) FillArc(float fillFraction, bool bipolar)
    {
        if (!bipolar)
            return (RingStartDegrees, Math.Clamp(fillFraction * 270f, 0f, 360f));

        return (BipolarStartDegrees, Math.Clamp((fillFraction - 0.5f) * 270f, -360f, 360f));
    }

    /// <summary>
    ///     The pixels of drag that sweep the whole range when the knob is not given a <c>stepPixelSize</c>:
    ///         <c>offsetWidth / ((max - min) / step)</c>.
    /// </summary>
    public static float DefaultStepPixelSize(float width, float min, float max, float step)
    {
        var size = width / ((max - min) / step);
        return float.IsFinite(size) && size > 0f ? size : 1f;
    }

    /// <summary>
    ///     DraggableControl's <c>handleDragMove</c>: <c>clamp(floor(original / step) * step + trunc(drag / stepPixelSize) * step)</c>,
    ///         with <paramref name="drag"/> the pointer's travel along the drag matrix since the press. Relative to the press,
    ///         so nothing jumps to the click, and the truncation is a dead zone of one step around where it started.
    /// </summary>
    public static float DragValue(float original, float drag, float step, float stepPixelSize, float min, float max)
    {
        var value = MathF.Floor(original / step) * step + MathF.Truncate(drag / stepPixelSize) * step;
        return Math.Clamp(value, min, MathF.Max(min, max));
    }

    /// <summary>
    ///     A typed value: held to the range unless <paramref name="unclamped"/>. Null when it is not a number, which cancels.
    /// </summary>
    public static float? TypedValue(float parsed, float min, float max, bool unclamped)
    {
        if (float.IsNaN(parsed))
            return null;

        return unclamped ? parsed : Math.Clamp(parsed, min, MathF.Max(min, max));
    }

    /// <summary>
    ///     The Gaussian edge of a CSS <c>box-shadow</c> blur as a share of the shadow's alpha: a half at the shape's edge,
    ///         going to 0 outwards. <c>erfc(d / (sigma * sqrt 2)) / 2</c>, where sigma is half the blur radius.
    /// </summary>
    public static float ShadowFalloff(float distance, float blurRadius)
    {
        var sigma = blurRadius / 2f;
        return 0.5f * Erfc(distance / (sigma * MathF.Sqrt(2f)));
    }

    /// <summary>
    ///     Abramowitz and Stegun 7.1.26, to 1.5e-7.
    /// </summary>
    public static float Erfc(float x)
    {
        var sign = MathF.Sign(x);
        var absolute = MathF.Abs(x);
        var t = 1f / (1f + 0.3275911f * absolute);
        var polynomial = ((((1.061405429f * t - 1.453152027f) * t) + 1.421413741f) * t - 0.284496736f) * t + 0.254829592f;
        var erf = 1f - polynomial * t * MathF.Exp(-absolute * absolute);
        return 1f - sign * erf;
    }
}
