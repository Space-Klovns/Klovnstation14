using System.Numerics;

namespace Content.Shared._KS14.Mortar;

/// <summary>
///     The mortar's aim as a compass bearing (zero is north, turning clockwise) and a distance, and as an offset on the map's
///         axes (+X east, +Y north), one to the other.
/// </summary>
public static class KsMortarAim
{
    /// <summary>
    ///     How many points the compass words name: N, NE, E and so on.
    /// </summary>
    public const int CompassPoints = 8;

    /// <summary>
    ///     The offset from the mortar of a shot at <paramref name="bearing"/> and <paramref name="distance"/>.
    /// </summary>
    public static Vector2 ToOffset(Angle bearing, float distance)
    {
        var theta = bearing.Theta;
        return new Vector2((float) Math.Sin(theta), (float) Math.Cos(theta)) * distance;
    }

    /// <summary>
    ///     The compass bearing of <paramref name="offset"/>, between 0 and 360 degrees; zero for no offset at all.
    /// </summary>
    public static Angle ToBearing(Vector2 offset)
    {
        if (offset.LengthSquared() <= 0f)
            return Angle.Zero;

        return Angle.FromDegrees(NormalizeDegrees(Math.Atan2(offset.X, offset.Y) * 180d / Math.PI));
    }

    /// <summary>
    ///     <paramref name="degrees"/> turned into 0 (inclusive) to 360 (exclusive).
    /// </summary>
    public static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360d;
        if (normalized < 0d)
            normalized += 360d;

        return normalized >= 360d ? 0d : normalized;
    }

    /// <summary>
    ///     Which of the <see cref="CompassPoints"/> a bearing in degrees is nearest: 0 north, 1 north-east, and on clockwise.
    /// </summary>
    public static int CompassPoint(double degrees)
    {
        var point = (int) Math.Round(NormalizeDegrees(degrees) / (360d / CompassPoints));
        return point % CompassPoints;
    }
}
