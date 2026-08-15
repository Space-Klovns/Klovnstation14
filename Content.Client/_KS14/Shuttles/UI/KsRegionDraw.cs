using System.Numerics;
using Content.Shared._KS14.Sensors;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Shuttles.UI;

/// <summary>
///     Builds a coverage region's screen-space vertices: the apex through the
///         grid matrix (it rides the ship), and the boundary either as world-oriented
///         offsets from the apex (a sensor fan, see
///         <see cref="KsSensorRegionState.WorldOffsets"/>) or through the same grid
///         matrix (a jammer wedge, which follows its mount). TransformNormal applies
///         only the linear part of <paramref name="worldToView"/>, exactly what a
///         translation-free offset needs.
/// </summary>
public static class KsRegionDraw
{
    /// <summary>
    ///     Fills caller-owned scratch and returns the count (a fresh array per region
    ///         per frame was pure churn); slice draws by the count, the tail may be stale.
    /// </summary>
    public static int BuildVerts(KsSensorRegionState region, Matrix3x2 gridToView, Matrix3x2 worldToView, ref Vector2[] into)
    {
        var count = region.Points.Count;
        Extensions.EnsureLength(ref into, count);

        var apexView = Vector2.Transform(region.Points[0], gridToView);
        into[0] = apexView;

        for (var i = 1; i < count; i++)
        {
            into[i] = region.WorldOffsets
                ? apexView + Vector2.TransformNormal(region.Points[i], worldToView)
                : Vector2.Transform(region.Points[i], gridToView);
        }

        return count;
    }

    // A between-push ease of these polygons was tried and removed: eased cones read
    // as morphing lag on the instrument, so regions draw each push raw.
}
