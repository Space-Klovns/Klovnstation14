using System.Numerics;

namespace Content.Client._KS14.Graphics;

/// <summary>
///     Snaps overlay drawing to whole screen pixels the way the engine snaps a grid's sprites and tiles, so what an
///         overlay draws on a grid doesn't shimmer against the floor and sprites while the eye moves.
/// </summary>
public static class KsPixelSnap
{
    /// <summary>
    ///     The world-space nudge that puts <paramref name="worldPosition"/> on a whole screen pixel. Add it to a
    ///         grid's world matrix's translation before drawing in that grid's frame.
    /// </summary>
    /// <remarks>
    ///     A copy of the engine's <c>Clyde.GetPixelSnapOffset</c>, which it applies to every grid before drawing
    ///         that grid's sprites and tiles. It is internal, so it can't be called from content.
    /// </remarks>
    public static Vector2 GetOffset(
        Vector2 worldPosition,
        Vector2 viewPosition,
        Angle viewRotation,
        Vector2 viewScale,
        Vector2 viewportSize)
    {
        var viewPositionRelative = viewRotation.RotateVec(worldPosition - viewPosition);
        var screenPosition = viewPositionRelative * viewScale + viewportSize / 2f;
        var screenOffset = screenPosition.Rounded() - screenPosition;
        var viewOffset = screenOffset / viewScale;
        return (-viewRotation).RotateVec(viewOffset);
    }
}
