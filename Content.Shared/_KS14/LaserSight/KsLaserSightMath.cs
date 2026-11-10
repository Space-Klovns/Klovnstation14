using System.Numerics;

namespace Content.Shared._KS14.LaserSight;

/// <summary>
///     The geometry of a laser sight, in one place so the raycast and the drawn beam cannot disagree.
/// </summary>
/// <remarks>
///     An entity at world rotation zero faces south (0, -1); <see cref="Angle.ToWorldAngle"/> and
///         <see cref="Angle.ToWorldVec"/> are exact inverses. A gun turned to <c>(target - position).ToWorldAngle()</c>
///         therefore fires along <c>rotation.ToWorldVec()</c>.
/// </remarks>
public static class KsLaserSightMath
{
    /// <summary>
    ///     Unit direction of the beam. A zero <paramref name="angleOffset"/> is straight along the facing, and a
    ///         positive one turns the same way entity rotation does.
    /// </summary>
    public static Vector2 GetDirection(Angle worldRotation, Angle angleOffset)
    {
        return (worldRotation + angleOffset).ToWorldVec();
    }

    /// <summary>
    ///     Beam origin: the local <paramref name="offset"/> rotated by the entity's rotation, as sprite offsets are.
    /// </summary>
    public static Vector2 GetOrigin(Vector2 worldPosition, Angle worldRotation, Vector2 offset)
    {
        return worldPosition + worldRotation.RotateVec(offset);
    }

    /// <summary>
    ///     The beam as a quad of <paramref name="width"/> running <paramref name="length"/> along
    ///         <see cref="GetDirection"/> from <paramref name="origin"/>. Unrotated it runs -Y (south), as rotation
    ///         zero faces south.
    /// </summary>
    public static Box2Rotated GetBeamBox(Vector2 origin, Angle beamRotation, float length, float width)
    {
        var box = new Box2(
            origin.X - width / 2f,
            origin.Y - length,
            origin.X + width / 2f,
            origin.Y);

        return new Box2Rotated(box, beamRotation, origin);
    }

    /// <summary>
    ///     Length in metres of the fade: <c>(1 - fadeStart) * maxLength</c>. It is constant, so a beam cut short by an
    ///         obstacle fades over the same distance as a full one, ending at the cut.
    /// </summary>
    public static float GetFadeLength(float maxLength, float fadeStart)
    {
        return MathF.Max((1f - Math.Clamp(fadeStart, 0f, 1f)) * maxLength, 0f);
    }

    /// <summary>
    ///     Distance in metres from the origin at which the beam starts to fade: <paramref name="length"/> minus
    ///         <see cref="GetFadeLength"/>, never before the origin. The alpha reaches zero exactly at
    ///         <paramref name="length"/>. With no fade (<c>fadeStart</c> of 1) this is <paramref name="length"/>.
    /// </summary>
    public static float GetFadeSplit(float length, float maxLength, float fadeStart)
    {
        length = MathF.Max(length, 0f);
        var fadeLength = GetFadeLength(maxLength, fadeStart);
        if (fadeLength <= 0f)
            return length;

        return MathF.Max(length - fadeLength, 0f);
    }

    /// <summary>
    ///     Alpha multiplier at <paramref name="distance"/> along a beam drawn <paramref name="length"/> long: 1 up to
    ///         <see cref="GetFadeSplit"/>, then linear to 0 at <paramref name="length"/>.
    /// </summary>
    public static float GetAlphaAt(float distance, float length, float maxLength, float fadeStart)
    {
        var split = GetFadeSplit(length, maxLength, fadeStart);
        if (distance <= split || length <= split)
            return 1f;

        return Math.Clamp(1f - (distance - split) / (length - split), 0f, 1f);
    }

    /// <summary>
    ///     Number of vertices <see cref="GetFadeStrip"/> writes.
    /// </summary>
    public const int FadeStripVertexCount = 6;

    /// <summary>
    ///     Fills a triangle strip of <see cref="FadeStripVertexCount"/> vertices covering the same area as
    ///         <see cref="GetBeamBox"/>, as three left/right pairs: at the origin, at <see cref="GetFadeSplit"/> and at
    ///         <paramref name="length"/>. Each vertex gets its alpha multiplier, so the first quad is solid and the
    ///         second is the gradient.
    /// </summary>
    public static void GetFadeStrip(
        Vector2 origin,
        Angle beamRotation,
        float length,
        float width,
        float maxLength,
        float fadeStart,
        Vector2[] positions,
        float[] alphas)
    {
        var direction = beamRotation.ToWorldVec();
        var sideways = new Vector2(-direction.Y, direction.X) * (width / 2f);
        var split = GetFadeSplit(length, maxLength, fadeStart);

        WriteStripPair(origin, direction, sideways, 0f, length, maxLength, fadeStart, positions, alphas, 0);
        WriteStripPair(origin, direction, sideways, split, length, maxLength, fadeStart, positions, alphas, 2);
        WriteStripPair(origin, direction, sideways, length, length, maxLength, fadeStart, positions, alphas, 4);
    }

    private static void WriteStripPair(
        Vector2 origin,
        Vector2 direction,
        Vector2 sideways,
        float distance,
        float length,
        float maxLength,
        float fadeStart,
        Vector2[] positions,
        float[] alphas,
        int index)
    {
        var centre = origin + direction * distance;
        var alpha = GetAlphaAt(distance, length, maxLength, fadeStart);

        positions[index] = centre - sideways;
        positions[index + 1] = centre + sideways;
        alphas[index] = alpha;
        alphas[index + 1] = alpha;
    }
}
