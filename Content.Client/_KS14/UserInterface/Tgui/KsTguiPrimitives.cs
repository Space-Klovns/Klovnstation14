using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     A reusable triangle list for drawing tgui's vector shapes with <see cref="DrawingHandleBase.DrawPrimitives(DrawPrimitiveTopology, ReadOnlySpan{Vector2}, Color)"/>.
///         Shapes are given in a local space that <see cref="Begin"/> maps to pixels, so an icon can be drawn in a
///         0..1 box at any size. The buffer only grows, so steady drawing allocates nothing.
/// </summary>
public sealed class KsTguiPrimitives
{
    private const int CircleSegments = 20;

    // stackalloc doesn't pass the client sandbox's IL verifier, so the vertices live here between draws.
    private Vector2[] _vertices = new Vector2[256];
    private Vector2 _origin;
    private float _scale = 1f;

    public int Count { get; private set; }

    /// <summary>
    ///     Empties the list and maps local point (x, y) to <paramref name="origin"/> + (x, y) * <paramref name="scale"/>.
    /// </summary>
    public void Begin(Vector2 origin, float scale)
    {
        _origin = origin;
        _scale = scale;
        Count = 0;
    }

    public void Draw(DrawingHandleScreen handle, Color color)
    {
        // The span is made here rather than returned from a property: ILVerify rejects a method that returns one.
        if (Count > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, new ReadOnlySpan<Vector2>(_vertices, 0, Count), color);
    }

    public void Triangle(Vector2 a, Vector2 b, Vector2 c)
    {
        if (Count + 3 > _vertices.Length)
            Array.Resize(ref _vertices, _vertices.Length * 2);

        _vertices[Count++] = _origin + a * _scale;
        _vertices[Count++] = _origin + b * _scale;
        _vertices[Count++] = _origin + c * _scale;
    }

    public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        Triangle(a, b, c);
        Triangle(a, c, d);
    }

    public void Rect(float left, float top, float right, float bottom)
    {
        Quad(new Vector2(left, top), new Vector2(right, top), new Vector2(right, bottom), new Vector2(left, bottom));
    }

    public void RectOutline(float left, float top, float right, float bottom, float width)
    {
        Rect(left, top, right, top + width);
        Rect(left, bottom - width, right, bottom);
        Rect(left, top + width, left + width, bottom - width);
        Rect(right - width, top + width, right, bottom - width);
    }

    public void Line(Vector2 from, Vector2 to, float width)
    {
        var direction = to - from;
        if (direction.LengthSquared() <= 0f)
            return;

        var normal = Vector2.Normalize(new Vector2(-direction.Y, direction.X)) * (width / 2f);
        Quad(from + normal, to + normal, to - normal, from - normal);
    }

    /// <summary>
    ///     A ring segment of the given width centred on <paramref name="radius"/>, between two angles in degrees
    ///         measured clockwise on screen from the +x axis (so 270 is straight up).
    /// </summary>
    public void Arc(Vector2 center, float radius, float fromDegrees, float toDegrees, float width, int segments = CircleSegments)
    {
        var inner = radius - width / 2f;
        var outer = radius + width / 2f;
        var from = MathHelper.DegreesToRadians(fromDegrees);
        var span = MathHelper.DegreesToRadians(toDegrees - fromDegrees);

        for (var segment = 0; segment < segments; segment++)
        {
            var directionA = Direction(from + span * segment / segments);
            var directionB = Direction(from + span * (segment + 1) / segments);
            Quad(center + directionA * inner, center + directionA * outer, center + directionB * outer, center + directionB * inner);
        }
    }

    public void Disc(Vector2 center, float radius)
    {
        for (var segment = 0; segment < CircleSegments; segment++)
        {
            Triangle(center,
                center + Direction(MathF.Tau * segment / CircleSegments) * radius,
                center + Direction(MathF.Tau * (segment + 1) / CircleSegments) * radius);
        }
    }

    /// <summary>
    ///     An arrowhead sitting on a circle at <paramref name="atDegrees"/> and pointing along it.
    /// </summary>
    public void ArrowHead(Vector2 center, float radius, float atDegrees, float size, bool clockwise)
    {
        var radial = Direction(MathHelper.DegreesToRadians(atDegrees));
        var tangent = new Vector2(-radial.Y, radial.X) * (clockwise ? 1f : -1f);
        var basePoint = center + radial * radius;
        Triangle(basePoint + radial * size * 0.75f, basePoint - radial * size * 0.75f, basePoint + tangent * size);
    }

    private static Vector2 Direction(float radians)
    {
        return new Vector2(MathF.Cos(radians), MathF.Sin(radians));
    }
}
