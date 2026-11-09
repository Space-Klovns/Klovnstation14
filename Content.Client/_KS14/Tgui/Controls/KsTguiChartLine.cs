using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's Chart.Line (<c>Chart.js</c>): a polyline through <see cref="Data"/>, scaled so the data's own minimum and
///         maximum (or <see cref="RangeX"/> and <see cref="RangeY"/>) span the box, y upwards, and optionally filled down to
///         the bottom. Like tgui's it has no size of its own: give it one. tgui draws the stroke and the fill through four
///         extra points that close the polygon off the box, so a range that leaves room after the last point shows a stub
///         running from it to the right edge; the same points are used here.
/// </summary>
public sealed class KsTguiChartLine : Control
{
    private readonly List<Vector2> _data = new();

    private float _strokeWidth = 2f;

    public KsTguiChartLine()
    {
        RectClipContent = true;
    }

    /// <summary>
    ///     The points, as x and y.
    /// </summary>
    public IReadOnlyList<Vector2> Data => _data;

    /// <summary>
    ///     tgui's <c>rangeX</c>: the x values the width spans, instead of the data's own.
    /// </summary>
    public (float Min, float Max)? RangeX { get; set; }

    /// <summary>
    ///     tgui's <c>rangeY</c>.
    /// </summary>
    public (float Min, float Max)? RangeY { get; set; }

    /// <summary>
    ///     tgui's <c>fillColor</c>; null is <c>none</c>.
    /// </summary>
    public Color? FillColor { get; set; }

    /// <summary>
    ///     tgui's <c>strokeColor</c>, white by default.
    /// </summary>
    public Color StrokeColor { get; set; } = Color.White;

    /// <summary>
    ///     tgui's <c>strokeWidth</c>, in pixels (2 by default).
    /// </summary>
    public float StrokeWidth
    {
        get => _strokeWidth;
        set => _strokeWidth = value;
    }

    public void SetData(IEnumerable<Vector2> data)
    {
        _data.Clear();
        _data.AddRange(data);
    }

    /// <summary>
    ///     The points of tgui's polyline for a box of this size, with y counted from the top, as they are drawn. Scaling is
    ///         <c>(v - min) / (max - min) * size</c> on each axis; the four closing points follow the data's, and the flip of
    ///         <c>scale(1, -1)</c> is applied last. A flat axis divides by zero, and the point it makes is not a number.
    /// </summary>
    public static List<Vector2> ComputePoints(
        IReadOnlyList<Vector2> data,
        Vector2 size,
        (float Min, float Max)? rangeX,
        (float Min, float Max)? rangeY,
        float strokeWidth)
    {
        var points = new List<Vector2>();
        if (data.Count == 0)
            return points;

        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        foreach (var point in data)
        {
            minX = MathF.Min(minX, point.X);
            maxX = MathF.Max(maxX, point.X);
            minY = MathF.Min(minY, point.Y);
            maxY = MathF.Max(maxY, point.Y);
        }

        if (rangeX is { } x)
        {
            minX = x.Min;
            maxX = x.Max;
        }

        if (rangeY is { } y)
        {
            minY = y.Min;
            maxY = y.Max;
        }

        foreach (var point in data)
            points.Add(new Vector2((point.X - minX) / (maxX - minX) * size.X, (point.Y - minY) / (maxY - minY) * size.Y));

        var first = points[0];
        var last = points[^1];
        points.Add(new Vector2(size.X + strokeWidth, last.Y));
        points.Add(new Vector2(size.X + strokeWidth, -strokeWidth));
        points.Add(new Vector2(-strokeWidth, -strokeWidth));
        points.Add(new Vector2(-strokeWidth, first.Y));

        for (var i = 0; i < points.Count; i++)
            points[i] = new Vector2(points[i].X, size.Y - points[i].Y);

        return points;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var points = ComputePoints(_data, Size, RangeX, RangeY, _strokeWidth);
        if (points.Count == 0)
            return;

        foreach (var point in points)
        {
            // A flat axis made a point that is not a number: tgui's polyline is invalid, and nothing is drawn.
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
                return;
        }

        for (var i = 0; i < points.Count; i++)
            points[i] *= UIScale;

        if (FillColor is { } fill)
            DrawFill(handle, points, fill * handle.Modulate);

        DrawStroke(handle, points, StrokeColor * handle.Modulate, _strokeWidth * UIScale);
    }

    /// <summary>
    ///     The area under the whole polyline down to the bottom edge of the box, cut into a trapezoid under each segment,
    ///         the closing points included, which is what the polygon's fill covers.
    /// </summary>
    private void DrawFill(DrawingHandleScreen handle, List<Vector2> points, Color color)
    {
        var bottom = PixelHeight + _strokeWidth * UIScale;
        var vertices = new List<DrawVertexUV2DColor>();
        for (var i = 0; i + 1 < points.Count; i++)
        {
            // The last four points are off the box and only close the polygon; its fill is under the data and the two
            //      stretches from the first and last point to the box's edges.
            var from = points[i];
            var to = points[i + 1];
            if (i >= _data.Count)
                break;

            AddTrapezoid(vertices, from, to, bottom, color);
        }

        // The stretch left of the first point, and the one right of the last, at their own heights.
        var firstPoint = points[0];
        var lastPoint = points[_data.Count - 1];
        AddTrapezoid(vertices, new Vector2(-_strokeWidth * UIScale, firstPoint.Y), firstPoint, bottom, color);
        AddTrapezoid(vertices, lastPoint, new Vector2(PixelWidth + _strokeWidth * UIScale, lastPoint.Y), bottom, color);

        if (vertices.Count > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, vertices.ToArray());
    }

    private static void AddTrapezoid(List<DrawVertexUV2DColor> vertices, Vector2 from, Vector2 to, float bottom, Color color)
    {
        var fromBottom = new Vector2(from.X, bottom);
        var toBottom = new Vector2(to.X, bottom);
        vertices.Add(new DrawVertexUV2DColor(from, color));
        vertices.Add(new DrawVertexUV2DColor(to, color));
        vertices.Add(new DrawVertexUV2DColor(toBottom, color));
        vertices.Add(new DrawVertexUV2DColor(from, color));
        vertices.Add(new DrawVertexUV2DColor(toBottom, color));
        vertices.Add(new DrawVertexUV2DColor(fromBottom, color));
    }

    /// <summary>
    ///     A quad along each segment, with a bevel at each join so a steep turn leaves no gap.
    /// </summary>
    private void DrawStroke(DrawingHandleScreen handle, List<Vector2> points, Color color, float width)
    {
        var vertices = new List<DrawVertexUV2DColor>();
        var half = width / 2f;
        Vector2? previousNormal = null;
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var delta = points[i + 1] - points[i];
            if (delta.LengthSquared() <= 0f)
                continue;

            var normal = Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * half;
            var a = points[i] + normal;
            var b = points[i] - normal;
            var c = points[i + 1] - normal;
            var d = points[i + 1] + normal;
            vertices.Add(new DrawVertexUV2DColor(a, color));
            vertices.Add(new DrawVertexUV2DColor(b, color));
            vertices.Add(new DrawVertexUV2DColor(c, color));
            vertices.Add(new DrawVertexUV2DColor(a, color));
            vertices.Add(new DrawVertexUV2DColor(c, color));
            vertices.Add(new DrawVertexUV2DColor(d, color));

            if (previousNormal is { } before)
            {
                vertices.Add(new DrawVertexUV2DColor(points[i], color));
                vertices.Add(new DrawVertexUV2DColor(points[i] + before, color));
                vertices.Add(new DrawVertexUV2DColor(points[i] + normal, color));
                vertices.Add(new DrawVertexUV2DColor(points[i], color));
                vertices.Add(new DrawVertexUV2DColor(points[i] - before, color));
                vertices.Add(new DrawVertexUV2DColor(points[i] - normal, color));
            }

            previousNormal = normal;
        }

        if (vertices.Count > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, vertices.ToArray());
    }
}
