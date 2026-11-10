using System.Numerics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     The shapes tgui's CSS gets for free and the engine has no box for. Pure, so what can't be seen headlessly can at
///         least be asserted on.
/// </summary>
public static class KsTguiGeometry
{
    /// <summary>
    ///     The title bar's height. tgui's <c>Window__rest</c> starts here, so the logo is centred in what's below it.
    /// </summary>
    public const float TitleBarHeight = 32f;

    private const float LogoAspectWidth = 425f;
    private const float LogoAspectHeight = 200f;

    /// <summary>
    ///     tgui's <c>background-size: 70% 70%</c>.
    /// </summary>
    private const float LogoFill = 0.7f;

    /// <summary>
    ///     Points written by <see cref="RoundedLoop"/>.
    /// </summary>
    public static int RoundedLoopLength(int segments)
    {
        return (segments + 1) * 4;
    }

    /// <summary>
    ///     Writes a rounded rectangle's outline, clockwise from the top left corner's start.
    /// </summary>
    /// <param name="fringe">
    ///     How far the corners' arcs are pushed out from their true radius, at their middles. It tapers to nothing where an
    ///         arc meets a straight edge, so the sides stay exactly on the box and only the corners get a soft edge.
    /// </param>
    public static void RoundedLoop(
        UIBox2 box,
        float topLeft,
        float topRight,
        float bottomRight,
        float bottomLeft,
        int segments,
        float fringe,
        Vector2[] output)
    {
        var limit = MathF.Min(box.Width, box.Height) / 2f;
        topLeft = MathF.Min(topLeft, limit);
        topRight = MathF.Min(topRight, limit);
        bottomRight = MathF.Min(bottomRight, limit);
        bottomLeft = MathF.Min(bottomLeft, limit);

        var index = 0;
        WriteArc(new Vector2(box.Left + topLeft, box.Top + topLeft), topLeft, MathF.PI, segments, fringe, output, ref index);
        WriteArc(new Vector2(box.Right - topRight, box.Top + topRight), topRight, MathF.PI * 1.5f, segments, fringe, output, ref index);
        WriteArc(new Vector2(box.Right - bottomRight, box.Bottom - bottomRight), bottomRight, 0f, segments, fringe, output, ref index);
        WriteArc(new Vector2(box.Left + bottomLeft, box.Bottom - bottomLeft), bottomLeft, MathF.PI * 0.5f, segments, fringe, output, ref index);

        // On a small radius the pushed-out arc can pass the box's sides, which a border's corner doesn't.
        for (var i = 0; i < index; i++)
            output[i] = Vector2.Clamp(output[i], box.TopLeft, box.BottomRight);
    }

    private static void WriteArc(
        Vector2 center,
        float radius,
        float startAngle,
        int segments,
        float fringe,
        Vector2[] output,
        ref int index)
    {
        for (var i = 0; i <= segments; i++)
        {
            var progress = (float)i / segments;
            var angle = startAngle + progress * MathF.PI * 0.5f;
            var pointRadius = radius > 0f ? radius + fringe * MathF.Sin(progress * MathF.PI) : 0f;
            output[index++] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * pointRadius;
        }
    }

    /// <summary>
    ///     A box with its sides moved in, never turned inside out. A control is often smaller than its border or its title
    ///         bar for a frame, a window on its first especially, and an inverted box is a fatal assert in Debug.
    /// </summary>
    public static UIBox2 Deflate(UIBox2 box, float left, float top, float right, float bottom)
    {
        var newLeft = MathF.Min(box.Left + left, box.Right);
        var newTop = MathF.Min(box.Top + top, box.Bottom);

        return new UIBox2(newLeft, newTop, MathF.Max(box.Right - right, newLeft), MathF.Max(box.Bottom - bottom, newTop));
    }

    /// <summary>
    ///     Where the Nanotrasen logo goes: fitted into 70% of each axis of the area, keeping its aspect, centred.
    /// </summary>
    public static UIBox2 FitLogo(UIBox2 area)
    {
        return FitLogo(area, LogoAspectWidth / LogoAspectHeight);
    }

    /// <summary>
    ///     As <see cref="FitLogo(UIBox2)"/>, for a logo of another shape: an svg's <c>background-size: 70% 70%</c> keeps its
    ///         aspect and fits inside that box.
    /// </summary>
    public static UIBox2 FitLogo(UIBox2 area, float aspect)
    {
        var scale = MathF.Min(area.Width * LogoFill / aspect, area.Height * LogoFill);

        var size = new Vector2(aspect, 1f) * scale;
        var center = area.Center;
        return new UIBox2(center - size / 2f, center + size / 2f);
    }

    /// <summary>
    ///     Clips one of a NoticeBox's stripes to a box. Works in distance from the bottom right corner, which is where
    ///         tgui's <c>repeating-linear-gradient(-45deg, ...)</c> starts: <c>x</c> is leftwards, <c>y</c> upwards, and the
    ///         stripe is everywhere <c>low &lt;= x + y &lt;= high</c>.
    /// </summary>
    /// <param name="output">Receives the stripe as a convex polygon. Needs room for 6 points.</param>
    /// <param name="scratch">Working space, as long as <paramref name="output"/>.</param>
    /// <returns>Points written, which is under 3 if the stripe misses the box.</returns>
    public static int ClipStripe(Vector2 size, float low, float high, Vector2[] output, Vector2[] scratch)
    {
        scratch[0] = Vector2.Zero;
        scratch[1] = new Vector2(size.X, 0f);
        scratch[2] = size;
        scratch[3] = new Vector2(0f, size.Y);

        var count = ClipToSum(scratch, 4, low, keepAbove: true, output);
        return ClipToSum(output, count, high, keepAbove: false, scratch, copyTo: output);
    }

    private static int ClipToSum(
        Vector2[] input,
        int count,
        float limit,
        bool keepAbove,
        Vector2[] output,
        Vector2[]? copyTo = null)
    {
        var written = 0;
        for (var i = 0; i < count; i++)
        {
            var current = input[i];
            var next = input[(i + 1) % count];
            var currentSum = current.X + current.Y;
            var nextSum = next.X + next.Y;
            var currentInside = keepAbove ? currentSum >= limit : currentSum <= limit;
            var nextInside = keepAbove ? nextSum >= limit : nextSum <= limit;

            if (currentInside)
                output[written++] = current;

            if (currentInside != nextInside)
                output[written++] = Vector2.Lerp(current, next, (limit - currentSum) / (nextSum - currentSum));
        }

        if (copyTo != null)
            Array.Copy(output, copyTo, written);

        return written;
    }
}
