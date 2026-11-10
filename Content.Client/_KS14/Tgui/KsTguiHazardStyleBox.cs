using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     tgui's yellow and black hazard stripes, from the anomalous weather tower's <c>.AnomalyTower__HazardBg</c>:
///         <c>repeating-linear-gradient(45deg, #ffc107 0 15px, #000 15px 30px)</c> in a 3px <c>outset</c> border of <c>#333</c>.
/// </summary>
/// <remarks>
///     <para>
///         The CSS tiles the gradient in 42.42px squares (30px times the square root of two), which repeats it seamlessly, so
///             it is one endless pattern of stripes 15px across. A 45 degree gradient runs from the bottom left corner of each
///             tile to the top right, and the tiles start at the top left of the padding box, which puts a stripe edge through
///             that corner. Stripes run from top left to bottom right, so this is the notice box's pattern mirrored, drawn
///             with the same clipping (<see cref="KsTguiGeometry.ClipStripe"/>) from the top right corner.
///     </para>
///     <para>
///         <c>outset</c> draws the top and left sides in the border colour and the bottom and right darkened by a third, as
///             in the CSS specification's example, which is what Chromium does. The sides meet in mitred corners.
///     </para>
/// </remarks>
public sealed class KsTguiHazardStyleBox : StyleBox
{
    public static readonly Color Yellow = Color.FromHex("#FFC107");
    public static readonly Color Black = Color.FromHex("#000000");
    public static readonly Color BorderLight = Color.FromHex("#333333");
    public static readonly Color BorderDark = Color.FromHex("#222222");

    /// <summary>
    ///     CSS <c>medium</c>, the width a border with no width given has.
    /// </summary>
    public const float BorderWidth = 3f;

    /// <summary>
    ///     Across one stripe, yellow or black.
    /// </summary>
    public const float StripeWidth = 15f;

    private static readonly float Diagonal = MathF.Sqrt(2f);

    private DrawVertexUV2DColor[] _vertices = [];
    private readonly Vector2[] _polygon = new Vector2[6];
    private readonly Vector2[] _scratch = new Vector2[6];

    /// <param name="padding">Between the border and what the box holds.</param>
    public KsTguiHazardStyleBox(float padding = 0f)
    {
        SetContentMarginOverride(Margin.All, BorderWidth + padding);
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var border = BorderWidth * uiScale;

        // A box too small to have an inside (before layout) is all border.
        if (box.Width <= 2f * border || box.Height <= 2f * border)
        {
            handle.DrawRect(box, BorderDark * handle.Modulate);
            return;
        }

        var inner = new UIBox2(box.Left + border, box.Top + border, box.Right - border, box.Bottom - border);
        DrawStripes(handle, inner, uiScale);
        DrawBorder(handle, box, inner);
    }

    private void DrawStripes(DrawingHandleScreen handle, UIBox2 inner, float uiScale)
    {
        handle.DrawRect(inner, Yellow * handle.Modulate);

        var count = StripeTriangles(inner, uiScale, Black * handle.Modulate, out var vertices);
        if (count > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, vertices.AsSpan(0, count));
    }

    /// <summary>
    ///     The black stripes over the yellow inside of the border, as the first <c>count</c> vertices of a triangle list.
    ///         Valid until the next call. A count and not a span: returning a span of a field fails the sandbox's ILVerify.
    /// </summary>
    public int StripeTriangles(UIBox2 inner, float uiScale, Color color, out DrawVertexUV2DColor[] vertices)
    {
        var size = inner.Size;

        // Measured along the gradient p = (x - y) / sqrt(2) from the top left corner, black is [15, 30) mod 30. Mirrored
        //      into s = x' + y from the top right, where x' = width - x, so s = width - (x - y).
        var period = StripeWidth * 2f * uiScale * Diagonal;
        var stripe = StripeWidth * uiScale * Diagonal;
        var first = (int)MathF.Floor(-size.Y / period) - 1;
        var last = (int)MathF.Ceiling(size.X / period);

        // A clipped stripe has at most 6 points, so at most 4 triangles.
        var maxVertices = (last - first + 1) * 4 * 3;
        if (_vertices.Length < maxVertices)
            _vertices = new DrawVertexUV2DColor[maxVertices];

        var written = 0;
        for (var n = first; n <= last; n++)
        {
            var blackStart = n * period + stripe;
            var low = size.X - (blackStart + stripe);
            var high = size.X - blackStart;
            var count = KsTguiGeometry.ClipStripe(size, low, high, _polygon, _scratch);

            for (var point = 1; point < count - 1; point++)
            {
                _vertices[written++] = new DrawVertexUV2DColor(Mirror(inner, _polygon[0]), color);
                _vertices[written++] = new DrawVertexUV2DColor(Mirror(inner, _polygon[point]), color);
                _vertices[written++] = new DrawVertexUV2DColor(Mirror(inner, _polygon[point + 1]), color);
            }
        }

        vertices = _vertices;
        return written;
    }

    private static Vector2 Mirror(UIBox2 inner, Vector2 point)
    {
        return new Vector2(inner.Right - point.X, inner.Top + point.Y);
    }

    private void DrawBorder(DrawingHandleScreen handle, UIBox2 outer, UIBox2 inner)
    {
        if (_vertices.Length < 24)
            _vertices = new DrawVertexUV2DColor[24];

        var light = BorderLight * handle.Modulate;
        var dark = BorderDark * handle.Modulate;
        var written = 0;

        // Each side is a trapezoid from its outer edge to its inner one, so the corners are mitred.
        Quad(ref written, outer.TopLeft, outer.TopRight, inner.TopRight, inner.TopLeft, light);
        Quad(ref written, outer.TopLeft, inner.TopLeft, inner.BottomLeft, outer.BottomLeft, light);
        Quad(ref written, outer.TopRight, outer.BottomRight, inner.BottomRight, inner.TopRight, dark);
        Quad(ref written, outer.BottomLeft, inner.BottomLeft, inner.BottomRight, outer.BottomRight, dark);

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, _vertices.AsSpan(0, written));
    }

    private void Quad(ref int written, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        _vertices[written++] = new DrawVertexUV2DColor(a, color);
        _vertices[written++] = new DrawVertexUV2DColor(b, color);
        _vertices[written++] = new DrawVertexUV2DColor(c, color);
        _vertices[written++] = new DrawVertexUV2DColor(a, color);
        _vertices[written++] = new DrawVertexUV2DColor(c, color);
        _vertices[written++] = new DrawVertexUV2DColor(d, color);
    }

    /// <summary>
    ///     Whether a point, from the top left of the area inside the border, is on a black stripe. The pattern's definition,
    ///         for a test.
    /// </summary>
    public static bool IsBlack(Vector2 point, float uiScale = 1f)
    {
        var along = (point.X - point.Y) / Diagonal / uiScale;
        var phase = along - MathF.Floor(along / (StripeWidth * 2f)) * StripeWidth * 2f;
        return phase >= StripeWidth;
    }
}
