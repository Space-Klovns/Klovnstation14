using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Client._KS14.Lobby;

/// <summary>
///     Maps screen-pixel rects into the normalised quad space the <c>KsLobbyContrast</c> shader reads, with the
///         origin at the top-left. Pure, so it is unit tested.
/// </summary>
public static class KsLobbyContrastMath
{
    /// <summary>
    ///     How far outside 0..1 a mapped rect may reach. Not clamped to 0..1 itself: a region running off the
    ///         edge of the picture must stay solid up to that edge instead of feathering out at it.
    /// </summary>
    public const float OverreachLimit = 2f;

    /// <summary>
    ///     Maps <paramref name="regionPixels"/> (padded by <paramref name="paddingPixels"/>) into the space of
    ///         <paramref name="quadPixels"/>, the rect the shader's quad is drawn into. Returns
    ///         (minX, minY, maxX, maxY), or <see cref="Vector4.Zero"/> when nothing of it lands on the quad.
    /// </summary>
    public static Vector4 RegionToQuadSpace(UIBox2 regionPixels, UIBox2 quadPixels, float paddingPixels)
    {
        var quadWidth = quadPixels.Width;
        var quadHeight = quadPixels.Height;
        if (quadWidth <= 0f || quadHeight <= 0f || regionPixels.Width <= 0f || regionPixels.Height <= 0f)
            return Vector4.Zero;

        var minX = (regionPixels.Left - paddingPixels - quadPixels.Left) / quadWidth;
        var minY = (regionPixels.Top - paddingPixels - quadPixels.Top) / quadHeight;
        var maxX = (regionPixels.Right + paddingPixels - quadPixels.Left) / quadWidth;
        var maxY = (regionPixels.Bottom + paddingPixels - quadPixels.Top) / quadHeight;

        if (maxX <= 0f || maxY <= 0f || minX >= 1f || minY >= 1f)
            return Vector4.Zero;

        return new Vector4(
            MathF.Max(minX, -OverreachLimit),
            MathF.Max(minY, -OverreachLimit),
            MathF.Min(maxX, OverreachLimit),
            MathF.Min(maxY, OverreachLimit));
    }
}
