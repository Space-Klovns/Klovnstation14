using System.Numerics;

namespace Content.Shared._KS14.Occlusion;

/// <summary>
///     Whether a line segment touches a box, edges included.
/// </summary>
/// <remarks>
///     For checking what the occluder tree's ray queries report. Their ray test gives a ray lying exactly along a box's
///         edge - as one along a tile edge does - a NaN or zero distance to every box on that line, behind the ray's start
///         and past its end included, so a query along a tile edge reports every wall on that line as in the way. A hit
///         whose box the segment does not touch is one of those, and is not in the way.
/// </remarks>
public static class KsSegmentBounds
{
    /// <summary>
    ///     A direction component smaller than this is taken as zero: the segment is parallel to the other axis.
    /// </summary>
    private const float AxisParallel = 1e-6f;

    public static bool Touches(Vector2 start, Vector2 end, Box2 bounds)
    {
        var delta = end - start;
        var enter = 0f;
        var leave = 1f;

        return Slab(start.X, delta.X, bounds.Left, bounds.Right, ref enter, ref leave) &&
            Slab(start.Y, delta.Y, bounds.Bottom, bounds.Top, ref enter, ref leave);
    }

    /// <summary>
    ///     Narrows [enter, leave], fractions of the way along the segment, to where it is between low and high on one
    ///         axis; false once that is empty.
    /// </summary>
    private static bool Slab(float start, float delta, float low, float high, ref float enter, ref float leave)
    {
        if (MathF.Abs(delta) < AxisParallel)
            return start >= low && start <= high;

        var first = (low - start) / delta;
        var second = (high - start) / delta;
        enter = MathF.Max(enter, MathF.Min(first, second));
        leave = MathF.Min(leave, MathF.Max(first, second));
        return enter <= leave;
    }
}
