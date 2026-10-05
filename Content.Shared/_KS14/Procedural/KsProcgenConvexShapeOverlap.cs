using System.Numerics;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;

namespace Content.Shared._KS14.Procedural;

/// <summary>
/// Fresh closed-shape overlap for circles and bounded convex polygons, including fixture skin radii.
/// Does not evaluate collision filters, joints, event vetoes, contact freshness or support stability.
/// </summary>
public static class KsProcgenConvexShapeOverlap
{
    public static bool TryCheck(IPhysShape shapeA, Transform poseA, IPhysShape shapeB, Transform poseB,
        out bool overlaps, out string? issue)
    {
        overlaps = false;
        issue = null;
        if (!TryVertices(shapeA, poseA, out var verticesA) || !TryVertices(shapeB, poseB, out var verticesB))
        {
            issue = "UnsupportedSurfaceContactShape";
            return false;
        }
        if (!float.IsFinite(shapeA.Radius) || !float.IsFinite(shapeB.Radius) || shapeA.Radius < 0f || shapeB.Radius < 0f)
        {
            issue = "InvalidSurfaceContactShape";
            return false;
        }
        var radius = (double) shapeA.Radius + (double) shapeB.Radius;
        var distanceSquared = double.MaxValue;
        if (Inside(verticesA[0], verticesB) || Inside(verticesB[0], verticesA))
            distanceSquared = 0.0;
        else
        {
            for (var first = 0; first < verticesA.Length; first++)
            for (var second = 0; second < verticesB.Length; second++)
            {
                var a = verticesA[first];
                var b = verticesA[(first + 1) % verticesA.Length];
                var c = verticesB[second];
                var d = verticesB[(second + 1) % verticesB.Length];
                var distance = Intersects(a, b, c, d) ? 0.0 : Math.Min(
                    Math.Min(PointSegment(a, c, d), PointSegment(b, c, d)),
                    Math.Min(PointSegment(c, a, b), PointSegment(d, a, b)));
                distanceSquared = Math.Min(distanceSquared, distance);
            }
        }
        overlaps = distanceSquared <= radius * radius;
        return true;
    }

    private static bool TryVertices(IPhysShape shape, Transform pose, out Vector2[] vertices)
    {
        vertices = [];
        Vector2[] local;
        switch (shape)
        {
            case PhysShapeCircle circle:
                local = [circle.Position];
                break;
            case PhysShapeAabb box:
                local = [box.LocalBounds.BottomLeft, box.LocalBounds.BottomRight,
                    box.LocalBounds.TopRight, box.LocalBounds.TopLeft];
                break;
            case PolygonShape polygon when polygon.VertexCount is >= 3 and <= 8 &&
                                           polygon.Vertices.Length >= polygon.VertexCount:
                local = polygon.Vertices[..polygon.VertexCount];
                break;
            default:
                return false;
        }
        vertices = new Vector2[local.Length];
        for (var index = 0; index < local.Length; index++)
        {
            var vertex = Transform.Mul(pose, local[index]);
            if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y) ||
                Math.Abs(vertex.X) > 1_000_000f || Math.Abs(vertex.Y) > 1_000_000f)
                return false;
            vertices[index] = vertex;
        }
        if (vertices.Length == 1)
            return true;
        var sign = 0;
        for (var index = 0; index < vertices.Length; index++)
        {
            var cross = Cross(vertices[index], vertices[(index + 1) % vertices.Length],
                vertices[(index + 2) % vertices.Length]);
            if (cross == 0.0)
                return false;
            var nextSign = Math.Sign(cross);
            if (sign != 0 && nextSign != sign)
                return false;
            sign = nextSign;
        }
        for (var edge = 0; edge < vertices.Length; edge++)
        for (var point = 0; point < vertices.Length; point++)
        {
            var side = Math.Sign(Cross(vertices[edge], vertices[(edge + 1) % vertices.Length], vertices[point]));
            if (side != 0 && side != sign)
                return false;
        }
        return true;
    }

    private static bool Inside(Vector2 point, Vector2[] polygon)
    {
        if (polygon.Length == 1)
            return point == polygon[0];
        var sign = 0;
        for (var index = 0; index < polygon.Length; index++)
        {
            var nextSign = Math.Sign(Cross(polygon[index], polygon[(index + 1) % polygon.Length], point));
            if (nextSign == 0)
                continue;
            if (sign != 0 && nextSign != sign)
                return false;
            sign = nextSign;
        }
        return true;
    }

    private static bool Intersects(Vector2 a, Vector2 b, Vector2 c, Vector2 d) =>
        Math.Sign(Cross(a, b, c)) * Math.Sign(Cross(a, b, d)) < 0 &&
        Math.Sign(Cross(c, d, a)) * Math.Sign(Cross(c, d, b)) < 0;

    private static double Cross(Vector2 a, Vector2 b, Vector2 c) =>
        ((double) b.X - (double) a.X) * ((double) c.Y - (double) a.Y) -
        ((double) b.Y - (double) a.Y) * ((double) c.X - (double) a.X);

    private static double PointSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var dx = (double) end.X - (double) start.X;
        var dy = (double) end.Y - (double) start.Y;
        var px = (double) point.X - (double) start.X;
        var py = (double) point.Y - (double) start.Y;
        var lengthSquared = dx * dx + dy * dy;
        var fraction = lengthSquared == 0.0 ? 0.0 : Math.Clamp((px * dx + py * dy) / lengthSquared, 0.0, 1.0);
        var x = px - fraction * dx;
        var y = py - fraction * dy;
        return x * x + y * y;
    }
}
