using System.Numerics;

namespace Content.Shared._KS14.MedicalBeam;

/// <summary>
///     Where two medical beams cross, each a straight segment from its holder to its target.
/// </summary>
public static class KsMedicalBeamMath
{
    /// <summary>
    ///     How close, in metres, counts as touching: a beam ending on the mob another starts from does.
    /// </summary>
    private const float Tolerance = 0.001f;

    /// <summary>
    ///     Whether beam <paramref name="from"/>-<paramref name="to"/> crosses beam
    ///         <paramref name="otherFrom"/>-<paramref name="otherTo"/>, and where: the first point along the first beam
    ///         that the other one touches. Touching counts, so a beam onto someone who is beaming another crosses theirs,
    ///         as in tg, where the patient's tile holds the first piece of their own beam.
    /// </summary>
    /// <param name="sharedTarget">
    ///     Both beams are on the same patient, so they always meet there. That alone isn't crossing (tg: beams coming in
    ///         from different sides don't share a tile); only one lying along the other, from the same side, is.
    /// </param>
    public static bool TryGetCrossing(Vector2 from, Vector2 to, Vector2 otherFrom, Vector2 otherTo, bool sharedTarget, out Vector2 crossing)
    {
        crossing = default;
        var direction = to - from;
        var otherDirection = otherTo - otherFrom;
        var length = direction.Length();
        var otherLength = otherDirection.Length();
        if (length < Tolerance || otherLength < Tolerance)
            return false;

        var denominator = Cross(direction, otherDirection);
        var offset = otherFrom - from;

        // Not parallel: one point, if it's on both.
        if (MathF.Abs(denominator) > Tolerance * length * otherLength)
        {
            if (sharedTarget)
                return false;

            var along = Cross(offset, otherDirection) / denominator;
            var otherAlong = Cross(offset, direction) / denominator;
            var slack = Tolerance / length;
            var otherSlack = Tolerance / otherLength;
            if (along < -slack || along > 1f + slack || otherAlong < -otherSlack || otherAlong > 1f + otherSlack)
                return false;

            crossing = from + direction * Math.Clamp(along, 0f, 1f);
            return true;
        }

        // Parallel: only if on one line, overlapping.
        if (MathF.Abs(Cross(offset, direction)) > Tolerance * length)
            return false;

        var unit = direction / length;
        var otherStart = Vector2.Dot(otherFrom - from, unit);
        var otherEnd = Vector2.Dot(otherTo - from, unit);
        var overlapStart = MathF.Max(0f, MathF.Min(otherStart, otherEnd));
        var overlapEnd = MathF.Min(length, MathF.Max(otherStart, otherEnd));
        if (overlapStart > overlapEnd + Tolerance)
            return false;

        // On one patient, the overlap is more than the patient themselves only if both come in from the same side.
        if (sharedTarget && (Vector2.Dot(direction, otherDirection) <= 0f || overlapEnd - overlapStart <= Tolerance))
            return false;

        crossing = from + unit * overlapStart;
        return true;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
