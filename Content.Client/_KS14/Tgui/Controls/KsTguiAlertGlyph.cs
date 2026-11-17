using System.Linq;
using System.Numerics;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     The alert glyph of tgui's RoundGauge, from its SVG path (<c>RoundGauge.js</c>): a triangle with rounded corners and
///         an even-odd "!" (a teardrop and a dot) cut out of it, in its 100 by 50 view box. The cut-outs are holes, not paint
///         in some background colour, so whatever is behind the gauge shows through them. The shape is split into trapezoids
///         between horizontal lines through every vertex, which a plain triangle list can draw with holes in it.
/// </summary>
internal static class KsTguiAlertGlyph
{
    /// <summary>
    ///     One trapezoid with horizontal top (A, B) and bottom (D, C) edges.
    /// </summary>
    public readonly record struct Piece(Vector2 A, Vector2 B, Vector2 C, Vector2 D);

    /// <summary>
    ///     The triangle's outline, which a soft edge is drawn round.
    /// </summary>
    public static readonly Vector2[] Outline =
    [
        new(48.211f, 14.578f), new(48.75f, 13.95f), new(49.35f, 13.55f), new(50f, 13.472f), new(50.65f, 13.55f),
        new(51.25f, 13.95f), new(51.789f, 14.578f),
        new(63.553f, 38.106f), new(63.78f, 38.75f), new(63.76f, 39.45f), new(63.465f, 40.051f), new(63.05f, 40.55f),
        new(62.45f, 40.9f), new(61.764f, 41f),
        new(38.236f, 41f), new(37.55f, 40.9f), new(36.95f, 40.55f), new(36.535f, 40.051f), new(36.24f, 39.45f),
        new(36.22f, 38.75f), new(36.447f, 38.106f),
    ];

    public static readonly Piece[] Pieces = Build();

    private static Piece[] Build()
    {
        var loops = new List<Vector2[]> { Outline, Teardrop(), Dot() };

        var ys = new SortedSet<float>();
        foreach (var loop in loops)
        {
            foreach (var point in loop)
                ys.Add(point.Y);
        }

        var pieces = new List<Piece>();
        var bounds = ys.ToArray();
        for (var band = 0; band < bounds.Length - 1; band++)
        {
            var top = bounds[band];
            var bottom = bounds[band + 1];
            var middle = (top + bottom) / 2f;

            // The edges crossing the band's middle, left to right. Between two vertex heights every edge is straight, so the
            //      pairs of them bound trapezoids exactly. Even-odd: the first and second edge are in, the third is a hole.
            var edges = new List<(float X, Vector2 From, Vector2 To)>();
            foreach (var loop in loops)
            {
                for (var i = 0; i < loop.Length; i++)
                {
                    var from = loop[i];
                    var to = loop[(i + 1) % loop.Length];
                    if (from.Y == to.Y || middle < MathF.Min(from.Y, to.Y) || middle > MathF.Max(from.Y, to.Y))
                        continue;

                    edges.Add((XAt(from, to, middle), from, to));
                }
            }

            edges.Sort((a, b) => a.X.CompareTo(b.X));
            for (var i = 0; i + 1 < edges.Count; i += 2)
            {
                var left = edges[i];
                var right = edges[i + 1];
                pieces.Add(new Piece(
                    new Vector2(XAt(left.From, left.To, top), top),
                    new Vector2(XAt(right.From, right.To, top), top),
                    new Vector2(XAt(right.From, right.To, bottom), bottom),
                    new Vector2(XAt(left.From, left.To, bottom), bottom)));
            }
        }

        return pieces.ToArray();
    }

    private static float XAt(Vector2 from, Vector2 to, float y)
    {
        return from.X + (y - from.Y) * (to.X - from.X) / (to.Y - from.Y);
    }

    /// <summary>
    ///     The "!" stem: <c>M50,32.75 C50,32.75 53,31.805 53,22.25 C53,20.594 51.656,19.25 50,19.25 C48.344,19.25 47,20.594
    ///         47,22.25 C47,31.805 50,32.75 50,32.75</c>.
    /// </summary>
    private static Vector2[] Teardrop()
    {
        var points = new List<Vector2>();
        Cubic(points, new(50f, 32.75f), new(50f, 32.75f), new(53f, 31.805f), new(53f, 22.25f));
        Cubic(points, new(53f, 22.25f), new(53f, 20.594f), new(51.656f, 19.25f), new(50f, 19.25f));
        Cubic(points, new(50f, 19.25f), new(48.344f, 19.25f), new(47f, 20.594f), new(47f, 22.25f));
        Cubic(points, new(47f, 22.25f), new(47f, 31.805f), new(50f, 32.75f), new(50f, 32.75f));
        return points.ToArray();
    }

    /// <summary>
    ///     The "!" dot: a circle of radius 2.583 at (50, 37).
    /// </summary>
    private static Vector2[] Dot()
    {
        var points = new Vector2[16];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = MathF.Tau * (float)i / (float)points.Length;
            points[i] = new Vector2(50f + MathF.Cos(angle) * 2.583f, 37f + MathF.Sin(angle) * 2.583f);
        }

        return points;
    }

    /// <summary>
    ///     Adds a cubic Bezier's points after its start, which the previous curve ended on.
    /// </summary>
    private static void Cubic(List<Vector2> into, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
    {
        const int steps = 6;
        if (into.Count == 0)
            into.Add(p0);

        for (var step = 1; step <= steps; step++)
        {
            var t = (float)step / (float)steps;
            var inverse = 1f - t;
            into.Add(inverse * inverse * inverse * p0 + 3f * inverse * inverse * t * p1 + 3f * inverse * t * t * p2 + t * t * t * p3);
        }
    }
}
