using System.Linq;
using System.Numerics;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;

namespace Content.Shared._KS14.Procedural;

public static partial class KsProcgenConvexShapeOverlap
{
    /// <summary>
    /// Continuous translation of a convex fixture at a fixed orientation. The convex hull of
    /// start/end vertices plus the original skin radius is the exact swept closed shape.
    /// This is geometric clearance, not an engine movement or contact-response simulation.
    /// </summary>
    public static bool TrySweep(IPhysShape movingShape, Transform start, Vector2 translation,
        IPhysShape obstacleShape, Transform obstacle, out bool overlaps, out string? issue)
    {
        overlaps = false;
        issue = null;
        if (!float.IsFinite(translation.X) || !float.IsFinite(translation.Y) ||
            !TryVertices(movingShape, start, out var first) ||
            !TryVertices(obstacleShape, obstacle, out var second))
        {
            issue = "UnsupportedClearanceSweepShape";
            return false;
        }
        if (!float.IsFinite(movingShape.Radius) || !float.IsFinite(obstacleShape.Radius) ||
            movingShape.Radius < 0f || obstacleShape.Radius < 0f)
        {
            issue = "InvalidClearanceSweepShape";
            return false;
        }
        var points = new List<Vector2>();
        foreach (var point in first)
        {
            var end = point + translation;
            if (!float.IsFinite(end.X) || !float.IsFinite(end.Y) ||
                Math.Abs(end.X) > 1_000_000f || Math.Abs(end.Y) > 1_000_000f)
            {
                issue = "UnsupportedClearanceSweepShape";
                return false;
            }
            points.Add(point);
            points.Add(end);
        }
        var sorted = points.Distinct().OrderBy(point => point.X).ThenBy(point => point.Y).ToArray();
        if (sorted.Length == 1)
        {
            overlaps = CheckVertices(sorted, second, (double) movingShape.Radius + obstacleShape.Radius);
            return true;
        }
        // Monotone chain, at most sixteen input points; discard collinear interior vertices.
        var hull = new List<Vector2>();
        foreach (var point in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], point) <= 0.0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        var lowerCount = hull.Count;
        for (var index = sorted.Length - 2; index >= 0; index--)
        {
            var point = sorted[index];
            while (hull.Count > lowerCount && Cross(hull[^2], hull[^1], point) <= 0.0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        hull.RemoveAt(hull.Count - 1);
        overlaps = CheckVertices(hull.ToArray(), second, (double) movingShape.Radius + obstacleShape.Radius);
        return true;
    }
}
